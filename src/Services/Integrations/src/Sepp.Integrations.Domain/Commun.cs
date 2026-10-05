using System.Globalization;
using System.Text.RegularExpressions;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Integrations.Domain;

/// <summary>Contrôles de texte communs aux agrégats du service.</summary>
public static class Texte
{
    public static string Obligatoire(string? valeur, string nom, int longueurMaximale)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new DomainException($"{nom} est obligatoire.");
        }

        var texte = valeur.Trim();
        return texte.Length <= longueurMaximale
            ? texte
            : throw new DomainException($"{nom} dépasse {longueurMaximale} caractères.");
    }

    public static string? Facultatif(string? valeur, string nom, int longueurMaximale) =>
        string.IsNullOrWhiteSpace(valeur) ? null : Obligatoire(valeur, nom, longueurMaximale);

    public static string Tronquer(string valeur, int longueurMaximale) =>
        valeur.Length <= longueurMaximale ? valeur : valeur[..(longueurMaximale - 1)] + "…";
}

/// <summary>
/// Garde-fou DAT-06 : masque toute suite de onze chiffres (éventuellement séparés par des points, espaces ou tirets)
/// ayant la forme d'un NISS, avant qu'un texte reçu d'un système externe ne soit stocké ou journalisé.
/// </summary>
public static partial class DonneesPersonnelles
{
    public const string Masque = "***********";

    public static string Masquer(string texte) => Niss().Replace(texte, Masque);

    [GeneratedRegex(@"(?<!\d)\d{2}[.\s-]?\d{2}[.\s-]?\d{2}[.\s-]?\d{3}[.\s-]?\d{2}(?!\d)")]
    private static partial Regex Niss();
}

/// <summary>
/// Numéros de la Banque-Carrefour des Entreprises : dix chiffres avec contrôle modulo 97 ; un numéro d'entreprise
/// commence par 0 ou 1, un numéro d'unité d'établissement par 2 à 8. Forme canonique : dix chiffres sans séparateur.
/// </summary>
public static class NumerosBce
{
    public static string Entreprise(string? saisie) =>
        Normaliser(saisie, "numéro d'entreprise BCE", c => c is '0' or '1', "0 ou 1");

    public static string UniteEtablissement(string? saisie) =>
        Normaliser(saisie, "numéro d'unité d'établissement BCE", c => c is >= '2' and <= '8', "un chiffre de 2 à 8");

    public static bool EstEntrepriseValide(string? saisie, out string numero)
    {
        try
        {
            numero = Entreprise(saisie);
            return true;
        }
        catch (DomainException)
        {
            numero = string.Empty;
            return false;
        }
    }

    /// <summary>Calcule les deux chiffres de contrôle d'une base de huit chiffres.</summary>
    public static string AvecControle(string huitChiffres)
    {
        var baseNumero = long.Parse(huitChiffres, CultureInfo.InvariantCulture);
        return $"{huitChiffres}{97 - (baseNumero % 97):D2}";
    }

    public static string Formater(string chiffres) => $"{chiffres[..4]}.{chiffres[4..7]}.{chiffres[7..]}";

    private static string Normaliser(string? saisie, string nature, Func<char, bool> premierChiffreAdmis, string premiersChiffres)
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

        if (AvecControle(chiffres[..8]) != chiffres)
        {
            throw new DomainException($"Le {nature} '{saisie}' est invalide (contrôle modulo 97).");
        }

        return chiffres;
    }
}
