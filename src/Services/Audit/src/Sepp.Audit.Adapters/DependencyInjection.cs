using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Audit.Adapters.Persistence;
using Sepp.Audit.Application;
using Sepp.Audit.Application.Journal;
using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Audit;

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
        services.AddSeppConsumer<AuditDbContext>(configuration);

        // NF-04 : conservation d'au moins 10 ans, vérifiée au démarrage.
        services.AddOptions<ConservationOptions>()
            .Bind(configuration.GetSection("Audit:Conservation"))
            .Validate(o => o.Annees >= PolitiqueConservation.MinimumAnnees,
                $"Audit:Conservation:Annees doit valoir au moins {PolitiqueConservation.MinimumAnnees} (NF-04).")
            .Validate(o => o.IntervallePurge > TimeSpan.Zero, "Audit:Conservation:IntervallePurge doit être positif.")
            .ValidateOnStart();
        services.AddHostedService<PurgeJournalService>();
        return services;
    }
}
