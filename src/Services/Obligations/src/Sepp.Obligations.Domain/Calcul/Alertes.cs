using System.Globalization;

using Sepp.BuildingBlocks.Domain.Calendar;

using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Domain.Calcul;

/// <summary>Alertes AFF-32.</summary>
public enum TypeAlerte
{
    /// <summary>Travailleur exposé à un risque dont une obligation de surveillance due n'est pas planifiée.</summary>
    TravailleurExposeSansSurveillancePlanifiee,

    /// <summary>
    /// Poste occupé dont aucun profil de risques validé n'a été reçu. L'analyse de risques elle-même relève du service
    /// Prévention, qui ne publie encore aucun événement : le profil validé par le CPMT (AFF-14) en est l'indicateur.
    /// </summary>
    PosteSansAnalyseRisques,

    /// <summary>Liste nominative non revue depuis SANTE.LISTES_NOMINATIVES.REVUE_ALERTE (12 mois par défaut).</summary>
    ListeNominativeNonRevue,

    // Alertes du processus de reprise (ARC-33, POR-04) : voir ProcessusReprise.Alertes.
    RepriseEcheanceMenacee,
    RepriseHorsDelai,
    RepriseConvocationNonRemise,
    RepriseUrgenceNonCouverte,
    RepriseReplanificationRequise,
    RepriseRendezVousApresDateLimite,
    RepriseRendezVousSansCloture,
    RepriseDecisionEnAttente,
    RepriseExpiree,
}

public sealed record Alerte(
    TypeAlerte Type,
    Guid AffilieId,
    Guid? PersonneId,
    Guid? PosteId,
    string? TypeListe,
    DateOnly? Depuis,
    string Message,
    IReadOnlyList<Guid> ObligationIds);

/// <summary>AFF-32 : détection des alertes d'un affilié à partir des obligations et des projections.</summary>
public static class DetectionAlertes
{
    /// <summary>
    /// Travailleurs dont une obligation liée à un risque (préalable, périodique, actes, prolongée) est à planifier
    /// (à planifier, absent, excusé, reporté) alors que son échéance est dépassée ou tombe dans l'horizon.
    /// </summary>
    public static IEnumerable<Alerte> TravailleursSansSurveillancePlanifiee(
        Guid affilieId, IEnumerable<Obligation> obligations, DateOnly aujourdHui, int horizonJours)
    {
        var horizon = aujourdHui.AddDays(horizonJours);
        return obligations
            .Where(o => o.AffilieId == affilieId
                        && o.Origine is OrigineObligation.Regle or OrigineObligation.Surcharge
                        && MachineEtatsObligation.EstAPlanifier(o.Statut)
                        && o.Echeance <= horizon)
            .GroupBy(o => o.PersonneId)
            .Select(g =>
            {
                var premiere = g.Min(o => o.Echeance);
                var risques = string.Join(", ", g.SelectMany(o => o.CodesRisques).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                return new Alerte(
                    TypeAlerte.TravailleurExposeSansSurveillancePlanifiee,
                    affilieId,
                    g.Key,
                    null,
                    null,
                    premiere,
                    premiere < aujourdHui
                        ? $"Travailleur exposé ({risques}) sans surveillance planifiée : échéance dépassée depuis le {Jour(premiere)}."
                        : $"Travailleur exposé ({risques}) sans surveillance planifiée : échéance le {Jour(premiere)}.",
                    g.OrderBy(o => o.Echeance).Select(o => o.Id).ToList());
            })
            .OrderBy(a => a.Depuis);
    }

    /// <summary>Postes occupés à la date dont aucun profil de risques n'est connu.</summary>
    public static IEnumerable<Alerte> PostesSansAnalyseRisques(
        Guid affilieId, IEnumerable<AffectationLocale> affectationsActives, IReadOnlySet<Guid> postesAvecProfil) =>
        affectationsActives
            .Where(a => !postesAvecProfil.Contains(a.PosteId))
            .GroupBy(a => a.PosteId)
            .Select(g => new Alerte(
                TypeAlerte.PosteSansAnalyseRisques,
                affilieId,
                null,
                g.Key,
                null,
                g.Min(a => a.DateDebut),
                $"Poste occupé par {g.Select(a => a.PersonneId).Distinct().Count()} travailleur(s) sans analyse de risques validée (aucun profil de risques reçu).",
                []))
            .OrderBy(a => a.Depuis);

    /// <summary>Dernière version de chaque type de liste nominative de l'affilié, si elle n'a pas été revue dans le délai.</summary>
    public static IEnumerable<Alerte> ListesNonRevues(
        Guid affilieId, IEnumerable<ListeNominativeLocale> listes, DureeLegale delaiRevue, BusinessCalendar calendrier, DateOnly aujourdHui) =>
        listes
            .Where(l => l.AffilieId == affilieId)
            .GroupBy(l => l.TypeListe, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(l => l.DateGeneration).ThenByDescending(l => l.Version).First())
            .Where(l => delaiRevue.AjouterA(l.DateGeneration, calendrier) <= aujourdHui)
            .Select(l => new Alerte(
                TypeAlerte.ListeNominativeNonRevue,
                affilieId,
                null,
                null,
                l.TypeListe,
                l.DateGeneration,
                $"Liste nominative {l.TypeListe} (version {l.Version.ToString(CultureInfo.InvariantCulture)}) non revue depuis le {Jour(l.DateGeneration)} (délai : {delaiRevue.Valeur} {delaiRevue.Unite}).",
                []))
            .OrderBy(a => a.Depuis);

    private static string Jour(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
