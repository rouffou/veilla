using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.BffEmployeur;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Planification;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.Reintegration;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Application.Projections;

// SAN-01, SAN-04, ARC-31 : chaque gestionnaire met à jour un modèle de lecture local puis recalcule immédiatement les
// obligations des travailleurs concernés, sans aucun appel synchrone. L'inbox écarte les messages déjà traités ; les
// projections sont des écritures par clé qui ignorent un événement plus ancien que l'état connu ; le calcul est une
// fonction pure de l'état des projections : des événements rejoués, en double ou dans le désordre donnent le même résultat.

/// <summary>
/// Termine un gestionnaire : enregistre la projection, puis recalcule les travailleurs concernés et enregistre le résultat.
/// L'inbox enveloppe l'ensemble dans une seule transaction (le recalcul lit la projection enregistrée).
/// </summary>
public sealed class MiseAJourProjection(
    RecalculObligations recalcul, IUnitOfWork unitOfWork, IProjectionRepository projections, IDemandeRepository demandes)
{
    public async Task TerminerAsync(IEnumerable<Guid> personnes, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var concernees = personnes.Where(p => p != Guid.Empty).Distinct().Order().ToList();
        if (concernees.Count > 0)
        {
            await recalcul.RecalculerAsync(concernees, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Travailleurs dont une échéance dépend d'un délai légal ou du calendrier (reprise, incapacité, trajet, demande).</summary>
    public async Task<IReadOnlyList<Guid>> PersonnesSensiblesAuxDelaisAsync(CancellationToken cancellationToken) =>
        (await projections.PersonnesAvecEvenementsAsync(cancellationToken)).Concat(await demandes.PersonnesAsync(cancellationToken)).Distinct().ToList();

    /// <summary>Travailleurs exposés à un risque (affectés à un poste dont un profil contient le risque).</summary>
    public async Task<IReadOnlyList<Guid>> PersonnesExposeesAsync(string codeRisque, CancellationToken cancellationToken) =>
        await projections.PersonnesAffecteesAsync(await projections.PostesExposesAuRisqueAsync(Codes.Normaliser(codeRisque), cancellationToken), cancellationToken);
}

public sealed class AffectationModifieeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<AffectationModifiee>
{
    public async Task HandleAsync(AffectationModifiee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var affectation = await projections.GetAffectationAsync(e.AffectationId, cancellationToken);
        Guid[] anciens = affectation is null ? [] : [affectation.PersonneId];
        if (affectation is null)
        {
            projections.Add(new AffectationLocale(e.AffectationId, e.PersonneId, e.PosteId, e.DateDebut, e.DateFin, e.OccurredAt));
        }
        else if (!affectation.Appliquer(e.PersonneId, e.PosteId, e.DateDebut, e.DateFin, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync(anciens.Append(e.PersonneId), cancellationToken);
    }
}

public sealed class ProfilRisquePosteModifieHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<ProfilRisquePosteModifie>
{
    public async Task HandleAsync(ProfilRisquePosteModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var profil = await projections.GetProfilAsync(e.PosteId, e.ValideDu, cancellationToken);
        if (profil is null)
        {
            projections.Add(new ProfilRisquePosteLocal(e.PosteId, e.ValideDu, e.AffilieId, e.CodesRisques, e.OccurredAt));
        }
        else if (!profil.Appliquer(e.AffilieId, e.CodesRisques, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync(await projections.PersonnesAffecteesAsync([e.PosteId], cancellationToken), cancellationToken);
    }
}

/// <summary>AFF-12 : une nouvelle version d'une règle de surveillance recalcule les travailleurs exposés au risque.</summary>
public sealed class RegleSurveillanceModifieeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<RegleSurveillanceModifiee>
{
    public async Task HandleAsync(RegleSurveillanceModifiee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var code = Codes.Normaliser(e.CodeRisque);
        var regle = await projections.GetRegleAsync(code, e.Version, cancellationToken);
        if (regle is null)
        {
            projections.Add(new RegleSurveillanceLocale(code, e.Version, e.RisqueId, e.Categorie, e.TypeSurveillance, e.FrequenceMois, e.SurveillanceProlongee, e.ValideDu));
        }
        else
        {
            regle.Appliquer(e.RisqueId, e.Categorie, e.TypeSurveillance, e.FrequenceMois, e.SurveillanceProlongee, e.ValideDu);
        }

        await miseAJour.TerminerAsync(await miseAJour.PersonnesExposeesAsync(code, cancellationToken), cancellationToken);
    }
}

/// <summary>AFF-13 : une surcharge de fréquence du CPMT (poste ou travailleur) recalcule les travailleurs ciblés.</summary>
public sealed class SurchargeFrequenceDefinieHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<SurchargeFrequenceDefinie>
{
    public async Task HandleAsync(SurchargeFrequenceDefinie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var surcharge = await projections.GetSurchargeAsync(e.SurchargeId, cancellationToken);
        var personnes = new List<Guid>();
        if (surcharge is not null)
        {
            personnes.AddRange(await CiblesAsync(surcharge.CibleType, surcharge.CibleId, cancellationToken));
            if (!surcharge.Appliquer(e.AffilieId, e.CibleType, e.CibleId, e.CodeRisque, e.FrequenceMois, e.ValideDu, e.ValideJusquAu, e.OccurredAt))
            {
                return;
            }
        }
        else
        {
            projections.Add(new SurchargeFrequenceLocale(
                e.SurchargeId, e.AffilieId, e.CibleType, e.CibleId, e.CodeRisque, e.FrequenceMois, e.ValideDu, e.ValideJusquAu, e.OccurredAt));
        }

        personnes.AddRange(await CiblesAsync(e.CibleType, e.CibleId, cancellationToken));
        await miseAJour.TerminerAsync(personnes, cancellationToken);
    }

    /// <summary>Un groupe n'a pas d'appartenance connue du service : la surcharge de groupe n'est pas appliquée (voir le moteur).</summary>
    private async Task<IReadOnlyList<Guid>> CiblesAsync(string cibleType, Guid cibleId, CancellationToken cancellationToken) =>
        string.Equals(cibleType, SurchargeFrequenceLocale.CiblePersonne, StringComparison.OrdinalIgnoreCase) ? [cibleId]
        : string.Equals(cibleType, SurchargeFrequenceLocale.CiblePoste, StringComparison.OrdinalIgnoreCase) ? await projections.PersonnesAffecteesAsync([cibleId], cancellationToken)
        : [];
}

public sealed class OccupationDebuteeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<OccupationDebutee>
{
    public async Task HandleAsync(OccupationDebutee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var occupation = await projections.GetOccupationAsync(e.OccupationId, cancellationToken);
        if (occupation is null)
        {
            occupation = new OccupationLocale(e.OccupationId, e.PersonneId, e.AffilieId);
            projections.Add(occupation);
        }

        occupation.Debuter(e.DateDebut);
        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

public sealed class OccupationTermineeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<OccupationTerminee>
{
    public async Task HandleAsync(OccupationTerminee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var occupation = await projections.GetOccupationAsync(e.OccupationId, cancellationToken);
        if (occupation is null)
        {
            occupation = new OccupationLocale(e.OccupationId, e.PersonneId, e.AffilieId);
            projections.Add(occupation);
        }

        if (!occupation.Terminer(e.DateFin, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>AFF-24 : protection de la maternité, reçue sous une catégorie générique uniquement (ARC-06).</summary>
public sealed class EtatParticulierDeclareHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<EtatParticulierDeclare>
{
    public async Task HandleAsync(EtatParticulierDeclare integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var etat = await projections.GetEtatParticulierAsync(e.EtatParticulierId, cancellationToken);
        if (etat is null)
        {
            projections.Add(new EtatParticulierLocal(e.EtatParticulierId, e.PersonneId, e.Categorie, e.DateDebut, e.DateFin, e.OccurredAt));
        }
        else if (!etat.Appliquer(e.PersonneId, e.Categorie, e.DateDebut, e.DateFin, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>Un examen clôturé (type et date uniquement) réalise l'obligation correspondante et fonde le cycle suivant.</summary>
public sealed class ExamenClotureHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<ExamenCloture>
{
    public async Task HandleAsync(ExamenCloture integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var examen = await projections.GetExamenAsync(e.ExamenId, cancellationToken);
        if (examen is null)
        {
            projections.Add(new ExamenLocal(e.ExamenId, e.PersonneId, e.AffilieId, e.TypeExamen, e.Date));
        }
        else
        {
            examen.Appliquer(e.PersonneId, e.AffilieId, e.TypeExamen, e.Date);
        }

        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>ARC-21 : copie locale des paramètres légaux utilisés par le moteur ; un changement recalcule les échéances qui en dépendent.</summary>
public sealed class ParametreLegalModifieHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<ParametreLegalModifie>
{
    public async Task HandleAsync(ParametreLegalModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!PolitiquesLegales.ValeursParDefaut.ContainsKey(e.Code))
        {
            return;
        }

        var parametre = await projections.GetParametreAsync(e.Code, e.ValideDu, cancellationToken);
        if (parametre is null)
        {
            projections.Add(new ParametreLegalLocal(e.Code, e.ValideDu, e.ValideJusquAu, e.Valeur, e.Unite, e.OccurredAt));
        }
        else if (!parametre.Appliquer(e.ValideJusquAu, e.Valeur, e.Unite, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync(await miseAJour.PersonnesSensiblesAuxDelaisAsync(cancellationToken), cancellationToken);
    }
}

/// <summary>DAT-08 : jours fériés supplémentaires de l'année ; un changement recalcule les délais en jours ouvrables.</summary>
public sealed class JoursFeriesModifiesHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<JoursFeriesModifies>
{
    public async Task HandleAsync(JoursFeriesModifies integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.JoursSupplementaires is null)
        {
            // Producteur antérieur à l'ajout du champ (ARC-34) : l'état complet est inconnu, rien à projeter.
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

        await miseAJour.TerminerAsync(await miseAJour.PersonnesSensiblesAuxDelaisAsync(cancellationToken), cancellationToken);
    }
}

/// <summary>SAN-02 : un rendez-vous qui couvre l'obligation la fait passer à « planifié ».</summary>
public sealed class RendezVousPlanifieHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<RendezVousPlanifie>
{
    public async Task HandleAsync(RendezVousPlanifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await projections.GetRendezVousAsync(e.RendezVousId, cancellationToken);
        if (rendezVous is null)
        {
            rendezVous = new RendezVousLocal(e.RendezVousId, e.PersonneId);
            projections.Add(rendezVous);
        }

        if (!rendezVous.Planifier(e.AffilieId, e.Debut, e.ObligationIds, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>SAN-02 : un rendez-vous annulé remet l'obligation « à planifier » ; l'annulation est définitive, quel que soit l'ordre de réception.</summary>
public sealed class RendezVousAnnuleHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<RendezVousAnnule>
{
    public async Task HandleAsync(RendezVousAnnule integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await projections.GetRendezVousAsync(e.RendezVousId, cancellationToken);
        if (rendezVous is null)
        {
            rendezVous = new RendezVousLocal(e.RendezVousId, e.PersonneId);
            projections.Add(rendezVous);
        }

        rendezVous.Annuler();
        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>§5.1 : reprise annoncée par l'employeur → examen de reprise du jour de la reprise à J+10 ouvrables.</summary>
public sealed class RepriseAnnonceeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<RepriseAnnoncee>
{
    public async Task HandleAsync(RepriseAnnoncee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var reprise = await projections.GetRepriseAsync(e.PersonneId, e.AffilieId, e.DateReprise, cancellationToken);
        if (reprise is null)
        {
            projections.Add(new RepriseLocale(e.PersonneId, e.AffilieId, e.DateReprise, e.DebutAbsence, e.OccurredAt));
        }
        else if (!reprise.Appliquer(e.DebutAbsence, e.OccurredAt))
        {
            return;
        }

        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>§5.1 : incapacité notifiée → estimation du potentiel de travail après huit semaines.</summary>
public sealed class IncapaciteNotifieeHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<IncapaciteNotifiee>
{
    public async Task HandleAsync(IncapaciteNotifiee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var incapacite = await projections.GetIncapaciteAsync(e.IncapaciteId, cancellationToken);
        if (incapacite is null)
        {
            projections.Add(new IncapaciteLocale(e.IncapaciteId, e.PersonneId, e.AffilieId, e.DateDebut, e.Source));
        }
        else
        {
            incapacite.Appliquer(e.PersonneId, e.AffilieId, e.DateDebut, e.Source);
        }

        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

public sealed class TrajetDemarreHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<TrajetDemarre>
{
    public async Task HandleAsync(TrajetDemarre integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var trajet = await projections.GetTrajetAsync(e.TrajetId, cancellationToken);
        if (trajet is null)
        {
            trajet = new TrajetLocal(e.TrajetId, e.PersonneId, e.AffilieId);
            projections.Add(trajet);
        }

        trajet.Demarrer(e.DateDemande);
        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

public sealed class TrajetTermineHandler(IProjectionRepository projections, MiseAJourProjection miseAJour)
    : IIntegrationEventHandler<TrajetTermine>
{
    public async Task HandleAsync(TrajetTermine integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var trajet = await projections.GetTrajetAsync(e.TrajetId, cancellationToken);
        if (trajet is null)
        {
            trajet = new TrajetLocal(e.TrajetId, e.PersonneId, e.AffilieId);
            projections.Add(trajet);
        }

        trajet.Terminer(e.DateFin, e.Statut);
        await miseAJour.TerminerAsync([e.PersonneId], cancellationToken);
    }
}

/// <summary>AFF-32 : dernière version des listes nominatives, pour l'alerte « non revue depuis 12 mois » (lue à la demande).</summary>
public sealed class ListeNominativeGenereeHandler(IProjectionRepository projections, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<ListeNominativeGeneree>
{
    public async Task HandleAsync(ListeNominativeGeneree integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var liste = await projections.GetListeNominativeAsync(e.ListeNominativeId, cancellationToken);
        if (liste is null)
        {
            projections.Add(new ListeNominativeLocale(e.ListeNominativeId, e.AffilieId, e.TypeListe, e.Version, e.DateReference, e.DateGeneration));
        }
        else
        {
            liste.Appliquer(e.AffilieId, e.TypeListe, e.Version, e.DateReference, e.DateGeneration);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
