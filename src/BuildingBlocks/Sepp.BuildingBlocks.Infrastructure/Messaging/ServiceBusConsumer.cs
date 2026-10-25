using Azure.Messaging.ServiceBus;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

public sealed class ServiceBusConsumerOptions
{
    /// <summary>Nom de l'abonnement du service sur chaque rubrique (en général le nom du service).</summary>
    public string SubscriptionName { get; set; } = string.Empty;

    public int MaxConcurrentCalls { get; set; } = 4;

    /// <summary>
    /// Rubrique → sujets acceptés (noms versionnés de contrats), copie des <c>subject_filters</c> de <c>infra/variables.tf</c>.
    /// Lu par le seul bus de développement (<see cref="PostgresBusConsumer{TContext}"/>) : sur Azure, Terraform pose le filtre
    /// sur l'abonnement lui-même. Rubrique absente = tous les messages ; les entrées vides sont ignorées.
    /// </summary>
    public Dictionary<string, string[]> SubjectFilters { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Consomme les rubriques auxquelles le service est abonné. Livraison au moins une fois :
/// un message est complété après traitement, abandonné en cas d'erreur (lettre morte après le
/// nombre maximal de livraisons configuré sur l'abonnement).
/// </summary>
public sealed partial class ServiceBusConsumer<TContext>(
    ServiceBusClient client,
    IntegrationEventSubscriptions subscriptions,
    IntegrationEventDispatcher<TContext> dispatcher,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<ServiceBusConsumer<TContext>> logger) : IHostedService, IAsyncDisposable
    where TContext : SeppDbContext
{
    private readonly List<ServiceBusProcessor> _processors = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SubscriptionName))
        {
            throw new InvalidOperationException("ServiceBus:SubscriptionName doit être configuré pour consommer des événements.");
        }

        foreach (var topic in subscriptions.Topics)
        {
            var processor = client.CreateProcessor(topic, options.Value.SubscriptionName, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = options.Value.MaxConcurrentCalls,
            });
            processor.ProcessMessageAsync += OnMessageAsync;
            processor.ProcessErrorAsync += OnErrorAsync;
            _processors.Add(processor);
            await processor.StartProcessingAsync(cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors)
        {
            await processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
        {
            await processor.DisposeAsync();
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        if (!Guid.TryParse(message.MessageId, out var messageId))
        {
            await args.DeadLetterMessageAsync(message, "identifiant-invalide", "MessageId n'est pas un UUID.", args.CancellationToken);
            return;
        }

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = message.CorrelationId ?? string.Empty });
        await dispatcher.DispatchAsync(messageId, message.Subject, message.Body.ToString(), args.CancellationToken);
        await args.CompleteMessageAsync(message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        LogError(logger, args.EntityPath, args.ErrorSource.ToString(), args.Exception);
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Error, "Erreur de consommation sur {EntityPath} ({Source}).")]
    private static partial void LogError(ILogger logger, string entityPath, string source, Exception exception);
}
