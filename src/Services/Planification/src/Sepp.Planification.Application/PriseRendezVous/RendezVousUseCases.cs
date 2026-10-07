using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Planification;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;

namespace Sepp.Planification.Application.PriseRendezVous;

public sealed record RendezVousDto(
    Guid Id,
    Guid CreneauId,
    Guid PersonneId,
    Guid AffilieId,
    Guid RessourceId,
    Guid LieuId,
    DateTimeOffset Debut,
    DateTimeOffset Fin,
    string TypeActe,
    StatutRendezVous Statut,
    MotifAnnulation? MotifAnnulation,
    IReadOnlyList<Guid> ObligationIds,
    OrigineRendezVous Origine,
    bool Urgent,
    DateTimeOffset? ArriveeA,
    DateTimeOffset? AppeleA,
    Guid? SalleId,
    Guid? ReconvocationDeId);

internal static class RendezVousMapping
{
    public static RendezVousDto ToDto(this RendezVous r) => new(
        r.Id, r.CreneauId, r.PersonneId, r.AffilieId, r.RessourceId, r.LieuId, r.Debut, r.Fin, r.TypeActe, r.Statut, r.MotifAnnulation,
        r.ObligationIds, r.Origine, r.Urgent, r.ArriveeA, r.AppeleA, r.SalleId, r.ReconvocationDeId);

    public static Error Inconnu() => Error.NotFound("rendez-vous.inconnu", "Rendez-vous inconnu.");
}

/// <summary>SAN-10 : rendez-vous pris par le planificateur dans un créneau, avec convocation à l'unité.</summary>
public sealed record PlanifierRendezVous(
    Guid CreneauId,
    Guid PersonneId,
    Guid AffilieId,
    IReadOnlyList<Guid>? ObligationIds,
    string? Canal,
    bool? Recommande,
    bool Convoquer = true);

public sealed class PlanifierRendezVousHandler(
    ICreneauRepository creneaux,
    IObligationRepository obligations,
    PriseDeRendezVous prise,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<PlanifierRendezVous, Guid>
{
    public async Task<Result<Guid>> HandleAsync(PlanifierRendezVous command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        var canal = Enumerations.Optionnel<CanalConvocation>(command.Canal, "Canal");
        if (!canal.IsSuccess)
        {
            return canal.Error!;
        }

        var creneau = await creneaux.GetAsync(command.CreneauId, cancellationToken);
        if (creneau is null)
        {
            return Error.NotFound("creneau.inconnu", "Créneau inconnu.");
        }

        var obligationIds = command.ObligationIds ?? [];
        if (await VerifierObligationsAsync(obligations, obligationIds, command.PersonneId, command.AffilieId, cancellationToken) is { } invalide)
        {
            return invalide;
        }

        var rdv = await prise.PlanifierAsync(creneau,
            new DemandeRendezVous(command.PersonneId, command.AffilieId, obligationIds, OrigineRendezVous.Planificateur, false)
            {
                Canal = canal.Value,
                Recommande = command.Recommande,
                Convoquer = command.Convoquer,
            },
            cancellationToken);
        if (!rdv.IsSuccess)
        {
            return rdv.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rdv.Value.Id;
    }

    /// <summary>Les obligations couvertes appartiennent à la personne et à l'affilié du rendez-vous et ne sont pas déjà couvertes.</summary>
    internal static async Task<Error?> VerifierObligationsAsync(IObligationRepository obligations, IReadOnlyList<Guid> ids, Guid personneId, Guid affilieId,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        var trouvees = await obligations.ListAsync(ids, cancellationToken);
        if (trouvees.Count != ids.Distinct().Count() || trouvees.Any(o => o.PersonneId != personneId || o.AffilieId != affilieId))
        {
            return Error.Validation("obligation.inconnue", "Une obligation est inconnue ou ne concerne pas cette personne et cet affilié.");
        }

        return trouvees.Any(o => !o.EstAPlanifier)
            ? Error.Conflict("obligation.deja-couverte", "Une des obligations est déjà couverte par un rendez-vous.")
            : null;
    }
}

public sealed record ObtenirRendezVous(Guid Id);

public sealed class ObtenirRendezVousHandler(IRendezVousRepository rendezVous, IPerimetreUtilisateur perimetre, ICurrentUser user)
    : IQueryHandler<ObtenirRendezVous, RendezVousDto>
{
    public async Task<Result<RendezVousDto>> HandleAsync(ObtenirRendezVous query, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlanificationLire) && !user.HasPermission(Permissions.PlanificationReserver))
        {
            return Error.Forbidden("planification.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");
        }

        var rdv = await rendezVous.GetAsync(query.Id, cancellationToken);
        if (rdv is null)
        {
            return RendezVousMapping.Inconnu();
        }

        return Perimetre.PeutVoir(perimetre, rdv) ? rdv.ToDto() : RendezVousMapping.Inconnu();
    }
}

/// <summary>Contrôle de périmètre des externes (SAN-12) : travailleur = lui-même, employeur = ses affiliés.</summary>
internal static class Perimetre
{
    public static bool PeutVoir(IPerimetreUtilisateur perimetre, RendezVous rdv) =>
        !perimetre.EstExterne || PeutAgirPour(perimetre, rdv.PersonneId, rdv.AffilieId);

    public static bool PeutAgirPour(IPerimetreUtilisateur perimetre, Guid personneId, Guid affilieId) =>
        perimetre.PersonneId is { } personne ? personne == personneId : perimetre.PeutAccederAffilie(affilieId);
}

public sealed record ListerRendezVous(Guid? PersonneId, Guid? AffilieId, Guid? RessourceId, Guid? LieuId, DateTimeOffset? Du, DateTimeOffset? Au);

public sealed class ListerRendezVousHandler(IRendezVousRepository rendezVous, ICurrentUser user) : IQueryHandler<ListerRendezVous, IReadOnlyList<RendezVousDto>>
{
    public async Task<Result<IReadOnlyList<RendezVousDto>>> HandleAsync(ListerRendezVous query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        if (query.PersonneId is null && query.AffilieId is null && query.RessourceId is null && query.LieuId is null && (query.Du is null || query.Au is null))
        {
            return Error.Validation("rendez-vous.filtre-obligatoire", "Précisez une personne, un affilié, une ressource, un lieu ou une période.");
        }

        var resultat = await rendezVous.RechercherAsync(
            new CritereRendezVous
            {
                PersonneId = query.PersonneId,
                AffilieId = query.AffilieId,
                RessourceId = query.RessourceId,
                LieuId = query.LieuId,
                Du = query.Du,
                Au = query.Au,
            },
            cancellationToken);
        return resultat.OrderBy(r => r.Debut).Take(1000).Select(r => r.ToDto()).ToList();
    }
}

/// <summary>Annule un rendez-vous : le créneau est libéré et les obligations redeviennent à planifier.</summary>
public sealed record AnnulerRendezVous(Guid RendezVousId, string Motif);

public sealed class AnnulerRendezVousHandler(Annulation annulation, IRendezVousRepository rendezVous, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<AnnulerRendezVous, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AnnulerRendezVous command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        if (!Enumerations.TryParse<MotifAnnulation>(command.Motif, out var motif))
        {
            return Error.Validation("rendez-vous.motif-inconnu",
                $"Motif d'annulation inconnu : '{command.Motif}'. Valeurs admises : {string.Join(", ", Enum.GetNames<MotifAnnulation>())}.");
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null)
        {
            return RendezVousMapping.Inconnu();
        }

        if (await annulation.AnnulerAsync(rdv, motif, cancellationToken) is { } erreur)
        {
            return erreur;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Annulation commune (planificateur, réservation en ligne, replanification sans solution).</summary>
public sealed class Annulation(ICreneauRepository creneaux, IObligationRepository obligations, IIntegrationEventOutbox outbox, TimeProvider horloge)
{
    public async Task<Error?> AnnulerAsync(RendezVous rdv, MotifAnnulation motif, CancellationToken cancellationToken)
    {
        var creneau = await creneaux.GetAsync(rdv.CreneauId, cancellationToken);
        if (creneau is null)
        {
            return Error.NotFound("creneau.inconnu", "Créneau du rendez-vous introuvable.");
        }

        try
        {
            rdv.Annuler(motif, creneau, horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("rendez-vous.statut", ex.Message);
        }

        foreach (var obligation in await obligations.ListAsync(rdv.ObligationIds, cancellationToken))
        {
            obligation.Decouvrir(rdv.Id);
        }

        outbox.Add(new RendezVousAnnule(rdv.Id, rdv.PersonneId, motif.ToString()));
        return null;
    }
}

/// <summary>SAN-13 : la personne ne s'est pas présentée ; ses obligations redeviennent à planifier (reconvocation).</summary>
public sealed record ConstaterAbsence(Guid RendezVousId);

public sealed class ConstaterAbsenceHandler(
    IRendezVousRepository rendezVous,
    IObligationRepository obligations,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<ConstaterAbsence, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ConstaterAbsence command, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlanificationGerer) && !user.HasPermission(Permissions.PlanificationSalleAttente))
        {
            return Error.Forbidden("planification.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null)
        {
            return RendezVousMapping.Inconnu();
        }

        try
        {
            rdv.ConstaterAbsence(horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("rendez-vous.statut", ex.Message);
        }

        foreach (var obligation in await obligations.ListAsync(rdv.ObligationIds, cancellationToken))
        {
            obligation.Decouvrir(rdv.Id);
        }

        outbox.Add(new AbsenceRendezVousConstatee(rdv.Id, rdv.PersonneId, rdv.AffilieId, rdv.Debut, rdv.ObligationIds));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// SAN-13 : reconvocation après une absence, dans le créneau indiqué ou, à défaut, dans le premier créneau libre du même
/// type d'acte (même lieu d'abord, puis tout lieu) dans l'horizon de recherche. Les mêmes obligations sont couvertes.
/// </summary>
public sealed record Reconvoquer(Guid RendezVousId, Guid? CreneauId, string? Canal);

public sealed class ReconvoquerHandler(
    IRendezVousRepository rendezVous,
    ICreneauRepository creneaux,
    PriseDeRendezVous prise,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<Reconvoquer, Guid>
{
    public async Task<Result<Guid>> HandleAsync(Reconvoquer command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        var canal = Enumerations.Optionnel<CanalConvocation>(command.Canal, "Canal");
        if (!canal.IsSuccess)
        {
            return canal.Error!;
        }

        var manque = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (manque is null)
        {
            return RendezVousMapping.Inconnu();
        }

        if (manque.Statut != StatutRendezVous.Absent)
        {
            return Error.Conflict("rendez-vous.non-manque", "Seul un rendez-vous manqué donne lieu à une reconvocation.");
        }

        var dejaReconvoque = await rendezVous.RechercherAsync(
            new CritereRendezVous { PersonneId = manque.PersonneId, Statuts = RendezVous.StatutsActifs }, cancellationToken);
        if (dejaReconvoque.Any(r => r.ReconvocationDeId == manque.Id))
        {
            return Error.Conflict("rendez-vous.deja-reconvoque", "Cette personne a déjà été reconvoquée pour ce rendez-vous.");
        }

        Creneau? creneau;
        if (command.CreneauId is { } creneauId)
        {
            creneau = await creneaux.GetAsync(creneauId, cancellationToken);
            if (creneau is null)
            {
                return Error.NotFound("creneau.inconnu", "Créneau inconnu.");
            }
        }
        else
        {
            var maintenant = horloge.GetUtcNow();
            var recherche = new RechercheCreneau(manque.TypeActe, maintenant, maintenant.AddDays(parametres.Options.HorizonRechercheJours), manque.Urgent)
            {
                PersonneId = manque.PersonneId,
            };
            creneau = await prise.TrouverCreneauAsync(recherche with { LieuId = manque.LieuId }, cancellationToken)
                ?? await prise.TrouverCreneauAsync(recherche, cancellationToken);
            if (creneau is null)
            {
                return Error.Conflict("creneau.aucun", "Aucun créneau libre n'a été trouvé dans l'horizon de recherche.");
            }
        }

        var rdv = await prise.PlanifierAsync(creneau,
            new DemandeRendezVous(manque.PersonneId, manque.AffilieId, manque.ObligationIds, OrigineRendezVous.Reconvocation, manque.Urgent)
            {
                Canal = canal.Value,
                TypeConvocation = TypeConvocation.Reconvocation,
                ReconvocationDeId = manque.Id,
            },
            cancellationToken);
        if (!rdv.IsSuccess)
        {
            return rdv.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rdv.Value.Id;
    }
}
