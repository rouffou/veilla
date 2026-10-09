using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Contracts;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Obligations;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Domain;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Application.Reprises;

/// <summary>
/// Réglages du processus de reprise (section <c>Obligations:Reprise</c>). Les seuils sont des réglages d'organisation,
/// <b>à valider</b> par les départements médical et juridique (voir <see cref="PolitiqueSuiviReprise"/>).
/// </summary>
public sealed class OptionsReprise
{
    /// <summary>
    /// Replanification automatique après une absence, une annulation de rendez-vous ou une convocation non remise
    /// (<c>PlanificationUrgenteDemandee</c>). Désactivée par défaut : sans validation métier, une alerte est levée à la place.
    /// </summary>
    public bool ReplanificationAutomatique { get; set; }

    /// <summary>À valider : alerte « échéance menacée » à N jours ouvrables de la date limite (0 : désactivée).</summary>
    public int AlerteAvantEcheanceJoursOuvrables { get; set; } = 2;

    public int RendezVousSansClotureJoursOuvrables { get; set; } = 1;

    /// <summary>À valider : alerte « décision en attente » N jours ouvrables après l'examen (<c>null</c> : désactivée).</summary>
    public int? DelaiDecisionJoursOuvrables { get; set; }

    /// <summary>À valider : expiration administrative N jours après la reprise (<c>null</c> : désactivée).</summary>
    public int? ExpirationJours { get; set; }

    public PolitiqueSuiviReprise Politique() =>
        new(AlerteAvantEcheanceJoursOuvrables, RendezVousSansClotureJoursOuvrables, DelaiDecisionJoursOuvrables, ExpirationJours);
}

/// <summary>Traduction des événements de domaine du processus en événements d'intégration (ARC-06 : identifiants, dates, codes).</summary>
public static class EvenementsReprise
{
    /// <summary>Traduit et vide les événements du processus. <paramref name="dateDue"/> complète la demande d'urgence (connue de l'obligation).</summary>
    public static IReadOnlyList<IntegrationEvent> Traduire(ProcessusReprise processus, OptionsReprise options, DateOnly? dateDue)
    {
        var evenements = new List<IntegrationEvent>();
        foreach (var evenement in processus.DomainEvents)
        {
            switch (evenement)
            {
                case RepriseEnregistreeDomaine e:
                    evenements.Add(new RepriseEnregistree(
                        e.RepriseId, e.PersonneId, e.AffilieId, e.DateReprise, e.DebutAbsence, e.Origine.ToString(), e.Statut.ToString()));
                    break;
                case ReplanificationUrgenteRequise e when options.ReplanificationAutomatique && e.ObligationId is { } obligationId && processus.DateLimite is { } limite:
                    evenements.Add(new PlanificationUrgenteDemandee(
                        obligationId, e.RendezVousId, e.PersonneId, e.AffilieId, TypesExamen.ExamenReprise, dateDue ?? processus.DateReprise, limite, e.Motif.ToString()));
                    break;
            }
        }

        processus.ClearDomainEvents();
        return evenements;
    }

    public static void Publier(ProcessusReprise processus, OptionsReprise options, DateOnly? dateDue, IIntegrationEventOutbox outbox)
    {
        foreach (var evenement in Traduire(processus, options, dateDue))
        {
            outbox.Add(evenement);
        }
    }
}

/// <summary>
/// ARC-33 : après chaque recalcul, relit l'obligation <c>EXAMEN_REPRISE</c> de chaque processus du travailleur (lien par la
/// clé <c>REPRISE:affilié:date</c>, même transaction) et lui applique les jalons correspondants : obligation ouverte, rendez-vous,
/// convocation, absence, examen, décision parquée. Fonction de l'état des projections : rejouée ou réordonnée, elle converge.
/// </summary>
public sealed class SynchronisationProcessusReprise(
    IProcessusRepriseRepository processus,
    IDecisionRecueRepository decisions,
    IProjectionRepository projections,
    IIntegrationEventOutbox outbox,
    OptionsReprise options,
    TimeProvider clock)
{
    public async Task SynchroniserAsync(
        Guid personneId,
        IReadOnlyList<Obligation> obligations,
        IReadOnlyList<RendezVousLocal> rendezVous,
        MoteurEcheances moteur,
        ResultatCalcul resultat,
        BusinessCalendar calendrier,
        CancellationToken cancellationToken)
    {
        var maintenant = clock.GetUtcNow();
        var politique = options.Politique();
        foreach (var p in await processus.ListParPersonneAsync(personneId, cancellationToken))
        {
            if (p.EstAnnulee)
            {
                continue;
            }

            var cle = MoteurEcheances.CleReprise(p.AffilieId, p.DateReprise);
            var obligation = obligations.FirstOrDefault(o => o.Cle == cle);
            if (obligation is null)
            {
                if (!moteur.ExamenRepriseRequis(p.DebutAbsence, p.DateReprise))
                {
                    p.MarquerExamenNonRequis(maintenant);
                }
                else if (resultat.EstSortiDe(p.AffilieId))
                {
                    p.MarquerSansObjet();
                }
            }
            else
            {
                await AppliquerObligationAsync(p, obligation, rendezVous, moteur, maintenant, cancellationToken);
            }

            p.Reprogrammer(politique, calendrier);
            EvenementsReprise.Publier(p, options, obligation?.DateDue, outbox);
        }
    }

    /// <summary>Reprogramme la minuterie d'un processus modifié hors recalcul (décision, urgence non couverte).</summary>
    public async Task ReprogrammerAsync(ProcessusReprise p, CancellationToken cancellationToken)
    {
        var aujourdHui = clock.AujourdHui();
        var calendrier = CalendrierOuvrable.Construire(await projections.CalendriersAsync(cancellationToken), aujourdHui.Year - 30, aujourdHui.Year + 5);
        p.Reprogrammer(options.Politique(), calendrier);
    }

    private async Task AppliquerObligationAsync(
        ProcessusReprise p, Obligation o, IReadOnlyList<RendezVousLocal> rendezVous, MoteurEcheances moteur, DateTimeOffset maintenant, CancellationToken cancellationToken)
    {
        switch (o.Statut)
        {
            case StatutObligation.SortiEntreprise:
                p.MarquerSansObjet();
                return;
            case StatutObligation.Annule:
                if (!moteur.ExamenRepriseRequis(p.DebutAbsence, p.DateReprise))
                {
                    p.MarquerExamenNonRequis(maintenant);
                }
                else
                {
                    p.MarquerSansObjet();
                }

                return;
            case StatutObligation.Realise:
                p.LierObligation(o.Id, o.DateLimite);
                if (o.ExamenId is { } examenId && o.DateRealisation is { } date)
                {
                    p.EnregistrerExamen(examenId, date);
                    if (await decisions.GetAsync(examenId, cancellationToken) is { } decision)
                    {
                        p.EnregistrerDecision(decision.DecisionId, decision.RecueLe);
                    }
                }

                return;
        }

        p.LierObligation(o.Id, o.DateLimite);
        if (o.Statut == StatutObligation.Absent && o.RendezVousId is { } absent)
        {
            p.EnregistrerAbsence(absent, maintenant);
        }
        else if (o.RendezVousId is { } rendezVousId && o.DateRendezVous is { } debut && MachineEtatsObligation.EstPlanifie(o.Statut))
        {
            p.PlanifierRendezVous(rendezVousId, debut);
            var local = rendezVous.FirstOrDefault(r => r.RendezVousId == rendezVousId);
            if (local is { ConvocationAJour: true, ConvocationEnvoyeeLe: { } envoyee })
            {
                p.EnregistrerConvocation(rendezVousId, envoyee);
            }
            else
            {
                p.ReinitialiserConvocation();
            }

            if (local is { ConvocationNonRemise: true, ConvocationNonRemiseLe: { } abandonnee })
            {
                p.EnregistrerConvocationNonRemise(rendezVousId, abandonnee, maintenant);
            }
        }
        else if (o.RendezVousId is null && o.Statut == StatutObligation.APlanifier && p.RendezVousId is { } precedent)
        {
            p.RendezVousAnnule(precedent, rendezVous.FirstOrDefault(r => r.RendezVousId == precedent)?.MotifAnnulation, maintenant);
        }
    }
}
