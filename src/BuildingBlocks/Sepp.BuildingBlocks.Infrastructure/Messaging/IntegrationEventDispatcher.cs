using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Abonnements d'un service : type de contrat → gestionnaires (un consommateur par gestionnaire).</summary>
public sealed class IntegrationEventSubscriptions
{
    private readonly Dictionary<string, (Type EventType, List<Type> Handlers)> _byContract = new(StringComparer.Ordinal);

    public IEnumerable<string> Topics => _byContract.Values.Select(v => EventContractAttribute.Of(v.EventType).Topic).Distinct(StringComparer.Ordinal);

    public void Add(Type eventType, Type handlerType)
    {
        var contract = EventContractAttribute.Of(eventType).FullName;
        if (!_byContract.TryGetValue(contract, out var entry))
        {
            _byContract[contract] = entry = (eventType, []);
        }

        entry.Handlers.Add(handlerType);
    }

    internal bool TryGet(string contract, out Type eventType, out IReadOnlyList<Type> handlers)
    {
        if (_byContract.TryGetValue(contract, out var entry))
        {
            eventType = entry.EventType;
            handlers = entry.Handlers;
            return true;
        }

        eventType = typeof(object);
        handlers = [];
        return false;
    }
}

/// <summary>
/// Remet un message reçu à ses gestionnaires, chacun au plus une fois grâce à l'inbox (ARC-31).
/// Indépendant du transport : utilisé par le consommateur Service Bus et par les tests.
/// </summary>
public sealed class IntegrationEventDispatcher<TContext>(IServiceScopeFactory scopeFactory, IntegrationEventSubscriptions subscriptions)
    where TContext : SeppDbContext
{
    /// <returns>Nombre de gestionnaires exécutés (0 si le message était déjà traité ou n'est pas attendu).</returns>
    public async Task<int> DispatchAsync(Guid messageId, string contract, string payload, CancellationToken cancellationToken)
    {
        if (!subscriptions.TryGet(contract, out var eventType, out var handlerTypes))
        {
            return 0;
        }

        var integrationEvent = (IntegrationEvent)(JsonSerializer.Deserialize(payload, eventType, EventSerialization.Options)
            ?? throw new InvalidOperationException($"Message {messageId} ({contract}) vide."));

        var executed = 0;
        foreach (var handlerType in handlerTypes)
        {
            // Un périmètre par gestionnaire : transactions et contextes indépendants.
            await using var scope = scopeFactory.CreateAsyncScope();
            var guard = scope.ServiceProvider.GetRequiredService<InboxGuard<TContext>>();
            var handler = scope.ServiceProvider.GetRequiredService(handlerType);
            var handle = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType).GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;

            if (await guard.ExecuteOnceAsync(
                    messageId,
                    handlerType.FullName!,
                    ct => (Task)handle.Invoke(handler, [integrationEvent, ct])!,
                    cancellationToken))
            {
                executed++;
            }
        }

        return executed;
    }
}
