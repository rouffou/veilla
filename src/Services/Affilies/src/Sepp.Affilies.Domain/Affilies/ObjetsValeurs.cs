using System.Globalization;
using System.Text.RegularExpressions;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>Catégorie tarifaire de l'affilié (AFF-01), qui détermine la cotisation forfaitaire.</summary>
public enum CategorieTarifaire
{
    A,
    B,
    C,
    D,
}

/// <summary>
/// Régime linguistique de l'affilié selon la législation sur l'emploi des langues (NF-41) :
/// région de langue du siège d'exploitation, Bruxelles-Capitale étant bilingue.
/// </summary>
public enum RegimeLinguistique
{
    Francais,
    Neerlandais,
    Allemand,
    BruxellesCapitale,
}

/// <summary>Cycle de vie de l'affiliation (AFF-01, AFF-06).</summary>
public enum StatutAffilie
{
    Actif,

    /// <summary>Affiliation résiliée à la date de fin.</summary>
    Resilie,

    /// <summary>Absorbé par un autre affilié (fusion).</summary>
    Absorbe,

    /// <summary>Scindé en plusieurs affiliés.</summary>
    Scinde,

    /// <summary>Transféré vers un autre SEPP.</summary>
    Transfere,
}

/// <summary>
/// Contrôle commun des numéros de la Banque-Carrefour des Entreprises : dix chiffres dont les deux derniers
/// valent 97 − (les huit premiers modulo 97). Les anciens numéros à neuf chiffres sont complétés d'un zéro.
/// </summary>
internal static class ControleBce
{
    public static string Normaliser(string? saisie, string nature, Func<char, bool> premierChiffreAdmis, string premiersChiffres)
    {
        if (string.IsNullOrWhiteSpace(saisie))
        {
            throw new DomainException($"Le {nature} est obligatoire.");
        }

        var texte = saisie.Trim().ToUpperInvariant();
        if (texte.StartsWith("BE", StringComparison.Ordinal))
        {
            texte = texte[2..];
        }

        var chiffres = string.Concat(texte.Where(c => c is not ('.' or ' ' or '-' or '/')));
        if (chiffres.Length == 9)
        {
            chiffres = "0" + chiffres;
        }

        if (chiffres.Length != 10 || !chiffres.All(char.IsAsciiDigit))
        {
            throw new DomainException($"Le {nature} '{saisie}' doit comporter dix chiffres.");
        }

        if (!premierChiffreAdmis(chiffres[0]))
        {
            throw new DomainException($"Le {nature} '{saisie}' doit commencer par {premiersChiffres}.");
        }

        var baseNumero = long.Parse(chiffres[..8], CultureInfo.InvariantCulture);
        var controle = int.Parse(chiffres[8..], CultureInfo.InvariantCulture);
        if (97 - (baseNumero % 97) != controle)
        {
            throw new DomainException($"Le {nature} '{saisie}' est invalide (contrôle modulo 97).");
        }

        return chiffres;
    }

    public static string Formater(string chiffres) => $"{chiffres[..4]}.{chiffres[4..7]}.{chiffres[7..]}";
}

/// <summary>
/// Numéro d'entreprise BCE (AFF-01) : identifiant métier unique de l'affilié (DAT-01).
/// Stocké sous forme de dix chiffres, présenté au format <c>0123.456.749</c>.
/// </summary>
public sealed record NumeroBce
{
    public NumeroBce(string value) =>
        Value = ControleBce.Normaliser(value, "numéro d'entreprise BCE", c => c is '0' or '1', "0 ou 1");

    /// <summary>Dix chiffres, sans séparateur.</summary>
    public string Value { get; }

    public string Formate => ControleBce.Formater(Value);

    public static bool TryParse(string? value, out NumeroBce? numero)
    {
        try
        {
            numero = new NumeroBce(value!);
            return true;
        }
        catch (DomainException)
        {
            numero = null;
            return false;
        }
    }

    public override string ToString() => Formate;
}

/// <summary>Numéro d'unité d'établissement BCE (AFF-02) : dix chiffres commençant par 2 à 8, contrôle modulo 97.</summary>
public sealed record NumeroUniteEtablissement
{
    public NumeroUniteEtablissement(string value) =>
        Value = ControleBce.Normaliser(value, "numéro d'unité d'établissement BCE", c => c is >= '2' and <= '8', "un chiffre de 2 à 8");

    public string Value { get; }

    public string Formate => ControleBce.Formater(Value);

    public override string ToString() => Formate;
}

/// <summary>Adresse postale d'une unité d'établissement ou d'un site (AFF-02).</summary>
public sealed record Adresse
{
    public Adresse(string rue, string numero, string? boite, string codePostal, string localite, string codePays = "BE")
    {
        Rue = Texte.Obligatoire(rue, "La rue", 200);
        Numero = Texte.Obligatoire(numero, "Le numéro", 20);
        Boite = Texte.Facultatif(boite, "La boîte", 20);
        CodePostal = Texte.Obligatoire(codePostal, "Le code postal", 20);
        Localite = Texte.Obligatoire(localite, "La localité", 100);
        var pays = Texte.Obligatoire(codePays, "Le code pays", 2).ToUpperInvariant();
        if (pays.Length != 2 || !pays.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException($"Le code pays '{codePays}' doit être un code ISO 3166-1 alpha-2.");
        }

        if (pays == "BE" && (CodePostal.Length != 4 || !CodePostal.All(char.IsAsciiDigit)))
        {
            throw new DomainException($"Le code postal belge '{codePostal}' doit comporter quatre chiffres.");
        }

        CodePays = pays;
    }

    public string Rue { get; }

    public string Numero { get; }

    public string? Boite { get; }

    public string CodePostal { get; }

    public string Localite { get; }

    public string CodePays { get; }
}

/// <summary>Règles de saisie des textes libres.</summary>
internal static partial class Texte
{
    public static string Obligatoire(string? valeur, string libelle, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new DomainException($"{libelle} est obligatoire.");
        }

        var texte = valeur.Trim();
        return texte.Length <= longueurMax
            ? texte
            : throw new DomainException($"{libelle} ne peut pas dépasser {longueurMax} caractères.");
    }

    public static string? Facultatif(string? valeur, string libelle, int longueurMax) =>
        string.IsNullOrWhiteSpace(valeur) ? null : Obligatoire(valeur, libelle, longueurMax);

    /// <summary>Code NACE-BEL : deux chiffres (division) suivis éventuellement d'un point et d'un à trois chiffres.</summary>
    public static string CodeNace(string? code)
    {
        var texte = Obligatoire(code, "Le code NACE", 10);
        return NaceRegex().IsMatch(texte) ? texte : throw new DomainException($"Code NACE-BEL invalide : '{code}' (attendu : 86.210).");
    }

    /// <summary>Commission paritaire : trois chiffres, éventuellement suivis d'une sous-commission (<c>330.01</c>).</summary>
    public static string CommissionParitaire(string? code)
    {
        var texte = Obligatoire(code, "La commission paritaire", 10);
        return CommissionRegex().IsMatch(texte) ? texte : throw new DomainException($"Commission paritaire invalide : '{code}' (attendu : 200 ou 330.01).");
    }

    public static string? Email(string? email)
    {
        var texte = Facultatif(email, "L'adresse électronique", 254);
        return texte is null || EmailRegex().IsMatch(texte) ? texte : throw new DomainException($"Adresse électronique invalide : '{email}'.");
    }

    [GeneratedRegex(@"^\d{2}(\.\d{1,3})?$")]
    private static partial Regex NaceRegex();

    [GeneratedRegex(@"^\d{3}(\.\d{2})?$")]
    private static partial Regex CommissionRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
