using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Domain.Agenda;

namespace Sepp.Planification.Domain.Convocations;

public enum TypeConvocation
{
    Convocation,

    /// <summary>SAN-13 : nouvelle convocation après une absence.</summary>
    Reconvocation,

    /// <summary>PLA-07 : le rendez-vous a été déplacé (notification de la personne convoquée).</summary>
    Replanification,
}

/// <summary>
/// Convocation (§15.3 : id, rendez_vous_id, canal, recommande, date_envoi, message_id). Le service Planification
/// décide qui est convoqué, quand et par quel canal ; l'envoi effectif (gabarit, coordonnées, recommandé électronique
/// ou papier) relève du service Communications, informé par l'événement <c>planification.convocation-emise</c> ; son retour
/// (<c>message-envoye</c>, <c>message-abandonne</c>) est enregistré ici (<see cref="EnregistrerEnvoi"/>,
/// <see cref="MarquerNonRemise"/>). Un message de Communications porte le <c>ConvocationId</c> comme référence d'origine.
/// </summary>
public sealed class Convocation : AggregateRoot
{
    private Convocation()
    {
    }

    private Convocation(Guid id) : base(id)
    {
    }

    public Guid RendezVousId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public CanalConvocation Canal { get; private set; }

    /// <summary>SAN-11 : envoi recommandé (électronique ou papier) exigé par la loi.</summary>
    public bool Recommande { get; private set; }

    public TypeConvocation Type { get; private set; }

    /// <summary>Lot de convocations émises ensemble (SAN-10), sinon <c>null</c> (convocation à l'unité).</summary>
    public Guid? LotId { get; private set; }

    public DateTimeOffset DateEmission { get; private set; }

    /// <summary>Date d'envoi effective communiquée par le service Communications.</summary>
    public DateTimeOffset? DateEnvoi { get; private set; }

    /// <summary>L'envoi a été abandonné par le service Communications : la personne n'a pas reçu la convocation.</summary>
    public DateTimeOffset? DateNonRemise { get; private set; }

    /// <summary>Identifiant du message chez le service Communications.</summary>
    public string? MessageId { get; private set; }

    public static Convocation Emettre(RendezVous rendezVous, CanalConvocation canal, bool recommande, TypeConvocation type, Guid? lotId, DateTimeOffset maintenant)
    {
        if (recommande && !PolitiqueCanal.PermetRecommande(canal))
        {
            throw new DomainException($"Un envoi recommandé ne peut pas passer par le canal {canal}.");
        }

        return new Convocation(NewId())
        {
            RendezVousId = rendezVous.Id,
            PersonneId = rendezVous.PersonneId,
            AffilieId = rendezVous.AffilieId,
            Canal = canal,
            Recommande = recommande,
            Type = type,
            LotId = lotId,
            DateEmission = maintenant,
        };
    }

    /// <summary>
    /// Le service Communications a envoyé la convocation (<c>communications.message-envoye</c>). Idempotent : une convocation
    /// recommandée donne deux messages et un événement peut être rejoué ; seul le premier envoi est retenu. Un envoi réussi
    /// lève la marque « non remise » d'un abandon antérieur (relance manuelle du message).
    /// </summary>
    /// <returns><c>true</c> si l'envoi vient d'être enregistré, <c>false</c> s'il l'était déjà (rien à publier).</returns>
    public bool EnregistrerEnvoi(string messageId, DateTimeOffset dateEnvoi)
    {
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > 100)
        {
            throw new DomainException("Identifiant de message invalide.");
        }

        if (DateEnvoi is not null)
        {
            return false;
        }

        MessageId = messageId.Trim();
        DateEnvoi = dateEnvoi;
        DateNonRemise = null;
        return true;
    }

    /// <summary>
    /// Le service Communications a abandonné l'envoi (<c>communications.message-abandonne</c>) : la personne n'est pas
    /// informée. Idempotent ; sans effet si la convocation est déjà partie par un autre message.
    /// </summary>
    /// <returns><c>true</c> si la convocation vient d'être marquée non remise, <c>false</c> sinon (rien à publier).</returns>
    public bool MarquerNonRemise(DateTimeOffset date)
    {
        if (DateEnvoi is not null || DateNonRemise is not null)
        {
            return false;
        }

        DateNonRemise = date;
        return true;
    }
}

/// <summary>SAN-10 : canal de convocation préféré d'un affilié pour ses travailleurs.</summary>
public sealed class PreferenceConvocation : AggregateRoot
{
    private PreferenceConvocation()
    {
    }

    private PreferenceConvocation(Guid id) : base(id)
    {
    }

    public Guid AffilieId { get; private set; }

    public CanalConvocation Canal { get; private set; }

    public static PreferenceConvocation Creer(Guid affilieId, CanalConvocation canal) => new(NewId()) { AffilieId = affilieId, Canal = canal };

    public void Modifier(CanalConvocation canal) => Canal = canal;
}

/// <summary>
/// SAN-10, SAN-11 : choix du canal. Ordre : canal demandé explicitement, sinon préférence de l'affilié, sinon canal par
/// défaut. Un envoi recommandé n'existe que par courrier (recommandé papier) ou par e-mail (recommandé électronique) :
/// si le canal retenu est le SMS ou le portail, la convocation recommandée part par courrier.
/// </summary>
public static class PolitiqueCanal
{
    public static bool PermetRecommande(CanalConvocation canal) => canal is CanalConvocation.Courrier or CanalConvocation.Email;

    public static CanalConvocation Resoudre(CanalConvocation? demande, CanalConvocation? preferenceAffilie, CanalConvocation parDefaut, bool recommande)
    {
        var canal = demande ?? preferenceAffilie ?? parDefaut;
        return recommande && !PermetRecommande(canal) ? CanalConvocation.Courrier : canal;
    }
}
