using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Application.Gestion;

/// <summary>Actions manuelles sur le statut d'une obligation (SAN-02).</summary>
public enum ActionStatut
{
    Convoquer,
    MarquerAbsent,
    Reporter,
    Excuser,
    Annuler,
}

/// <summary>SAN-02 : fait passer une obligation à un nouveau statut selon la machine à états ; une transition interdite est un conflit (409).</summary>
public sealed record ChangerStatutObligation(Guid ObligationId, ActionStatut Action, DateOnly? DateReport = null, string? Motif = null);

public sealed class ChangerStatutObligationHandler(
    IObligationRepository obligations, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<ChangerStatutObligation, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangerStatutObligation command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.ObligationGerer) is { } interdit)
        {
            return interdit;
        }

        var obligation = await obligations.GetAsync(command.ObligationId, cancellationToken);
        if (obligation is null)
        {
            return Acces.ObligationInconnue(command.ObligationId);
        }

        MotifAnnulation? motif = null;
        if (command.Action == ActionStatut.Annuler)
        {
            // Le motif est un code, jamais un texte libre (ARC-06) ; « recalcul » est réservé au moteur.
            if (!Enumerations.TryParse<MotifAnnulation>(command.Motif, out var lu) || lu == MotifAnnulation.Recalcul)
            {
                return Error.Validation("obligation.motif-invalide", "Le motif d'annulation doit être DecisionCpmt, Doublon, DemandeAffilie ou Autre.");
            }

            motif = lu;
        }

        var cible = command.Action switch
        {
            ActionStatut.Convoquer => StatutObligation.Convoque,
            ActionStatut.MarquerAbsent => StatutObligation.Absent,
            ActionStatut.Reporter => StatutObligation.Reporte,
            ActionStatut.Excuser => StatutObligation.Excuse,
            _ => StatutObligation.Annule,
        };
        if (!MachineEtatsObligation.EstAutorisee(obligation.Statut, cible))
        {
            return Error.Conflict(
                "obligation.transition-interdite", $"Une obligation au statut « {obligation.Statut} » ne peut pas passer au statut « {cible} ».");
        }

        try
        {
            switch (command.Action)
            {
                case ActionStatut.Convoquer:
                    obligation.Convoquer();
                    break;
                case ActionStatut.MarquerAbsent:
                    obligation.MarquerAbsent();
                    break;
                case ActionStatut.Reporter:
                    if (command.DateReport is not { } date)
                    {
                        return Error.Validation("obligation.date-report-obligatoire", "La date de report est obligatoire.");
                    }

                    obligation.Reporter(date, clock.AujourdHui());
                    break;
                case ActionStatut.Excuser:
                    obligation.Excuser();
                    break;
                default:
                    obligation.Annuler(motif!.Value);
                    break;
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation("obligation.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// §5.1 : enregistre une demande du travailleur (visite de pré-reprise, consultation spontanée) ; le type et la date
/// seulement, jamais le motif. L'obligation à 10 jours ouvrables est calculée immédiatement (SAN-04).
/// </summary>
public sealed record EnregistrerDemandeTravailleur(Guid PersonneId, Guid AffilieId, TypeObligation Type, DateOnly? DateDemande = null);

public sealed class EnregistrerDemandeTravailleurHandler(
    IDemandeRepository demandes, RecalculObligations recalcul, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<EnregistrerDemandeTravailleur, Guid>
{
    public async Task<Result<Guid>> HandleAsync(EnregistrerDemandeTravailleur command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.ObligationGerer) is { } interdit)
        {
            return interdit;
        }

        var aujourdHui = clock.AujourdHui();
        DemandeTravailleur demande;
        try
        {
            demande = DemandeTravailleur.Enregistrer(command.PersonneId, command.AffilieId, command.Type, command.DateDemande ?? aujourdHui, aujourdHui, user.UserId);
        }
        catch (DomainException ex)
        {
            return Error.Validation("demande.invalide", ex.Message);
        }

        demandes.Add(demande);
        await recalcul.RecalculerAsync([command.PersonneId], cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return demande.Id;
    }
}

/// <summary>SAN-04 : recalcul à la demande d'un travailleur, ou de tous les travailleurs occupés chez un affilié.</summary>
public sealed record DemanderRecalcul(Guid? PersonneId, Guid? AffilieId);

public sealed class DemanderRecalculHandler(
    RecalculObligations recalcul, IProjectionRepository projections, IObligationRepository obligations, IUnitOfWork unitOfWork,
    ICurrentUser user, TimeProvider clock) : ICommandHandler<DemanderRecalcul, int>
{
    public async Task<Result<int>> HandleAsync(DemanderRecalcul command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.ObligationGerer) is { } interdit)
        {
            return interdit;
        }

        if ((command.PersonneId is null) == (command.AffilieId is null))
        {
            return Error.Validation("recalcul.cible", "Précisez soit le travailleur, soit l'affilié à recalculer.");
        }

        var personnes = new HashSet<Guid>();
        if (command.PersonneId is { } personneId)
        {
            personnes.Add(personneId);
        }
        else
        {
            var affilieId = command.AffilieId!.Value;
            personnes.UnionWith(await projections.PersonnesOccupeesAsync(affilieId, clock.AujourdHui(), cancellationToken));
            personnes.UnionWith((await obligations.ListParAffilieAsync(affilieId, true, cancellationToken)).Select(o => o.PersonneId));
        }

        var nombre = await recalcul.RecalculerAsync(personnes.Order(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return nombre;
    }
}

public sealed record ResultatTraitement(int PersonnesRecalculees, int ObligationsEchues);

/// <summary>
/// Traitement périodique (SAN-04, ObligationEchue) : recalcule les travailleurs qui ont des obligations ouvertes, car
/// l'écoulement du temps seul change le résultat (fin d'occupation, fin d'exposition), puis signale une seule fois chaque
/// dépassement de date limite. Sans contrôle de permission : appelé par le service d'arrière-plan.
/// </summary>
public sealed class TraitementEcheances(
    RecalculObligations recalcul, IObligationRepository obligations, IDemandeRepository demandes, IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox, TimeProvider clock)
{
    private const int TailleLot = 100;

    public async Task<ResultatTraitement> ExecuterAsync(CancellationToken cancellationToken)
    {
        var personnes = (await obligations.PersonnesAvecObligationsOuvertesAsync(cancellationToken))
            .Concat(await demandes.PersonnesAsync(cancellationToken))
            .Distinct()
            .Order()
            .ToList();
        var recalculees = 0;
        foreach (var lot in personnes.Chunk(TailleLot))
        {
            recalculees += await recalcul.RecalculerAsync(lot, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var aujourdHui = clock.AujourdHui();
        var echues = 0;
        while (true)
        {
            var lot = await obligations.ListEchuesNonSignaleesAsync(aujourdHui, TailleLot, cancellationToken);
            var signalees = lot.Where(o => o.SignalerEchue(aujourdHui)).ToList();
            if (signalees.Count == 0)
            {
                break;
            }

            EvenementsIntegration.Publier(signalees, outbox);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            echues += signalees.Count;
        }

        return new ResultatTraitement(recalculees, echues);
    }
}

/// <summary>Lancement du traitement périodique à la demande (administration).</summary>
public sealed record TraiterEcheances;

public sealed class TraiterEcheancesHandler(TraitementEcheances traitement, ICurrentUser user)
    : ICommandHandler<TraiterEcheances, ResultatTraitement>
{
    public async Task<Result<ResultatTraitement>> HandleAsync(TraiterEcheances command, CancellationToken cancellationToken) =>
        Acces.Permission(user, Permissions.ObligationGerer) is { } interdit ? interdit : await traitement.ExecuterAsync(cancellationToken);
}
