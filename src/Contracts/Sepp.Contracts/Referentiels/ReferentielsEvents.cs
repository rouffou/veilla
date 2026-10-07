namespace Sepp.Contracts.Referentiels;

/// <summary>Un paramètre légal (délai, fréquence, durée) a une nouvelle valeur pour une période (ARC-21).</summary>
[EventContract("referentiels.parametre-legal-modifie", 1)]
public sealed record ParametreLegalModifie(
    string Code,
    decimal Valeur,
    string Unite,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu) : IntegrationEvent;

/// <summary>Une nomenclature partagée (NACE, commission paritaire, risque…) a changé de version (DAT-07).</summary>
[EventContract("referentiels.nomenclature-modifiee", 1)]
public sealed record NomenclatureModifiee(
    Guid NomenclatureId,
    string Code,
    int Version) : IntegrationEvent;

/// <summary>
/// Le calendrier des jours fériés d'une année a été modifié (DAT-08). <c>JoursSupplementaires</c> est l'état complet des
/// jours fériés supplémentaires de l'année (jours de remplacement, fêtes des Communautés), en plus des dix jours fériés
/// légaux que chaque service calcule : les services qui calculent des délais en jours ouvrables (Obligations) tiennent
/// ainsi leur calendrier à jour sans appel synchrone. Champ ajouté de façon compatible (ARC-34) : facultatif, <c>null</c>
/// pour un producteur antérieur, ignoré par les consommateurs antérieurs.
/// </summary>
[EventContract("referentiels.jours-feries-modifies", 1)]
public sealed record JoursFeriesModifies(int Annee, IReadOnlyList<DateOnly>? JoursSupplementaires = null) : IntegrationEvent;
