namespace Sepp.Contracts.Examens;

/// <summary>
/// Codes partagés des types d'examen (§5.1) : vocabulaire commun de <c>ObligationCreee.TypeExamen</c>,
/// <c>ExamenCloture.TypeExamen</c>, <c>ObligationCloturee.TypeExamen</c> et <c>ConvocationEmise.TypeActe</c>. Source de
/// vérité unique, reprise des codes du service Obligations (<c>TypeObligation.Code()</c>) ; les autres services y
/// renvoient au lieu de redéclarer leurs propres chaînes. Un code est une catégorie, jamais un contenu clinique (ARC-06).
/// </summary>
public static class TypesExamen
{
    public const string EvaluationPrealable = "EVALUATION_PREALABLE";
    public const string EvaluationPeriodique = "EVALUATION_PERIODIQUE";
    public const string ActesMedicauxSupplementaires = "ACTES_MEDICAUX_SUPPLEMENTAIRES";
    public const string ExamenReprise = "EXAMEN_REPRISE";
    public const string VisitePreReprise = "VISITE_PRE_REPRISE";
    public const string ConsultationSpontanee = "CONSULTATION_SPONTANEE";
    public const string ProtectionMaternite = "PROTECTION_MATERNITE";
    public const string SurveillanceProlongee = "SURVEILLANCE_PROLONGEE";
    public const string EstimationPotentielTravail = "ESTIMATION_POTENTIEL_TRAVAIL";
    public const string EvaluationReintegration = "EVALUATION_REINTEGRATION";

    /// <summary>Examen relevant d'une autre législation que la surveillance de santé du code du bien-être (Surveillance médicale).</summary>
    public const string AutreLegislation = "AUTRE_LEGISLATION";

    /// <summary>Tous les codes connus, dans l'ordre de déclaration.</summary>
    public static readonly IReadOnlyList<string> Connus =
    [
        EvaluationPrealable, EvaluationPeriodique, ActesMedicauxSupplementaires, ExamenReprise, VisitePreReprise,
        ConsultationSpontanee, ProtectionMaternite, SurveillanceProlongee, EstimationPotentielTravail,
        EvaluationReintegration, AutreLegislation,
    ];
}
