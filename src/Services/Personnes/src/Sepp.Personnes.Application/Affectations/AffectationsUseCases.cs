using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.Affectations;

/// <summary>AFF-22 : affectations aux postes ; historique complet si aucune date n'est donnée, sinon celles en vigueur à la date.</summary>
public sealed record ListerAffectations(Guid PersonneId, DateOnly? Date);

public sealed class ListerAffectationsHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre)
    : IQueryHandler<ListerAffectations, IReadOnlyList<AffectationDto>>
{
    public async Task<Result<IReadOnlyList<AffectationDto>>> HandleAsync(ListerAffectations query, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(query.PersonneId, Permissions.PersonneLire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        return perimetre.OccupationsVisibles(personne.Value)
            .SelectMany(o => o.Affectations.Select(a => a.Dto(o.Id)))
            .Where(a => query.Date is not { } date || new Validity(a.ValideDu, a.ValideJusquAu).Contains(date))
            .OrderBy(a => a.ValideDu)
            .ToList();
    }
}

/// <summary>AFF-22 : rattache le travailleur à un poste d'un site, du <c>ValideDu</c> au <c>ValideJusquAu</c> exclu (DAT-04).</summary>
public sealed record Affecter(Guid PersonneId, Guid OccupationId, Guid PosteId, Guid SiteId, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed class AffecterHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<Affecter, Guid>
{
    public async Task<Result<Guid>> HandleAsync(Affecter command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        // AFF-23 : l'entreprise utilisatrice affecte l'intérimaire à ses propres postes.
        var occupation = personne.Value.Occupations.SingleOrDefault(o => o.Id == command.OccupationId);
        if (occupation is null || !perimetre.PeutAgirPour(occupation))
        {
            return Error.NotFound("occupation.inconnue", "Occupation inconnue.");
        }

        var affectation = Regles.Appliquer("affectation.invalide",
            () => personne.Value.Affecter(command.OccupationId, command.PosteId, command.SiteId, command.ValideDu, command.ValideJusquAu));
        if (!affectation.IsSuccess)
        {
            return affectation.Error!;
        }

        EvenementsIntegration.Publier(personne.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return affectation.Value.Id;
    }
}

/// <summary>AFF-22, DAT-04 : clôture d'une affectation à partir d'une date (exclusive).</summary>
public sealed record TerminerAffectation(Guid PersonneId, Guid AffectationId, DateOnly APartirDu);

public sealed class TerminerAffectationHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<TerminerAffectation, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TerminerAffectation command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        if (!AffectationVisible(personne.Value, command.AffectationId, perimetre))
        {
            return Error.NotFound("affectation.inconnue", "Affectation inconnue.");
        }

        var resultat = Regles.Appliquer("affectation.invalide", () => personne.Value.TerminerAffectation(command.AffectationId, command.APartirDu));
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

    internal static bool AffectationVisible(Personne personne, Guid affectationId, PerimetreUtilisateur perimetre) =>
        perimetre.OccupationsVisibles(personne).Any(o => o.Affectations.Any(a => a.Id == affectationId));
}

/// <summary>AFF-22, DAT-04 : changement de poste ou de site à une date ; l'ancienne affectation est clôturée, une nouvelle est créée.</summary>
public sealed record ChangerAffectation(Guid PersonneId, Guid AffectationId, Guid PosteId, Guid SiteId, DateOnly APartirDu);

public sealed class ChangerAffectationHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<ChangerAffectation, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ChangerAffectation command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        if (!TerminerAffectationHandler.AffectationVisible(personne.Value, command.AffectationId, perimetre))
        {
            return Error.NotFound("affectation.inconnue", "Affectation inconnue.");
        }

        var nouvelle = Regles.Appliquer("affectation.invalide",
            () => personne.Value.ChangerAffectation(command.AffectationId, command.PosteId, command.SiteId, command.APartirDu));
        if (!nouvelle.IsSuccess)
        {
            return nouvelle.Error!;
        }

        EvenementsIntegration.Publier(personne.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return nouvelle.Value.Id;
    }
}
