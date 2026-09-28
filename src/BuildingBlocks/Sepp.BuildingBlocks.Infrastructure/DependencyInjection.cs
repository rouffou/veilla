using Azure.Identity;
using Azure.Messaging.ServiceBus;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

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
}
