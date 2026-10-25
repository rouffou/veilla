using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Audit.Adapters.Persistence;
using Sepp.Audit.Application;
using Sepp.Audit.Application.Journal;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Audit;
using Sepp.Contracts.Referentiels;

namespace Sepp.Audit.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Audit";

    public static IServiceCollection AddAuditAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<AuditDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);
        services.AddScoped<IJournalAuditRepository, JournalAuditRepository>();

        // ARC-31 : consommateur idempotent des traces publiées sur la rubrique « audit ».
        services.AddIntegrationEventHandler<AccesDonneeSensible, EnregistrerAccesHandler>();

        // ARC-21 : durée de conservation du journal, reçue de Référentiels (projection locale).
        services.AddScoped<IParametreLegalRepository, ParametreLegalRepository>();
        services.AddIntegrationEventHandler<ParametreLegalModifie, ParametreLegalModifieHandler>();
        services.AddSeppConsumer<AuditDbContext>(configuration);

        // NF-04 : purge périodique ; la durée (au moins 10 ans) est le paramètre AUDIT.CONSERVATION de Référentiels (ARC-21).
        services.AddOptions<ConservationOptions>()
            .Bind(configuration.GetSection("Audit:Conservation"))
            .Validate(o => o.IntervallePurge > TimeSpan.Zero, "Audit:Conservation:IntervallePurge doit être positif.")
            .ValidateOnStart();
        services.AddHostedService<PurgeJournalService>();
        return services;
    }
}
