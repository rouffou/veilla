namespace Sepp.Contracts.Audit;

/// <summary>
/// Trace d'un accès (lecture ou modification) à une donnée sensible, émise par le service qui a servi l'accès
/// et consommée par le service Audit (NF-04, §3.3, PSY-20).
/// </summary>
/// <remarks>
/// Exception documentée à la règle « une rubrique par producteur » : tous les services publient ce contrat unique
/// sur la rubrique dédiée <c>audit</c> (préfixe du nom), afin que le service Audit n'ait qu'un abonnement et que
/// chaque service n'ait besoin que du droit d'émission sur cette rubrique. Le service émetteur est porté par
/// <see cref="Service"/>. L'horodatage de l'accès est <see cref="IntegrationEvent.OccurredAt"/>.
/// Aucune donnée clinique ou psychosociale : identifiants, types, action et motif saisi uniquement (ARC-06).
/// Le motif est un texte libre court (300 caractères au plus) qui ne doit contenir aucune donnée de santé.
/// </remarks>
[EventContract("audit.acces-donnee-sensible", 1)]
public sealed record AccesDonneeSensible(
    string Service,
    string Zone,
    string UtilisateurId,
    string Role,
    string Action,
    string ObjetType,
    Guid ObjetId,
    string? Motif,
    bool BrisDeGlace) : IntegrationEvent;

/// <summary>
/// Alerte : un accès « bris de glace » a été journalisé (§3.3). Destinée à l'avertissement du CPMT dirigeant
/// (zone médicale) ou du CPAP dirigeant (zone psychosociale) ; le motif n'est pas repris, il se consulte dans le journal.
/// </summary>
[EventContract("audit.bris-de-glace-signale", 1)]
public sealed record BrisDeGlaceSignale(
    Guid EntreeAuditId,
    string Zone,
    string Service,
    string UtilisateurId,
    string ObjetType,
    Guid ObjetId) : IntegrationEvent;
