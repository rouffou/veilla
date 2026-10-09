using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Referentiels;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Application.Urgences;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;

namespace Sepp.Planification.Application.Projections;

/// <summary>
/// Compensation de la saga de reprise (ARC-33, §14.6) : une obligation est close (réalisée, annulée, travailleur sorti de
/// l'entreprise). La projection locale est close ; un rendez-vous à venir qui ne couvre plus aucune obligation ouverte est
/// annulé avec le motif <c>ObligationLevee</c> (publie <c>RendezVousAnnule</c> : Communications prévient la personne,
/// Obligations n'en fait pas une obligation à replanifier). Idempotent.
/// </summary>
public sealed class ObligationClotureeHandler(
    IObligationRepository obligations,
    IRendezVousRepository rendezVous,
    Annulation annulation,
    IUnitOfWork unitOfWork,
    TimeProvider horloge) : IIntegrationEventHandler<ObligationCloturee>
{
    public async Task HandleAsync(ObligationCloturee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var obligation = await obligations.GetAsync(e.ObligationId, cancellationToken);
        if (obligation is null)
        {
            // Livraison désordonnée : la clôture arrive avant la création ; la création, plus ancienne, sera ignorée.
            obligation = new ObligationAPlanifier(e.ObligationId, e.PersonneId, e.AffilieId, ObligationCreeeHandler.Normaliser(e.TypeExamen), e.Date, null,
                e.OccurredAt);
            obligations.Add(obligation);
        }

        if (!obligation.Cloturer(e.Statut, e.Date, e.OccurredAt))
        {
            return;
        }

        if (obligation.RendezVousId is { } rendezVousId && await rendezVous.GetAsync(rendezVousId, cancellationToken) is { } rdv
            && rdv.Statut == StatutRendezVous.Planifie && rdv.Debut > horloge.GetUtcNow())
        {
            var couvertes = await obligations.ListAsync(rdv.ObligationIds, cancellationToken);
            var ouvertes = rdv.ObligationIds.Distinct().Any(id => couvertes.All(o => o.ObligationId != id || !o.Cloturee));
            if (!ouvertes && await annulation.AnnulerAsync(rdv, MotifAnnulation.ObligationLevee, cancellationToken) is { } erreur)
            {
                // Incohérence de données (créneau introuvable) : l'événement échoue pour être rejoué, jamais perdu en silence.
                throw new InvalidOperationException($"Annulation du rendez-vous {rdv.Id} impossible : {erreur.Message}");
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Obligations demande de replanifier une obligation à échéance légale (SAN-13, PLA-06) : absence, rendez-vous annulé ou
/// convocation non remise. Passe par les mécanismes existants : réservation d'un créneau d'urgence avant la date limite
/// (reconvocation d'un rendez-vous manqué : type <c>Reconvocation</c>), ou alerte <c>UrgenceNonCouverte</c>. Pour une
/// convocation non remise, le rendez-vous est reconvoqué une seule fois (garde contre une boucle d'échecs).
/// </summary>
public sealed class PlanificationUrgenteDemandeeHandler(
    IObligationRepository obligations,
    IRendezVousRepository rendezVous,
    IConvocationRepository convocations,
    ReservationUrgence reservation,
    PriseDeRendezVous prise,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<PlanificationUrgenteDemandee>
{
    public const string MotifAbsence = "Absence";
    public const string MotifAnnulationRendezVous = "AnnulationRendezVous";
    public const string MotifConvocationNonRemise = "ConvocationNonRemise";

    public async Task HandleAsync(PlanificationUrgenteDemandee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.Motif is not (MotifAbsence or MotifAnnulationRendezVous or MotifConvocationNonRemise))
        {
            return;
        }

        var typeExamen = ObligationCreeeHandler.Normaliser(e.TypeExamen);
        var obligation = await obligations.GetAsync(e.ObligationId, cancellationToken);
        if (obligation is null)
        {
            obligation = new ObligationAPlanifier(e.ObligationId, e.PersonneId, e.AffilieId, typeExamen, e.DateDue, e.DateLimite, e.OccurredAt);
            obligations.Add(obligation);
        }

        if (obligation.Cloturee)
        {
            return;
        }

        if (e.Motif == MotifConvocationNonRemise)
        {
            await ReconvoquerAsync(e, cancellationToken);
        }
        else if (obligation.EstAPlanifier)
        {
            Guid? manque = null;
            if (e.Motif == MotifAbsence && e.RendezVousId is { } rdvId
                && await rendezVous.GetAsync(rdvId, cancellationToken) is { Statut: StatutRendezVous.Absent } absent)
            {
                manque = absent.Id;
            }

            var resultat = await reservation.ReserverAsync(e.PersonneId, e.AffilieId, typeExamen, [e.ObligationId], e.DateDue, e.DateLimite, manque,
                cancellationToken);
            if (resultat.IsSuccess && resultat.Value.RendezVous is { } nouveau)
            {
                obligation.Couvrir(nouveau.Id);
            }
            else
            {
                obligation.SignalerUrgenceNonCouverte();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconvoquerAsync(PlanificationUrgenteDemandee e, CancellationToken cancellationToken)
    {
        if (e.RendezVousId is not { } rdvId || await rendezVous.GetAsync(rdvId, cancellationToken) is not { Statut: StatutRendezVous.Planifie } rdv)
        {
            return;
        }

        var existantes = await convocations.ListAsync(rdv.Id, cancellationToken);
        if (existantes.Any(c => c.Type == TypeConvocation.Reconvocation) || existantes.Any(c => c.DateEnvoi is not null))
        {
            return;
        }

        // Une nouvelle convocation du même rendez-vous : le type Reconvocation donne une clé d'envoi distincte chez Communications.
        await prise.ConvoquerAsync(rdv, null, null, TypeConvocation.Reconvocation, null, cancellationToken);
    }
}

/// <summary>
/// DAT-08 : jours fériés supplémentaires d'une année (<c>referentiels.jours-feries-modifies</c>). Le calendrier local
/// remplace, pour l'année, la liste de la configuration, qui ne reste que la valeur initiale. L'état le plus récent
/// l'emporte ; un événement sans liste (producteur antérieur, ARC-34) est ignoré.
/// </summary>
public sealed class JoursFeriesModifiesHandler(ICalendrierLocalRepository calendriers, IUnitOfWork unitOfWork) : IIntegrationEventHandler<JoursFeriesModifies>
{
    public async Task HandleAsync(JoursFeriesModifies integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.JoursSupplementaires is null)
        {
            return;
        }

        var calendrier = await calendriers.GetAsync(e.Annee, cancellationToken);
        if (calendrier is null)
        {
            calendriers.Add(new CalendrierLocal(e.Annee, e.JoursSupplementaires, e.OccurredAt));
        }
        else if (!calendrier.Appliquer(e.JoursSupplementaires, e.OccurredAt))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
