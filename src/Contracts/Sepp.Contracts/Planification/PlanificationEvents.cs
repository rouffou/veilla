namespace Sepp.Contracts.Planification;

/// <summary>
/// SAN-10, SAN-11 : une convocation est à envoyer. Le service Communications l'envoie par le canal indiqué
/// (<c>Courrier</c>, <c>Email</c>, <c>Sms</c>, <c>Portail</c>), en recommandé si <c>Recommande</c>, après lecture des
/// coordonnées de la personne auprès du service Personnes. Aucun contenu de message ni donnée de santé (ARC-06) :
/// le type d'acte est une catégorie, comme dans <c>obligations.obligation-creee</c>. <c>TypeConvocation</c> :
/// <c>Convocation</c>, <c>Reconvocation</c> (après une absence, SAN-13) ou <c>Replanification</c> (rendez-vous déplacé,
/// PLA-07). <c>LotId</c> regroupe les convocations émises par lot.
/// </summary>
[EventContract("planification.convocation-emise", 1)]
public sealed record ConvocationEmise(
    Guid ConvocationId,
    Guid RendezVousId,
    Guid PersonneId,
    Guid AffilieId,
    Guid LieuId,
    string TypeActe,
    DateTimeOffset Debut,
    string Canal,
    bool Recommande,
    string TypeConvocation,
    Guid? LotId) : IntegrationEvent;

/// <summary>SAN-13 : rappel automatique d'un rendez-vous (1 = J-7, 2 = J-1 par défaut, paramètres CONVOCATION.RAPPEL_1/2).</summary>
[EventContract("planification.rappel-rendez-vous-du", 1)]
public sealed record RappelRendezVousDu(
    Guid RendezVousId,
    Guid PersonneId,
    Guid AffilieId,
    Guid LieuId,
    DateTimeOffset Debut,
    string Canal,
    int NumeroRappel) : IntegrationEvent;

/// <summary>
/// PLA-07 : rendez-vous déplacé (replanification en masse après l'absence d'un conseiller). Même identifiant de
/// rendez-vous ; <c>Motif</c> est un code (<c>AbsenceRessource</c>). Une convocation de type <c>Replanification</c>
/// est émise en parallèle pour notifier la personne.
/// </summary>
[EventContract("planification.rendez-vous-replanifie", 1)]
public sealed record RendezVousReplanifie(
    Guid RendezVousId,
    Guid PersonneId,
    Guid AffilieId,
    DateTimeOffset AncienDebut,
    DateTimeOffset NouveauDebut,
    string Motif) : IntegrationEvent;

/// <summary>SAN-13 : la personne ne s'est pas présentée ; les obligations couvertes redeviennent à planifier.</summary>
[EventContract("planification.absence-rendez-vous-constatee", 1)]
public sealed record AbsenceRendezVousConstatee(
    Guid RendezVousId,
    Guid PersonneId,
    Guid AffilieId,
    DateTimeOffset Debut,
    IReadOnlyList<Guid> ObligationIds) : IntegrationEvent;

/// <summary>PLA-06 : aucun créneau disponible avant l'échéance légale d'une urgence (alerte au planificateur).</summary>
[EventContract("planification.urgence-non-couverte", 1)]
public sealed record UrgenceNonCouverte(
    Guid ObligationId,
    Guid PersonneId,
    Guid AffilieId,
    string TypeExamen,
    DateOnly DateLimite) : IntegrationEvent;
