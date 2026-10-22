using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Application.Consultation;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Application.Travailleur;

/// <summary>Contrôle commun du portail travailleur : permission dédiée puis personne lue dans le jeton, jamais dans la requête.</summary>
internal static class AccesTravailleur
{
    public static Result<Guid> PersonneDuJeton(ICurrentUser user, IContextePersonne contexte, string permission)
    {
        if (Acces.Permission(user, permission) is { } interdit)
        {
            return interdit;
        }

        return contexte.PersonneId is { } personneId
            ? personneId
            : Error.Forbidden("perimetre.interdit", "Le jeton ne désigne aucune personne : accès refusé.");
    }
}

/// <summary>
/// POR-12, SAN-02 : demande du travailleur connecté (visite de pré-reprise, consultation spontanée) chez un de ses
/// employeurs. La personne est celle du jeton (claim <c>personne_id</c>) ; type et date seulement, jamais le motif (ARC-06).
/// </summary>
public sealed record EnregistrerMaDemande(Guid AffilieId, TypeObligation Type, DateOnly? DateDemande = null);

public sealed class EnregistrerMaDemandeHandler(
    IDemandeRepository demandes, IProjectionRepository projections, RecalculObligations recalcul, IUnitOfWork unitOfWork,
    ICurrentUser user, IContextePersonne contexte, TimeProvider clock)
    : ICommandHandler<EnregistrerMaDemande, Guid>
{
    public async Task<Result<Guid>> HandleAsync(EnregistrerMaDemande command, CancellationToken cancellationToken)
    {
        var personne = AccesTravailleur.PersonneDuJeton(user, contexte, Permissions.ObligationDemander);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var aujourdHui = clock.AujourdHui();

        // Le travailleur ne demande qu'auprès d'un affilié qui l'occupe (occupation projetée depuis Personnes).
        if (!await projections.OccupationActiveAsync(personne.Value, command.AffilieId, aujourdHui, cancellationToken))
        {
            return Error.Forbidden("perimetre.interdit", "Vous n'êtes pas occupé chez cet employeur.");
        }

        DemandeTravailleur demande;
        try
        {
            demande = DemandeTravailleur.Enregistrer(
                personne.Value, command.AffilieId, command.Type, command.DateDemande ?? aujourdHui, aujourdHui, user.UserId);
        }
        catch (DomainException ex)
        {
            return Error.Validation("demande.invalide", ex.Message);
        }

        demandes.Add(demande);
        await recalcul.RecalculerAsync([personne.Value], cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return demande.Id;
    }
}

/// <summary>
/// POR-11, SAN-12 : examens à planifier de la personne du jeton (statuts ouverts sans rendez-vous à venir). Le travailleur
/// voit toutes ses propres obligations, y compris celles qu'il a demandées ou liées à la maternité : le masquage des types
/// confidentiels ne vaut que pour les profils externes qui consultent un autre que lui (employeur, SIPP).
/// </summary>
public sealed record ListerMesExamensAPlanifier(Language Langue);

public sealed class ListerMesExamensAPlanifierHandler(
    IObligationRepository obligations, ICurrentUser user, IContextePersonne contexte, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<ListerMesExamensAPlanifier, IReadOnlyList<ObligationDto>>
{
    public async Task<Result<IReadOnlyList<ObligationDto>>> HandleAsync(ListerMesExamensAPlanifier query, CancellationToken cancellationToken)
    {
        var personne = AccesTravailleur.PersonneDuJeton(user, contexte, Permissions.ObligationLirePropres);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var aujourdHui = clock.AujourdHui();
        return (await obligations.ListParPersonneAsync(personne.Value, cancellationToken))
            .Where(o => o.PersonneId == personne.Value && MachineEtatsObligation.EstAPlanifier(o.Statut))
            .OrderBy(o => o.Echeance)
            .ThenBy(o => o.Cle, StringComparer.Ordinal)
            .Select(o => ObligationDto.De(o, aujourdHui, options.HorizonDuesJours, query.Langue))
            .ToList();
    }
}
