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
/// ou papier) relève du service Communications, informé par l'événement <c>planification.convocation-emise</c>.
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

    public void EnregistrerEnvoi(string messageId, DateTimeOffset dateEnvoi)
    {
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > 100)
        {
            throw new DomainException("Identifiant de message invalide.");
        }

        MessageId = messageId.Trim();
        DateEnvoi = dateEnvoi;
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
