using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Obligations;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Application.Calcul;

/// <summary>
/// SAN-01, SAN-04 : recalcule les obligations de travailleurs à partir des projections locales, applique le résultat
/// (création, mise à jour, réalisation, annulation, sortie, planification) et place les événements d'intégration
/// (ObligationCreee, ObligationEchue) dans l'outbox. N'enregistre pas : l'appelant valide la transaction (ARC-22).
/// </summary>
public sealed class RecalculObligations(
    IProjectionRepository projections,
    IObligationRepository obligations,
    IDemandeRepository demandes,
    IIntegrationEventOutbox outbox,
    TimeProvider clock,
    OptionsCalcul options)
{
    /// <returns>Nombre de travailleurs recalculés.</returns>
    public async Task<int> RecalculerAsync(IEnumerable<Guid> personneIds, CancellationToken cancellationToken)
    {
        var ids = personneIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var aujourdHui = clock.AujourdHui();
        var moteur = await MoteurAsync(aujourdHui, cancellationToken);
        foreach (var personneId in ids)
        {
            var situation = await ChargerSituationAsync(personneId, cancellationToken);
            var resultat = moteur.Calculer(situation, aujourdHui);
            var existantes = await obligations.ListParPersonneAsync(personneId, cancellationToken);
            var rendezVous = await projections.RendezVousDeAsync(personneId, cancellationToken);
            var nouvelles = ReconciliationObligations.Appliquer(personneId, existantes, resultat, rendezVous, clock.GetUtcNow());
            foreach (var obligation in nouvelles)
            {
                obligations.Add(obligation);
            }

            EvenementsIntegration.Publier(existantes.Concat(nouvelles), outbox);
        }

        return ids.Count;
    }

    /// <summary>Calcul seul, sans effet : pour les alertes et les tests.</summary>
    public async Task<ResultatCalcul> CalculerAsync(Guid personneId, CancellationToken cancellationToken)
    {
        var aujourdHui = clock.AujourdHui();
        var moteur = await MoteurAsync(aujourdHui, cancellationToken);
        return moteur.Calculer(await ChargerSituationAsync(personneId, cancellationToken), aujourdHui);
    }

    /// <summary>ARC-21 : politiques légales et calendrier alimentés par les projections du service Référentiels.</summary>
    public async Task<(PolitiquesLegales Politiques, Sepp.BuildingBlocks.Domain.Calendar.BusinessCalendar Calendrier)> ReferentielsAsync(
        DateOnly aujourdHui, CancellationToken cancellationToken)
    {
        var politiques = new PolitiquesLegales(await projections.ParametresAsync(cancellationToken));
        var calendrier = CalendrierOuvrable.Construire(await projections.CalendriersAsync(cancellationToken), aujourdHui.Year - 30, aujourdHui.Year + 5);
        return (politiques, calendrier);
    }

    private async Task<MoteurEcheances> MoteurAsync(DateOnly aujourdHui, CancellationToken cancellationToken)
    {
        var (politiques, calendrier) = await ReferentielsAsync(aujourdHui, cancellationToken);
        return new MoteurEcheances(politiques, calendrier, options);
    }

    private async Task<SituationTravailleur> ChargerSituationAsync(Guid personneId, CancellationToken cancellationToken)
    {
        var affectations = await projections.AffectationsDeAsync(personneId, cancellationToken);
        var postes = affectations.Select(a => a.PosteId).Distinct().ToList();
        var profils = await projections.ProfilsDesPostesAsync(postes, cancellationToken);
        var codes = profils.SelectMany(p => p.CodesRisques).Distinct(StringComparer.Ordinal).ToList();
        return new SituationTravailleur(
            personneId,
            affectations,
            profils,
            await projections.ReglesAsync(codes, cancellationToken),
            await projections.SurchargesAsync(personneId, postes, cancellationToken),
            await projections.OccupationsDeAsync(personneId, cancellationToken),
            await projections.EtatsParticuliersDeAsync(personneId, cancellationToken),
            await projections.ExamensDeAsync(personneId, cancellationToken),
            await projections.ReprisesDeAsync(personneId, cancellationToken),
            await projections.IncapacitesDeAsync(personneId, cancellationToken),
            await projections.TrajetsDeAsync(personneId, cancellationToken),
            await demandes.ListParPersonneAsync(personneId, cancellationToken));
    }
}

/// <summary>Traduction des événements de domaine en événements d'intégration (ARC-06 : identifiants, type d'examen, dates).</summary>
public static class EvenementsIntegration
{
    public static void Publier(IEnumerable<Obligation> obligations, IIntegrationEventOutbox outbox)
    {
        foreach (var obligation in obligations)
        {
            foreach (var evenement in obligation.DomainEvents)
            {
                switch (evenement)
                {
                    case ObligationOuverte e:
                        outbox.Add(new ObligationCreee(e.ObligationId, e.PersonneId, e.AffilieId, e.Type.Code(), e.DateDue, e.DateLimite));
                        break;
                    case ObligationDevenueEchue e:
                        outbox.Add(new ObligationEchue(e.ObligationId, e.PersonneId, e.AffilieId, e.Type.Code(), e.DateLimite));
                        break;
                }
            }

            obligation.ClearDomainEvents();
        }
    }

    /// <summary>Conserve le compilateur honnête : un événement de domaine inconnu ne doit pas être ignoré en silence.</summary>
    internal static bool EstConnu(IDomainEvent evenement) => evenement is ObligationOuverte or ObligationDevenueEchue;
}
