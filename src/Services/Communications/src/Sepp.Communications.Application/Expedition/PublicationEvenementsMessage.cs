using Sepp.BuildingBlocks.Application;
using Sepp.Communications.Domain.Messages;
using Sepp.Contracts.Communications;

namespace Sepp.Communications.Application.Expedition;

/// <summary>
/// Traduit les événements de domaine d'un message (envoi réussi, abandon) en événements d'intégration
/// <c>communications.message-envoye</c> et <c>communications.message-abandonne</c> (ARC-32, ARC-33). L'écriture dans
/// l'outbox a lieu dans le même <see cref="IUnitOfWork.SaveChangesAsync"/> que la preuve d'envoi : un événement n'est
/// jamais publié sans preuve ni perdu après elle. Les événements de domaine sont consommés pour ne partir qu'une fois.
/// </summary>
internal static class PublicationEvenementsMessage
{
    public static void Publier(Message message, IIntegrationEventOutbox outbox)
    {
        foreach (var evenement in message.DomainEvents.ToList())
        {
            switch (evenement)
            {
                case MessageEnvoyeDomaine e:
                    outbox.Add(new MessageEnvoye(e.MessageId, e.ObjetType, e.ObjetId, e.ReferenceOrigineId, e.Type.ToString(), e.Canal.ToString(),
                        e.Recommande, e.OccurredAt));
                    break;
                case MessageAbandonneDomaine e:
                    outbox.Add(new MessageAbandonne(e.MessageId, e.ObjetType, e.ObjetId, e.ReferenceOrigineId, e.Type.ToString(), e.Canal.ToString(),
                        e.Recommande, e.CodeErreur, e.OccurredAt));
                    break;
            }
        }

        message.ClearDomainEvents();
    }
}
