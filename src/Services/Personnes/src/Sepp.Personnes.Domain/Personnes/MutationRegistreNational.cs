using Sepp.BuildingBlocks.Domain;

namespace Sepp.Personnes.Domain.Personnes;

/// <summary>AFF-20, AFF-22 : nature d'une mutation communiquée par le registre national (via la BCSS et le service Intégrations).</summary>
public enum TypeMutationRegistreNational
{
    ChangementAdresse,
    ChangementNom,
    ChangementPrenom,
    ChangementLangue,
    Deces,
}

/// <summary>
/// Mutation du registre national à appliquer à une personne. <see cref="Reference"/> est la clé d'idempotence ;
/// <see cref="DateEffet"/> est la date d'effet (pour un décès : la date du décès). Seule la valeur propre au
/// <see cref="Type"/> est renseignée.
/// </summary>
public sealed record DemandeMutationRegistreNational(
    string Reference,
    TypeMutationRegistreNational Type,
    DateOnly DateEffet,
    Adresse? Adresse = null,
    string? Nom = null,
    string? Prenom = null,
    Language? Langue = null);

/// <summary>
/// AFF-20 : une occupation ne peut pas commencer après la date de décès de la personne. Code d'erreur explicite
/// <see cref="Code"/>, repris par l'API (et donc par la trace du rejet côté DIMONA).
/// </summary>
public sealed class OccupationApresDecesException(DateOnly dateDeces, DateOnly dateDebut)
    : DomainException($"L'occupation débute le {dateDebut:yyyy-MM-dd}, après le décès du travailleur ({dateDeces:yyyy-MM-dd}) : elle est refusée.")
{
    public const string Code = "occupation.apres-deces";
}

/// <summary>
/// Historique d'une mutation appliquée (DAT-04) : qui l'a appliquée et quand (colonnes d'audit), sa date d'effet et les
/// valeurs avant / après. Les valeurs sont des données d'identité : chiffrées au repos (ARC-45) et jamais publiées (ARC-06).
/// La référence de la mutation est unique : elle garantit l'idempotence du traitement.
/// </summary>
public sealed class MutationRegistreNational : Entity
{
    private MutationRegistreNational()
    {
    }

    internal MutationRegistreNational(Guid id, string reference, TypeMutationRegistreNational type, DateOnly dateEffet, int rang, string? avant, string? apres)
        : base(id)
    {
        Reference = reference;
        Type = type;
        DateEffet = dateEffet;
        Rang = rang;
        Avant = avant;
        Apres = apres;
    }

    public string Reference { get; private set; } = string.Empty;

    public TypeMutationRegistreNational Type { get; private set; }

    public DateOnly DateEffet { get; private set; }

    /// <summary>
    /// Ordre de réception parmi les mutations de la personne (1, 2, 3…). Départage deux mutations de même type et de même
    /// date d'effet : la dernière reçue détermine la valeur courante (AFF-22).
    /// </summary>
    public int Rang { get; private set; }

    /// <summary>Valeur remplacée (JSON), <c>null</c> si elle était inconnue.</summary>
    public string? Avant { get; private set; }

    /// <summary>Nouvelle valeur (JSON).</summary>
    public string? Apres { get; private set; }
}
