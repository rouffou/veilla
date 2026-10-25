using Azure.Identity;
using Azure.Messaging.ServiceBus;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Republication;
using Sepp.BuildingBlocks.Infrastructure.Auditing;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre le contexte EF Core du service (PostgreSQL, snake_case — DAT-09), l'unité de travail,
    /// l'outbox et son processeur de publication. La connexion vient de <c>ConnectionStrings:&lt;nom&gt;</c> (développement
    /// local, tests) ; à défaut, en Azure, de <c>POSTGRES__HOST</c>, <c>POSTGRES__DATABASE</c> et <c>POSTGRES__USER</c>
    /// avec un jeton Entra de l'identité managée comme mot de passe (<see cref="PostgresIdentiteManagee"/>, CTR-16).
    /// </summary>
    public static IServiceCollection AddSeppPersistence<TContext>(this IServiceCollection services, IConfiguration configuration, string connectionStringName)
        where TContext : SeppDbContext
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);

        services.TryAddSingleton(TimeProvider.System);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<TContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
                .UseSnakeCaseNamingConvention());
        }
        else if (PostgresIdentiteManagee.EstConfiguree(configuration))
        {
            // Valide la configuration dès l'enregistrement : le démarrage échoue avec un message clair.
            _ = PostgresIdentiteManagee.ConstruireConnexion(configuration);
            services.TryAddSingleton(_ => PostgresIdentiteManagee.ConstruireSourceDeDonnees(configuration));
            services.AddDbContext<TContext>((sp, options) => options
                .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.EnableRetryOnFailure())
                .UseSnakeCaseNamingConvention());
        }
        else
        {
            throw new InvalidOperationException(
                $"Connexion PostgreSQL absente : renseigner ConnectionStrings:{connectionStringName} (local) ou POSTGRES__HOST, POSTGRES__DATABASE et POSTGRES__USER (Azure, identité managée).");
        }

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IIntegrationEventOutbox>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<InboxGuard<TContext>>();

        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection("Outbox"));
        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }

    /// <summary>
    /// Sonde de disponibilité PostgreSQL du service : chaîne de connexion locale ou, en Azure, source de données à
    /// jeton Entra partagée avec le contexte EF Core (<see cref="AddSeppPersistence{TContext}"/>).
    /// </summary>
    public static IHealthChecksBuilder AddSeppPostgresHealthCheck(this IHealthChecksBuilder builder, IConfiguration configuration, string connectionStringName, string name, IEnumerable<string>? tags = null)
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);
        return !string.IsNullOrWhiteSpace(connectionString)
            ? builder.AddNpgSql(connectionString, name: name, tags: tags)
            : builder.AddNpgSql(sp => sp.GetRequiredService<NpgsqlDataSource>(), name: name, tags: tags);
    }

    /// <summary>
    /// Bus d'événements : Azure Service Bus si <c>ServiceBus:FullyQualifiedNamespace</c> (identité managée, CTR-16)
    /// ou <c>ConnectionStrings:ServiceBus</c> (émulateur local) est configuré ; bus de développement PostgreSQL si
    /// <c>ConnectionStrings:BusDeveloppement</c> l'est (Development, Test et Testing uniquement, ADR 0004) ; sinon
    /// publication en mémoire.
    /// </summary>
    public static IServiceCollection AddSeppMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        if (AjouterBusDeveloppement(services, configuration))
        {
            services.TryAddSingleton<IMessagePublisher, PostgresMessagePublisher>();
            return services;
        }

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
    /// Rattrapage des projections côté producteur (ADR 0011) : demandes de republication (<see cref="IRepublications"/>) et
    /// écriture des instantanés par lots dans l'outbox (<see cref="RepublicationProcessor{TContext}"/>, section
    /// <c>Republication</c>). Le contexte doit appeler <see cref="RepublicationModel.AjouterRepublications"/> et chaque type
    /// d'état est ajouté par <see cref="AddRepublicationSource{TSource}"/>. À combiner avec <see cref="AddSeppPersistence{TContext}"/>.
    /// </summary>
    public static IServiceCollection AddSeppRepublication<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : SeppDbContext
    {
        services.AddOptions<RepublicationOptions>().Bind(configuration.GetSection("Republication"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRepublications, Republications<TContext>>();
        services.AddHostedService<RepublicationProcessor<TContext>>();
        return services;
    }

    /// <summary>Type d'état republiable par le service (une source par type, ADR 0011).</summary>
    public static IServiceCollection AddRepublicationSource<TSource>(this IServiceCollection services)
        where TSource : class, ISourceRepublication
    {
        services.AddScoped<ISourceRepublication, TSource>();
        return services;
    }

    /// <summary>
    /// Réception des événements : répartiteur idempotent et, si un bus est configuré, consommateur Service Bus (ou du bus
    /// de développement PostgreSQL) sur l'abonnement <c>ServiceBus:SubscriptionName</c> du service.
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
        if (AjouterBusDeveloppement(services, configuration))
        {
            services.AddHostedService<PostgresBusConsumer<TContext>>();
        }
        else if (!string.IsNullOrWhiteSpace(configuration["ServiceBus:FullyQualifiedNamespace"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("ServiceBus")))
        {
            services.AddHostedService<ServiceBusConsumer<TContext>>();
        }

        return services;
    }

    /// <summary>
    /// Bus de développement PostgreSQL (ADR 0004, amendement) : enregistré si <c>ConnectionStrings:BusDeveloppement</c> est
    /// renseignée. Refusé hors Development, Test et Testing, dès l'enregistrement si l'environnement est connu de la
    /// configuration, et toujours à la construction du bus (<see cref="GardeBusDeveloppement"/>, démarrage en échec).
    /// </summary>
    private static bool AjouterBusDeveloppement(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(BusDeveloppement.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(configuration["ServiceBus:FullyQualifiedNamespace"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("ServiceBus")))
        {
            throw new InvalidOperationException(
                $"Deux bus configurés : ConnectionStrings:{BusDeveloppement.ConnectionStringName} (bus de développement) et Azure Service Bus. N'en garder qu'un.");
        }

        if (configuration[HostDefaults.EnvironmentKey] is { Length: > 0 } environnement)
        {
            BusDeveloppement.VerifierEnvironnement(environnement);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<BusDeveloppementOptions>().Bind(configuration.GetSection("BusDeveloppement"));
        services.TryAddSingleton(sp => new BusDeveloppement(
            connectionString,
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<ILogger<BusDeveloppement>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GardeBusDeveloppement>());
        return true;
    }
}
