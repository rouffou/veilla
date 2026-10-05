namespace Sepp.Contracts.Integrations;

/// <summary>
/// Données d'une entreprise reçues de la Banque-Carrefour des Entreprises (BCE, §12) et différentes de la dernière
/// réception : dénomination, forme juridique, code NACE-BEL et numéros des unités d'établissement (AFF-01, AFF-02).
/// Données d'entreprise, pas de données personnelles. <c>AffilieId</c> est renseigné quand le numéro BCE correspond
/// à un affilié connu (correspondance d'identifiants du service Intégrations). Les adresses des unités d'établissement
/// ne voyagent pas dans l'événement (ARC-06) : elles se lisent par l'API du service Intégrations
/// (<c>GET /api/v1/bce/entreprises/{numeroBce}</c>). Numéros sous leur forme canonique à dix chiffres.
/// </summary>
[EventContract("integrations.donnees-bce-recues", 1)]
public sealed record DonneesBceRecues(
    string NumeroBce,
    Guid? AffilieId,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    IReadOnlyList<string> NumerosUnitesEtablissement,
    DateOnly DateExtraction) : IntegrationEvent;
