using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.EtatsParticuliers;

/// <summary>
/// Visibilité des états particuliers (donnée sensible, AFF-24) : le gestionnaire, le CPMT et l'infirmier les voient tous ;
/// un employeur ne voit que ceux qu'il a déclarés ; les autres profils n'y ont pas accès.
/// </summary>
internal static class VisibiliteEtats
{
    public static bool PeutConsulter(ICurrentUser user, PerimetreUtilisateur perimetre) =>
        perimetre.EstExterne || user.HasPermission(Permissions.PersonneEcrire) || user.HasPermission(Permissions.DossierSanteLire);

    public static IEnumerable<EtatParticulier> Visibles(Personne personne, PerimetreUtilisateur perimetre) =>
        personne.EtatsParticuliers.Where(e => !perimetre.EstExterne || (e.AffilieDeclarantId is { } a && a == perimetre.AffilieExterne));
}

public sealed record ListerEtatsParticuliers(Guid PersonneId);

public sealed class ListerEtatsParticuliersHandler(AccesPersonnes acces, ICurrentUser currentUser, PerimetreUtilisateur perimetre)
    : IQueryHandler<ListerEtatsParticuliers, IReadOnlyList<EtatParticulierDto>>
{
    public async Task<Result<IReadOnlyList<EtatParticulierDto>>> HandleAsync(ListerEtatsParticuliers query, CancellationToken cancellationToken)
    {
        if (!VisibiliteEtats.PeutConsulter(currentUser, perimetre))
        {
            return Error.Forbidden("etat-particulier.interdit", "Les états particuliers ne sont accessibles qu'au gestionnaire, au CPMT et au déclarant.");
        }

        var personne = await acces.ChargerAsync(query.PersonneId, Permissions.PersonneLire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        return VisibiliteEtats.Visibles(personne.Value, perimetre)
            .OrderBy(e => e.DateDebut)
            .Select(e => new EtatParticulierDto(e.Id, e.Type, e.DateDebut, e.DateFin))
            .ToList();
    }
}

/// <summary>
/// AFF-23, AFF-24 : déclaration par l'employeur ou le SEPP d'une grossesse, d'un allaitement, d'un travail de nuit
/// ou d'un jeune travailleur. Publie <c>personnes.etat-particulier-declare</c> avec une catégorie générique (ARC-06).
/// </summary>
public sealed record DeclarerEtatParticulier(Guid PersonneId, TypeEtatParticulier Type, DateOnly DateDebut, DateOnly? DateFin);

public sealed class DeclarerEtatParticulierHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<DeclarerEtatParticulier, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DeclarerEtatParticulier command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var etat = Regles.Appliquer("etat-particulier.invalide",
            () => personne.Value.DeclarerEtatParticulier(command.Type, command.DateDebut, command.DateFin, perimetre.AffilieExterne));
        if (!etat.IsSuccess)
        {
            return etat.Error!;
        }

        EvenementsIntegration.Publier(personne.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return etat.Value.Id;
    }
}

/// <summary>Fin (anticipée) d'un état particulier, dernier jour inclus ; republie l'état courant.</summary>
public sealed record TerminerEtatParticulier(Guid PersonneId, Guid EtatParticulierId, DateOnly DateFin);

public sealed class TerminerEtatParticulierHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<TerminerEtatParticulier, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TerminerEtatParticulier command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        if (!VisibiliteEtats.Visibles(personne.Value, perimetre).Any(e => e.Id == command.EtatParticulierId))
        {
            return Error.NotFound("etat-particulier.inconnu", "État particulier inconnu.");
        }

        var resultat = Regles.Appliquer("etat-particulier.invalide",
            () => personne.Value.TerminerEtatParticulier(command.EtatParticulierId, command.DateFin));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        if (resultat.Value)
        {
            EvenementsIntegration.Publier(personne.Value, outbox);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
