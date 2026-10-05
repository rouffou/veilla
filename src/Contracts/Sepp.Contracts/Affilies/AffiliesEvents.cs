namespace Sepp.Contracts.Affilies;

/// <summary>
/// Opération de fusion, scission ou transfert d'un affilié entre SEPP projetée, réalisée ou annulée (AFF-06).
/// Publié à chaque changement de statut ; la Surveillance médicale s'en sert pour encadrer le transfert
/// des dossiers de santé (SAN-42). <c>SeppContrepartie</c> désigne le SEPP de destination ou d'origine.
/// </summary>
[EventContract("affilies.operation-affilie-modifiee", 1)]
public sealed record OperationAffilieModifiee(
    Guid OperationId,
    Guid AffilieId,
    string TypeOperation,
    string Statut,
    DateOnly DateEffet,
    Guid? AffilieAbsorbantId,
    IReadOnlyList<Guid> AffiliesBeneficiaires,
    string? SeppContrepartie) : IntegrationEvent;
