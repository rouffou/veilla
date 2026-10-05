using Sepp.BuildingBlocks.Domain;

namespace Sepp.PostesRisques.Domain;

/// <summary>Cycle de vie d'une proposition soumise à la validation du CPMT (AFF-14, AFF-31).</summary>
public enum StatutProposition
{
    Soumise,
    Validee,
    Refusee,
}

/// <summary>Canal par lequel une proposition a été soumise.</summary>
public enum OrigineProposition
{
    /// <summary>Utilisateur interne du SEPP (gestionnaire, CPMT).</summary>
    Interne,

    /// <summary>Employeur ou SIPP via le portail employeur (POR-03, AFF-31).</summary>
    PortailEmployeur,
}

/// <summary>Nature d'une ligne de proposition.</summary>
public enum TypeModification
{
    Ajout,
    Modification,
    Retrait,
}

/// <summary>Règles de saisie partagées par les agrégats du service.</summary>
internal static class Saisie
{
    public static string Obligatoire(string? valeur, string nom, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new DomainException($"{nom} est obligatoire.");
        }

        var nettoyee = valeur.Trim();
        return nettoyee.Length > longueurMax
            ? throw new DomainException($"{nom} ne peut pas dépasser {longueurMax} caractères.")
            : nettoyee;
    }

    public static string? Facultatif(string? valeur, string nom, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return null;
        }

        var nettoyee = valeur.Trim();
        return nettoyee.Length > longueurMax
            ? throw new DomainException($"{nom} ne peut pas dépasser {longueurMax} caractères.")
            : nettoyee;
    }

    /// <summary>Code en majuscules (codes de nomenclature, de risque, d'acte, de vaccin).</summary>
    public static string Code(string? valeur, string nom) => Obligatoire(valeur, nom, 50).ToUpperInvariant();

    public static string? CodeFacultatif(string? valeur, string nom) => Facultatif(valeur, nom, 50)?.ToUpperInvariant();

    public static Guid Identifiant(Guid valeur, string nom) =>
        valeur == Guid.Empty ? throw new DomainException($"{nom} est obligatoire.") : valeur;
}
