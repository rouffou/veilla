using System.Text.Json;
using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Événement d'intégration en attente de publication, écrit dans la transaction métier (ARC-32).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Nom complet versionné du contrat, par ex. <c>referentiels.parametre-legal-modifie.v1</c>.</summary>
    public required string EventType { get; init; }

    public required string Topic { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public static OutboxMessage From(IntegrationEvent integrationEvent)
    {
        var contract = EventContractAttribute.Of(integrationEvent.GetType());
        return new OutboxMessage
        {
            Id = integrationEvent.EventId,
            EventType = contract.FullName,
            Topic = contract.Topic,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), EventSerialization.Options),
            OccurredAt = integrationEvent.OccurredAt,
            CorrelationId = integrationEvent.CorrelationId,
        };
    }
}

/// <summary>Trace d'un message déjà traité par un consommateur : garantit l'idempotence (ARC-31).</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Consumer { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

public static class EventSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
