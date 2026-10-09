namespace Sepp.Contracts.Decisions;

/// <summary>
/// Codes partagés des catégories de décision d'évaluation de santé (SAN-31, annexe I.4-2) : vocabulaire de
/// <c>DecisionEmise.Categorie</c>. Source de vérité unique, reprise des codes émis par le service Surveillance médicale
/// (<c>CodesDecision.Code()</c>, test de conformité côté Surveillance médicale) ; les consommateurs (Documents…) y
/// renvoient au lieu de redéclarer leurs propres chaînes. Une catégorie est la seule partie de la décision qui sort de la
/// zone médicale : jamais le motif médical (ARC-06). Espace de noms dédié pour éviter la collision avec
/// <c>CategorieDecision</c> / <c>CodesDecision</c> du domaine de Surveillance médicale.
/// </summary>
public static class CategoriesDecision
{
    public const string Apte = "APTE";
    public const string ApteAvecMesures = "APTE_AVEC_MESURES";
    public const string InaptitudeTemporaire = "INAPTITUDE_TEMPORAIRE";
    public const string InaptitudeDefinitive = "INAPTITUDE_DEFINITIVE";
    public const string Mutation = "MUTATION";
    public const string EcartementMaternite = "ECARTEMENT_MATERNITE";

    /// <summary>Tous les codes connus, dans l'ordre de déclaration (SAN-31).</summary>
    public static readonly IReadOnlyList<string> Connus =
    [
        Apte, ApteAvecMesures, InaptitudeTemporaire, InaptitudeDefinitive, Mutation, EcartementMaternite,
    ];
}
