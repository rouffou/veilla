using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Referentiels;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Application.Trajets;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Projections;

namespace Sepp.Reintegration.Application.Projections;

/// <summary>
/// SAN-60 : notification d'incapacité reçue par le flux eHealth d'Intégrations. Clé : <c>IncapaciteId</c> (idempotent, inbox) ;
/// une prolongation actualise le dossier, une notification plus ancienne que l'état connu est ignorée.
/// </summary>
public sealed class IncapaciteNotifieeHandler(IIncapaciteRepository incapacites, MinuteriesDossier minuteries, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<IncapaciteNotifiee>
{
    public async Task HandleAsync(IncapaciteNotifiee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var source = CodesContrats.Source(e.Source);
        var incapacite = await incapacites.GetAsync(e.IncapaciteId, cancellationToken);
        if (incapacite is null)
        {
            incapacite = Incapacite.Notifier(e.IncapaciteId, e.PersonneId, e.AffilieId, e.DateDebut, e.DateFinPrevue, source, e.OccurredAt);
            incapacites.Add(incapacite);
        }
        else if (!incapacite.Actualiser(e.PersonneId, e.AffilieId, e.DateDebut, e.DateFinPrevue, source, e.OccurredAt))
        {
            return;
        }

        await minuteries.ReprogrammerAsync(incapacite, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Reprise du travail enregistrée par Obligations (<c>obligations.reprise-enregistree</c>) : les incapacités en cours du
/// travailleur chez l'affilié, commencées avant la reprise, prennent fin la veille. Une annulation de la reprise ne rouvre pas
/// l'incapacité (à valider : réouverture manuelle).
/// </summary>
public sealed class RepriseEnregistreeHandler(IIncapaciteRepository incapacites, MinuteriesDossier minuteries, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<RepriseEnregistree>
{
    public async Task HandleAsync(RepriseEnregistree integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.Statut is not ("Enregistree" or "Modifiee"))
        {
            return;
        }

        var terminees = 0;
        foreach (var incapacite in await incapacites.ListerEnCoursAsync(e.PersonneId, e.AffilieId, cancellationToken))
        {
            if (incapacite.DateDebut < e.DateReprise && incapacite.Terminer(e.DateReprise.AddDays(-1)))
            {
                await minuteries.ReprogrammerAsync(incapacite, cancellationToken);
                terminees++;
            }
        }

        if (terminees > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>SAN-63 : obligations de réintégration (estimation, évaluation) dont un rendez-vous manqué concerne le trajet.</summary>
public sealed class ObligationCreeeHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ObligationCreee>
{
    public static readonly IReadOnlySet<string> TypesSuivis = new HashSet<string>(StringComparer.Ordinal)
    {
        TypesExamen.EstimationPotentielTravail, TypesExamen.EvaluationReintegration,
    };

    public async Task HandleAsync(ObligationCreee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!TypesSuivis.Contains(e.TypeExamen) || await projections.GetObligationAsync(e.ObligationId, cancellationToken) is not null)
        {
            return;
        }

        projections.Add(new ObligationReintegration(e.ObligationId, e.PersonneId, e.AffilieId, e.TypeExamen));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// SAN-63 : absence constatée par Planification à un rendez-vous couvrant une obligation de réintégration → compteur du trajet
/// ouvert du travailleur (idempotent par rendez-vous) ; au seuil, information du médecin-conseil par eHealth.
/// </summary>
public sealed class AbsenceRendezVousConstateeHandler(
    IProjectionRepository projections, ITrajetRepository trajets, ParametresReintegration parametres, InformationMedecinConseil information,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : IIntegrationEventHandler<AbsenceRendezVousConstatee>
{
    public async Task HandleAsync(AbsenceRendezVousConstatee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.ObligationIds.Count == 0 || !await projections.ExisteObligationAsync(e.ObligationIds, cancellationToken)
            || await trajets.GetOuvertAsync(e.PersonneId, e.AffilieId, cancellationToken) is not { } trajet)
        {
            return;
        }

        var politique = await parametres.PolitiqueAsync(trajet.DateDemande, cancellationToken);
        var jour = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Debut, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels")).DateTime);
        if (!trajet.EnregistrerAbsence(e.RendezVousId, jour, horloge.Aujourdhui(), politique, horloge.GetUtcNow()))
        {
            return;
        }

        information.Traiter(trajet);
        trajet.Reprogrammer(politique);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>ARC-21 : paramètres légaux <c>REINTEGRATION.*</c> (écriture par clé, idempotente).</summary>
public sealed class ParametreLegalModifieHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ParametreLegalModifie>
{
    public async Task HandleAsync(ParametreLegalModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!ParametresReintegration.Suivis.Contains(e.Code))
        {
            return;
        }

        var parametre = await projections.GetParametreAsync(e.Code, e.ValideDu, cancellationToken);
        if (parametre is null)
        {
            projections.Add(new ParametreLegalLocal(e.Code, e.ValideDu, e.ValideJusquAu, e.Valeur, e.Unite));
        }
        else
        {
            parametre.Appliquer(e.ValideJusquAu, e.Valeur, e.Unite);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>DAT-08 : jours fériés supplémentaires d'une année (état complet ; un message plus ancien est ignoré).</summary>
public sealed class JoursFeriesModifiesHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<JoursFeriesModifies>
{
    public async Task HandleAsync(JoursFeriesModifies integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.JoursSupplementaires is null)
        {
            return;
        }

        var calendrier = await projections.GetCalendrierAsync(e.Annee, cancellationToken);
        if (calendrier is null)
        {
            projections.Add(new CalendrierLocal(e.Annee, e.JoursSupplementaires, e.OccurredAt));
        }
        else if (!calendrier.Appliquer(e.JoursSupplementaires, e.OccurredAt))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
