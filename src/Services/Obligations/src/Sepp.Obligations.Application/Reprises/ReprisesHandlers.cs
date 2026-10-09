using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Planification;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Application.Projections;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Projections;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Application.Reprises;

// ARC-33 : réactions du processus de reprise aux événements des autres services. Chaque gestionnaire met à jour un modèle de
// lecture local ou un jalon du processus, de façon idempotente et indépendante de l'ordre de réception (l'inbox écarte les
// doublons de livraison). Les jalons de rendez-vous passent par RendezVousLocal puis par le recalcul et la synchronisation.

/// <summary>SAN-10 : la convocation est remise au canal d'envoi → l'obligation couverte par le rendez-vous passe à « convoqué ».</summary>
public sealed class ConvocationEnvoyeeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<ConvocationEnvoyee>
{
    public async Task HandleAsync(ConvocationEnvoyee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await RendezVousLocaux.ObtenirAsync(projections, e.RendezVousId, e.PersonneId, cancellationToken);
        if (rendezVous.EnregistrerConvocation(e.DateEnvoi))
        {
            await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
        }
    }
}

/// <summary>SAN-10 : la convocation n'a pas pu être remise → jalon et alerte (replanification urgente si le mode automatique est activé).</summary>
public sealed class ConvocationNonRemiseHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<ConvocationNonRemise>
{
    public async Task HandleAsync(ConvocationNonRemise integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await RendezVousLocaux.ObtenirAsync(projections, e.RendezVousId, e.PersonneId, cancellationToken);
        if (rendezVous.EnregistrerConvocationNonRemise(e.Date))
        {
            await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
        }
    }
}

/// <summary>SAN-13 : absence au rendez-vous → l'obligation passe à « absent » (réconciliation), le processus compte l'absence.</summary>
public sealed class AbsenceRendezVousConstateeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<AbsenceRendezVousConstatee>
{
    public async Task HandleAsync(AbsenceRendezVousConstatee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await RendezVousLocaux.ObtenirAsync(projections, e.RendezVousId, e.PersonneId, cancellationToken);
        if (rendezVous.MarquerAbsent())
        {
            await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
        }
    }
}

/// <summary>PLA-07 : rendez-vous déplacé → nouvelle date sur l'obligation ; une alerte signale un rendez-vous au-delà de la date limite.</summary>
public sealed class RendezVousReplanifieHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<RendezVousReplanifie>
{
    public async Task HandleAsync(RendezVousReplanifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await RendezVousLocaux.ObtenirAsync(projections, e.RendezVousId, e.PersonneId, cancellationToken);
        if (rendezVous.Replanifier(e.NouveauDebut, e.OccurredAt))
        {
            await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
        }
    }
}

/// <summary>PLA-06 : aucun créneau avant l'échéance légale → jalon « non couvert » et alerte.</summary>
public sealed class UrgenceNonCouverteHandler(IProcessusRepriseRepository processus, SynchronisationProcessusReprise synchronisation, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<UrgenceNonCouverte>
{
    public async Task HandleAsync(UrgenceNonCouverte integrationEvent, CancellationToken cancellationToken)
    {
        var p = await processus.GetParObligationAsync(integrationEvent.ObligationId, cancellationToken);
        if (p is null || !p.SignalerUrgenceNonCouverte())
        {
            return;
        }

        await synchronisation.ReprogrammerAsync(p, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Décision émise : appliquée au processus de l'examen qu'elle clôt. Si l'examen n'est pas encore connu (DecisionEmise avant
/// ExamenCloture), la décision est parquée (<c>decision_recue</c>) puis appliquée quand l'examen réalise l'obligation. La plus
/// récente (OccurredAt) remplace la précédente. Une décision sans <c>ExamenId</c> (producteur antérieur) est ignorée.
/// </summary>
public sealed class DecisionEmiseHandler(
    IProcessusRepriseRepository processus, IDecisionRecueRepository decisions, SynchronisationProcessusReprise synchronisation, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<DecisionEmise>
{
    public async Task HandleAsync(DecisionEmise integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.ExamenId is not { } examenId)
        {
            return;
        }

        var parquee = await decisions.GetAsync(examenId, cancellationToken);
        var change = false;
        if (parquee is null)
        {
            decisions.Add(new DecisionRecue(examenId, e.DecisionId, e.PersonneId, e.AffilieId, e.OccurredAt));
            change = true;
        }
        else
        {
            change = parquee.Appliquer(e.DecisionId, e.OccurredAt);
        }

        var p = await processus.GetParExamenAsync(examenId, cancellationToken);
        if (p is not null && p.EnregistrerDecision(e.DecisionId, e.OccurredAt))
        {
            await synchronisation.ReprogrammerAsync(p, cancellationToken);
            change = true;
        }

        if (change)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Document publié pour une décision (<c>ObjetType = "decision"</c>) à destination de l'employeur : référence conservée sur le
/// processus (POR-04). Les autres documents sont ignorés ; un document reçu avant la décision n'est pas parqué (information seule).
/// </summary>
public sealed class DocumentPublieHandler(IProcessusRepriseRepository processus, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<DocumentPublie>
{
    private const string DestinataireAffilie = "Affilie";

    public async Task HandleAsync(DocumentPublie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!string.Equals(e.ObjetType, ProcessusReprise.ObjetDecision, StringComparison.OrdinalIgnoreCase)
            || e.ObjetId is not { } decisionId
            || !string.Equals(e.TypeDestinataire, DestinataireAffilie, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var p = await processus.GetParDecisionAsync(decisionId, cancellationToken);
        if (p is not null && p.EnregistrerDocumentEmployeur(e.DocumentId))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

internal static class RendezVousLocaux
{
    public static async Task<RendezVousLocal> ObtenirAsync(IProjectionRepository projections, Guid rendezVousId, Guid personneId, CancellationToken cancellationToken)
    {
        var rendezVous = await projections.GetRendezVousAsync(rendezVousId, cancellationToken);
        if (rendezVous is null)
        {
            rendezVous = new RendezVousLocal(rendezVousId, personneId);
            projections.Add(rendezVous);
        }

        return rendezVous;
    }
}

public sealed record ResultatMinuteries(int ProcessusTraites, int MinuteriesDeclenchees);

/// <summary>
/// ARC-33, ADR 0008 : traite les minuteries échues des processus de reprise (alerte « échéance menacée », « hors délai »,
/// rendez-vous sans clôture, décision en attente, expiration). Un lot par appel : l'appelant ouvre une transaction qui garde les
/// verrous <c>FOR UPDATE SKIP LOCKED</c> jusqu'à l'enregistrement, pour que plusieurs instances ne traitent jamais le même
/// processus. Sans contrôle de permission : appelé par le service d'arrière-plan.
/// </summary>
public sealed class TraiterMinuteriesReprise(
    IProcessusRepriseRepository processus,
    IProjectionRepository projections,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    OptionsReprise options,
    TimeProvider clock)
{
    public const int TailleLot = 50;

    public async Task<ResultatMinuteries> ExecuterLotAsync(CancellationToken cancellationToken)
    {
        var aujourdHui = clock.AujourdHui();
        var lot = await processus.ReserverEchusAsync(aujourdHui, TailleLot, cancellationToken);
        if (lot.Count == 0)
        {
            return new ResultatMinuteries(0, 0);
        }

        var calendrier = CalendrierOuvrable.Construire(await projections.CalendriersAsync(cancellationToken), aujourdHui.Year - 30, aujourdHui.Year + 5);
        var politique = options.Politique();
        var declenchees = 0;
        foreach (var p in lot)
        {
            declenchees += p.DeclencherMinuteries(aujourdHui, politique, calendrier).Count;
            EvenementsReprise.Publier(p, options, null, outbox);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ResultatMinuteries(lot.Count, declenchees);
    }
}
