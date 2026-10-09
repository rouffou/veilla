namespace Sepp.Contracts.Reintegration;

/// <summary>
/// Codes partagés du domaine de la réintégration (§5.8) portés par les événements <c>integrations.incapacite-notifiee</c>,
/// <c>reintegration.trajet-demarre</c> et <c>reintegration.trajet-termine</c>. Source de vérité unique : producteurs et
/// consommateurs y renvoient au lieu de redéclarer leurs chaînes. Des catégories seulement, jamais un motif médical (ARC-06).
/// </summary>
public static class CodesReintegration
{
    /// <summary>Émetteur de la notification d'incapacité (SAN-60, §15.3 <c>incapacite.source</c>).</summary>
    public static class SourcesIncapacite
    {
        public const string Employeur = "EMPLOYEUR";
        public const string MedecinConseil = "MEDECIN_CONSEIL";

        /// <summary>Notification reçue hors flux (courrier, appel) et saisie par le CPMT ou l'infirmier ; jamais publiée.</summary>
        public const string SaisieManuelle = "SAISIE_MANUELLE";

        public static readonly IReadOnlyList<string> Connus = [Employeur, MedecinConseil, SaisieManuelle];
    }

    /// <summary>Initiateur de la demande de trajet (SAN-62, §15.3 <c>trajet.initiateur</c>).</summary>
    public static class InitiateursTrajet
    {
        public const string Travailleur = "TRAVAILLEUR";
        public const string MedecinTraitant = "MEDECIN_TRAITANT";
        public const string MedecinConseil = "MEDECIN_CONSEIL";
        public const string Employeur = "EMPLOYEUR";
        public const string Cpmt = "CPMT";

        public static readonly IReadOnlyList<string> Connus = [Travailleur, MedecinTraitant, MedecinConseil, Employeur, Cpmt];
    }

    /// <summary>Issue d'un trajet terminé (SAN-62).</summary>
    public static class StatutsFinTrajet
    {
        /// <summary>Le travailleur a accepté le plan de réintégration.</summary>
        public const string PlanAccepte = "PLAN_ACCEPTE";

        /// <summary>Le travailleur a refusé le plan de réintégration.</summary>
        public const string PlanRefuse = "PLAN_REFUSE";

        /// <summary>L'employeur a remis un rapport motivé (aucun plan de réintégration).</summary>
        public const string RapportMotive = "RAPPORT_MOTIVE";

        /// <summary>Trajet arrêté avant son terme (demande retirée, incapacité annulée) : compensation.</summary>
        public const string Annule = "ANNULE";

        public static readonly IReadOnlyList<string> Connus = [PlanAccepte, PlanRefuse, RapportMotive, Annule];
    }
}
