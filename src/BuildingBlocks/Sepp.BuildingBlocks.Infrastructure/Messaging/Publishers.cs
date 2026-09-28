using System.Collections.Concurrent;

using Azure.Messaging.ServiceBus;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Publie un message de l'outbox sur le bus. Livraison au moins une fois : les consommateurs sont idempotents.</summary>
public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>Publication sur Azure Service Bus : une rubrique par service producteur (ADR 0004).</summary>
public sealed class ServiceBusMessagePublisher(ServiceBusClient client) : IMessagePublisher, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var sender = _senders.GetOrAdd(message.Topic, client.CreateSender);
        var busMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
        {
            MessageId = message.Id.ToString(),
            Subject = message.EventType,
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
        };
        busMessage.ApplicationProperties["event-type"] = message.EventType;
        busMessage.ApplicationProperties["occurred-at"] = message.OccurredAt.ToString("O");
        await sender.SendMessageAsync(busMessage, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }
}

/// <summary>Publication en mémoire, pour les tests et le poste de développement sans bus.</summary>
public sealed class InMemoryMessagePublisher : IMessagePublisher
{
    private readonly ConcurrentQueue<OutboxMessage> _published = new();

    public IReadOnlyCollection<OutboxMessage> Published => [.. _published];

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }
}
