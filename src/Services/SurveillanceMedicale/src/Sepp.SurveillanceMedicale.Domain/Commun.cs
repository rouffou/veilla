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

// Les codes de type d'examen (§5.1) ne sont plus déclarés ici : la source de vérité est Sepp.Contracts.Examens.TypesExamen
// (saga « examen de reprise », lot 6). Le domaine ne dépend pas des contrats ; il reçoit le code tel quel et le contrôle
// par Garde.Code (un code inconnu reste accepté pour ne pas bloquer un nouveau type).
