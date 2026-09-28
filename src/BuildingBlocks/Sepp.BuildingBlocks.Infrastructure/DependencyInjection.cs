using Azure.Identity;
using Azure.Messaging.ServiceBus;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Infrastructure.Auditing;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre le contexte EF Core du service (PostgreSQL, snake_case — DAT-09), l'unité de travail,
    /// l'outbox et son processeur de publication.
    /// </summary>
    public static IServiceCollection AddSeppPersistence<TContext>(this IServiceCollection services, IConfiguration configuration, string connectionStringName)
        where TContext : SeppDbContext
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Chaîne de connexion '{connectionStringName}' absente de la configuration.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<TContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IIntegrationEventOutbox>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<InboxGuard<TContext>>();

        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection("Outbox"));
        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }

    /// <summary>
    /// Bus d'événements : Azure Service Bus si <c>ServiceBus:FullyQualifiedNamespace</c> (identité managée, CTR-16)
    /// ou <c>ConnectionStrings:ServiceBus</c> (émulateur local) est configuré ; sinon publication en mémoire.
    /// </summary>
    public static IServiceCollection AddSeppMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"];
        var connectionString = configuration.GetConnectionString("ServiceBus");

        if (!string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            services.AddSingleton(_ => new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential()));
        }
        else if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton(_ => new ServiceBusClient(connectionString));
        }
        else
        {
            services.TryAddSingleton<IMessagePublisher, InMemoryMessagePublisher>();
            return services;
        }

        services.TryAddSingleton<IMessagePublisher, ServiceBusMessagePublisher>();
        return services;
    }

    /// <summary>
    /// Journal d'audit des accès aux données sensibles (NF-04) : <see cref="IAuditTrail"/> écrit les traces dans l'outbox
    /// du service (à combiner avec <see cref="AddSeppPersistence{TContext}"/>).
    /// </summary>
    /// <param name="service">Nom du service émetteur en kebab-case.</param>
    /// <param name="zone">Zone de sensibilité du service (<see cref="ZonesSensibilite"/>).</param>
    public static IServiceCollection AddSeppAuditTrail(this IServiceCollection services, string service, string zone)
    {
        new AuditTrailOptions { Service = service, Zone = zone }.Valider();
        services.AddOptions<AuditTrailOptions>().Configure(o =>
        {
            o.Service = service;
            o.Zone = zone;
        });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAuditTrail, OutboxAuditTrail>();
        return services;
    }

    /// <summary>Abonne un gestionnaire à un événement d'intégration (un consommateur idempotent par gestionnaire, ARC-31).</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        var subscriptions = services.FirstOrDefault(d => d.ServiceType == typeof(IntegrationEventSubscriptions))?.ImplementationInstance as IntegrationEventSubscriptions;
        if (subscriptions is null)
        {
            subscriptions = new IntegrationEventSubscriptions();
            services.AddSingleton(subscriptions);
        }

        subscriptions.Add(typeof(TEvent), typeof(THandler));
        services.TryAddScoped<THandler>();
        return services;
    }

    /// <summary>
    /// Réception des événements : répartiteur idempotent et, si un bus est configuré, consommateur Service Bus
    /// sur l'abonnement <c>ServiceBus:SubscriptionName</c> du service.
    /// </summary>
    public static IServiceCollection AddSeppConsumer<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : SeppDbContext
    {
        if (!services.Any(d => d.ServiceType == typeof(IntegrationEventSubscriptions)))
        {
            services.AddSingleton(new IntegrationEventSubscriptions());
        }

        services.AddSingleton<IntegrationEventDispatcher<TContext>>();
        services.AddOptions<ServiceBusConsumerOptions>().Bind(configuration.GetSection("ServiceBus"));
        if (!string.IsNullOrWhiteSpace(configuration["ServiceBus:FullyQualifiedNamespace"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("ServiceBus")))
        {
            services.AddHostedService<ServiceBusConsumer<TContext>>();
        }

        return services;
    }
}
