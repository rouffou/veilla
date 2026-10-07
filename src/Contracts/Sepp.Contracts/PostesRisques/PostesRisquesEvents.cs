namespace Sepp.Contracts.PostesRisques;

/// <summary>
/// Nouvelle version d'une règle de surveillance portée par un risque (AFF-12) : consommée par Obligations pour
/// recalculer les échéances (SAN-01). Les actes et vaccins détaillés restent consultables par l'API.
/// </summary>
[EventContract("postes-risques.regle-surveillance-modifiee", 1)]
public sealed record RegleSurveillanceModifiee(
    Guid RisqueId,
    string CodeRisque,
    string Categorie,
    int Version,
    string TypeSurveillance,
    int? FrequenceMois,
    bool SurveillanceProlongee,
    DateOnly ValideDu) : IntegrationEvent;

/// <summary>
/// Surcharge de fréquence définie ou clôturée par le CPMT pour un poste, un groupe ou un travailleur (AFF-13).
/// Cible par identifiant uniquement, sans motif (ARC-06).
/// </summary>
[EventContract("postes-risques.surcharge-frequence-definie", 1)]
public sealed record SurchargeFrequenceDefinie(
    Guid SurchargeId,
    Guid AffilieId,
    string CibleType,
    Guid CibleId,
    string CodeRisque,
    int FrequenceMois,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu) : IntegrationEvent;

/// <summary>
/// Nouvelle version d'une liste nominative générée ou revue (AFF-30, AFF-31) : consommée par Obligations pour l'alerte
/// « liste non revue depuis 12 mois » (AFF-32). Identifiants, type de liste, version et dates uniquement (ARC-06).
/// </summary>
[EventContract("postes-risques.liste-nominative-generee", 1)]
public sealed record ListeNominativeGeneree(
    Guid ListeNominativeId,
    Guid AffilieId,
    string TypeListe,
    int Version,
    DateOnly DateReference,
    DateOnly DateGeneration) : IntegrationEvent;
