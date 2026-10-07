namespace Sepp.Contracts.Communications;

/// <summary>
/// DOC-03, DOC-04, SAN-10 : un message a été envoyé (accepté par le canal). <c>ObjetType</c>/<c>ObjetId</c> désignent
/// l'objet métier à l'origine du message ; <c>ReferenceOrigineId</c> reprend l'identifiant de l'événement d'origine
/// (par exemple le <c>ConvocationId</c>). <c>TypeMessage</c> est un code (<c>ConvocationRendezVous</c>…), <c>Canal</c>
/// vaut <c>Courrier</c>, <c>Email</c>, <c>Sms</c> ou <c>Portail</c>. Aucun contenu de message (ARC-06).
/// </summary>
[EventContract("communications.message-envoye", 1)]
public sealed record MessageEnvoye(
    Guid MessageId,
    string ObjetType,
    Guid ObjetId,
    Guid? ReferenceOrigineId,
    string TypeMessage,
    string Canal,
    bool Recommande,
    DateTimeOffset EnvoyeLe) : IntegrationEvent;

/// <summary>
/// DOC-05 : l'envoi d'un message est abandonné après épuisement des tentatives. Mêmes champs que
/// <c>communications.message-envoye</c> ; <c>CodeErreur</c> est un code technique (jamais le texte de l'erreur).
/// </summary>
[EventContract("communications.message-abandonne", 1)]
public sealed record MessageAbandonne(
    Guid MessageId,
    string ObjetType,
    Guid ObjetId,
    Guid? ReferenceOrigineId,
    string TypeMessage,
    string Canal,
    bool Recommande,
    string CodeErreur,
    DateTimeOffset Date) : IntegrationEvent;
