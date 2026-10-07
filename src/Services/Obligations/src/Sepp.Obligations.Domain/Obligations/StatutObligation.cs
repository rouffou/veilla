namespace Sepp.Obligations.Domain.Obligations;

/// <summary>Statuts d'une obligation (SAN-02).</summary>
public enum StatutObligation
{
    /// <summary>À planifier : due, sans rendez-vous.</summary>
    APlanifier,

    /// <summary>Planifié : un rendez-vous couvre l'obligation (RendezVousPlanifie).</summary>
    Planifie,

    /// <summary>Convoqué : la convocation du rendez-vous a été envoyée.</summary>
    Convoque,

    /// <summary>Réalisé : un examen du type attendu a été clôturé (ExamenCloture).</summary>
    Realise,

    /// <summary>Absent : le travailleur ne s'est pas présenté ; à reconvoquer (SAN-13).</summary>
    Absent,

    /// <summary>Reporté à une date ultérieure.</summary>
    Reporte,

    /// <summary>Annulé : l'obligation n'est plus due (recalcul) ou a été annulée par une décision.</summary>
    Annule,

    /// <summary>Excusé : absence justifiée ; à reconvoquer.</summary>
    Excuse,

    /// <summary>Sorti de l'entreprise : le travailleur n'est plus occupé chez l'affilié.</summary>
    SortiEntreprise,
}

/// <summary>Motif d'annulation : un code, jamais un texte libre (aucune donnée de santé, ARC-06).</summary>
public enum MotifAnnulation
{
    /// <summary>L'obligation n'est plus due après recalcul (changement de poste, de risque, fin de période…).</summary>
    Recalcul,

    /// <summary>Décision du CPMT.</summary>
    DecisionCpmt,

    /// <summary>Doublon d'une autre obligation.</summary>
    Doublon,

    /// <summary>Demande de l'affilié (par exemple affectation finalement non réalisée).</summary>
    DemandeAffilie,

    Autre,
}

/// <summary>
/// SAN-02 : machine à états des obligations. Les transitions autorisées sont déclarées une seule fois ici ;
/// toute autre transition est refusée par l'agrégat.
/// </summary>
public static class MachineEtatsObligation
{
    private static readonly StatutObligation[] Tous = Enum.GetValues<StatutObligation>();

    private static readonly Dictionary<StatutObligation, HashSet<StatutObligation>> Transitions = new()
    {
        [StatutObligation.APlanifier] =
        [
            StatutObligation.Planifie, StatutObligation.Realise, StatutObligation.Reporte, StatutObligation.Annule,
            StatutObligation.Excuse, StatutObligation.SortiEntreprise,
        ],
        [StatutObligation.Planifie] =
        [
            StatutObligation.Planifie, StatutObligation.Convoque, StatutObligation.APlanifier, StatutObligation.Realise,
            StatutObligation.Absent, StatutObligation.Reporte, StatutObligation.Annule, StatutObligation.Excuse,
            StatutObligation.SortiEntreprise,
        ],
        [StatutObligation.Convoque] =
        [
            StatutObligation.Planifie, StatutObligation.APlanifier, StatutObligation.Realise, StatutObligation.Absent,
            StatutObligation.Reporte, StatutObligation.Annule, StatutObligation.Excuse, StatutObligation.SortiEntreprise,
        ],
        [StatutObligation.Absent] =
        [
            StatutObligation.Planifie, StatutObligation.Realise, StatutObligation.Reporte, StatutObligation.Annule,
            StatutObligation.Excuse, StatutObligation.SortiEntreprise,
        ],
        [StatutObligation.Reporte] =
        [
            StatutObligation.Planifie, StatutObligation.APlanifier, StatutObligation.Realise, StatutObligation.Reporte,
            StatutObligation.Annule, StatutObligation.SortiEntreprise,
        ],
        [StatutObligation.Excuse] =
        [
            StatutObligation.Planifie, StatutObligation.APlanifier, StatutObligation.Realise, StatutObligation.Reporte,
            StatutObligation.Annule, StatutObligation.SortiEntreprise,
        ],

        // Réalisé est définitif. Annulé et sorti ne se rouvrent que par recalcul (réactivation contrôlée par l'agrégat).
        [StatutObligation.Realise] = [],
        [StatutObligation.Annule] = [StatutObligation.APlanifier, StatutObligation.Realise],
        [StatutObligation.SortiEntreprise] = [StatutObligation.APlanifier, StatutObligation.Realise],
    };

    public static bool EstAutorisee(StatutObligation depuis, StatutObligation vers) => Transitions[depuis].Contains(vers);

    public static IReadOnlyList<StatutObligation> Suivants(StatutObligation depuis) => Tous.Where(s => EstAutorisee(depuis, s)).ToList();

    /// <summary>Statuts d'une obligation encore à honorer.</summary>
    public static bool EstOuvert(StatutObligation statut) =>
        statut is StatutObligation.APlanifier or StatutObligation.Planifie or StatutObligation.Convoque or StatutObligation.Absent
            or StatutObligation.Reporte or StatutObligation.Excuse;

    /// <summary>Statuts ouverts sans rendez-vous à venir : l'obligation doit être (re)planifiée (SAN-03, AFF-32).</summary>
    public static bool EstAPlanifier(StatutObligation statut) =>
        statut is StatutObligation.APlanifier or StatutObligation.Absent or StatutObligation.Reporte or StatutObligation.Excuse;

    public static bool EstPlanifie(StatutObligation statut) => statut is StatutObligation.Planifie or StatutObligation.Convoque;
}
