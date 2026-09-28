using System.Reflection;

namespace Sepp.Contracts;

/// <summary>
/// Événement d'intégration publié sur le bus. Ne transporte que des identifiants, dates, statuts
/// et catégories : aucune donnée clinique ni psychosociale (ARC-06).
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Identifiant de corrélation de bout en bout (ARC-47).</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Nom logique et version d'un contrat d'événement (ARC-34). Le nom est de la forme
/// <c>&lt;service&gt;.&lt;evenement&gt;</c> ; la version change à toute rupture de compatibilité.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventContractAttribute(string name, int version) : Attribute
{
    public string Name { get; } = name;

    public int Version { get; } = version;

    /// <summary>Nom de la rubrique Service Bus (un topic par service producteur).</summary>
    public string Topic => Name[..Name.IndexOf('.', StringComparison.Ordinal)];

    public string FullName => $"{Name}.v{Version}";

    public static EventContractAttribute Of(Type eventType) =>
        eventType.GetCustomAttribute<EventContractAttribute>()
        ?? throw new InvalidOperationException($"L'événement {eventType.Name} n'a pas d'attribut [EventContract].");
}
