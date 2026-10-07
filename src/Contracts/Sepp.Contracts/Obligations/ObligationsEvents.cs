namespace Sepp.Contracts.Obligations;

/// <summary>
/// POR-04, ARC-33 : une reprise du travail a été enregistrée (ou modifiée, annulée, jugée sans objet) par le processus de
/// reprise du service Obligations. <c>RepriseId</c> identifie le processus (clé d'annulation et de corrélation).
/// <c>Origine</c> : <c>PortailEmployeur</c>, <c>Interne</c> ou <c>Evenement</c> (annonce reçue par
/// <c>bff-employeur.reprise-annoncee</c>). <c>Statut</c> : <c>Enregistree</c>, <c>Modifiee</c>, <c>Annulee</c> ou
/// <c>NonRequise</c> (absence inférieure au minimum légal). Identifiants, dates et codes seulement (ARC-06).
/// </summary>
[EventContract("obligations.reprise-enregistree", 1)]
public sealed record RepriseEnregistree(
    Guid RepriseId,
    Guid PersonneId,
    Guid AffilieId,
    DateOnly DateReprise,
    DateOnly DebutAbsence,
    string Origine,
    string Statut) : IntegrationEvent;

/// <summary>
/// SAN-04 : une obligation est clôturée. <c>Statut</c> : <c>Realise</c>, <c>Annule</c> ou <c>SortiEntreprise</c> ;
/// <c>Motif</c> est un code facultatif (<c>Recalcul</c>, <c>RepriseAnnulee</c>…), jamais un texte libre ; <c>Date</c> est la
/// date de clôture.
/// </summary>
[EventContract("obligations.obligation-cloturee", 1)]
public sealed record ObligationCloturee(
    Guid ObligationId,
    Guid PersonneId,
    Guid AffilieId,
    string TypeExamen,
    string Statut,
    string? Motif,
    DateOnly Date) : IntegrationEvent;

/// <summary>
/// SAN-13, PLA-06 : une obligation à échéance légale doit être replanifiée en urgence (rendez-vous à reprendre). Émis
/// seulement si la replanification automatique est activée ; sinon une alerte est levée. <c>Motif</c> :
/// <c>Absence</c>, <c>AnnulationRendezVous</c> ou <c>ConvocationNonRemise</c> ; <c>RendezVousId</c> est le rendez-vous
/// concerné, s'il existe.
/// </summary>
[EventContract("obligations.planification-urgente-demandee", 1)]
public sealed record PlanificationUrgenteDemandee(
    Guid ObligationId,
    Guid? RendezVousId,
    Guid PersonneId,
    Guid AffilieId,
    string TypeExamen,
    DateOnly DateDue,
    DateOnly DateLimite,
    string Motif) : IntegrationEvent;
