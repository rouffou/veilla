using System.Text.RegularExpressions;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain;

/// <summary>Contrôles de saisie partagés par les agrégats de la zone médicale.</summary>
public static partial class Garde
{
    /// <summary>Longueur maximale d'un texte clinique libre (anamnèse, examen clinique, justification…).</summary>
    public const int TexteCliniqueMaximum = 20_000;

    /// <summary>Texte obligatoire, nettoyé, de longueur bornée.</summary>
    public static string Requis(string? valeur, string champ, int longueurMaximale)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new DomainException($"Le champ « {champ} » est obligatoire.");
        }

        var nettoye = valeur.Trim();
        return nettoye.Length > longueurMaximale
            ? throw new DomainException($"Le champ « {champ} » est limité à {longueurMaximale} caractères.")
            : nettoye;
    }

    /// <summary>Texte facultatif : <c>null</c> si vide, sinon nettoyé et borné.</summary>
    public static string? Facultatif(string? valeur, string champ, int longueurMaximale) =>
        string.IsNullOrWhiteSpace(valeur) ? null : Requis(valeur, champ, longueurMaximale);

    /// <summary>
    /// Code de référentiel (type d'examen, mesure, vaccin, agent, risque…) : majuscules, chiffres, « _ », « . » et « - ».
    /// Les codes sont les seules valeurs qui peuvent sortir de la zone médicale (ARC-06) : aucun texte libre.
    /// </summary>
    public static string Code(string? valeur, string champ)
    {
        var code = Requis(valeur, champ, 60).ToUpperInvariant();
        return FormatCode().IsMatch(code)
            ? code
            : throw new DomainException($"Le champ « {champ} » doit être un code (lettres, chiffres, « _ », « . », « - ») : « {code} » refusé.");
    }

    public static Guid Identifiant(Guid valeur, string champ) =>
        valeur == Guid.Empty ? throw new DomainException($"L'identifiant « {champ} » est obligatoire.") : valeur;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_.-]*$")]
    private static partial Regex FormatCode();
}

/// <summary>
/// Types d'examen (§5.1). Les codes sont partagés avec les services Obligations et Planification
/// (<c>ObligationCreee.TypeExamen</c>) ; un code inconnu reste accepté tel quel pour ne pas bloquer un nouveau type.
/// </summary>
public static class TypesExamen
{
    public const string EvaluationPrealable = "EVALUATION_PREALABLE";
    public const string EvaluationPeriodique = "EVALUATION_PERIODIQUE";
    public const string ActesSupplementaires = "ACTES_SUPPLEMENTAIRES";
    public const string ExamenReprise = "EXAMEN_REPRISE";
    public const string VisitePreReprise = "VISITE_PRE_REPRISE";
    public const string ConsultationSpontanee = "CONSULTATION_SPONTANEE";
    public const string ProtectionMaternite = "PROTECTION_MATERNITE";
    public const string SurveillanceProlongee = "SURVEILLANCE_PROLONGEE";
    public const string EvaluationReintegration = "EVALUATION_REINTEGRATION";
    public const string AutreLegislation = "AUTRE_LEGISLATION";

    public static IReadOnlyList<string> Connus { get; } =
    [
        EvaluationPrealable, EvaluationPeriodique, ActesSupplementaires, ExamenReprise, VisitePreReprise,
        ConsultationSpontanee, ProtectionMaternite, SurveillanceProlongee, EvaluationReintegration, AutreLegislation,
    ];
}
