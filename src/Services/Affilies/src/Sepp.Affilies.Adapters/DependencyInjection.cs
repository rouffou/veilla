using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Adapters.Externe;
using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Adapters.Securite;
using Sepp.Affilies.Application;
using Sepp.Affilies.Application.Bce;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Integrations;

namespace Sepp.Affilies.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Affilies";

    public static IServiceCollection AddAffiliesAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<AffiliesDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);
        services.AddScoped<IAffilieRepository, AffilieRepository>();
        services.AddScoped<IGroupeRepository, GroupeRepository>();
        services.AddScoped<IHistoriqueAffilieRepository, HistoriqueAffilieRepository>();
        services.AddScoped<IEcartSynchronisationRepository, EcartSynchronisationRepository>();
        services.AddScoped<IPerimetreUtilisateur, PerimetreJeton>();

        // AFF-01, AFF-02, INT-04 : données d'entreprise lues chez Intégrations avec le compte technique du service (rôle affilies).
        ClientsInternes.Ajouter(
            services,
            configuration.GetSection("Affilies:ServicesInternes").Get<OptionsServicesInternes>() ?? new OptionsServicesInternes(),
            configuration.GetSection("Affilies:CompteTechnique").Get<OptionsCompteTechnique>() ?? new OptionsCompteTechnique());

        // Mise à jour de l'affilié depuis la BCE (ARC-31 : consommateur idempotent, abonnement affilies ← integrations).
        services.AddIntegrationEventHandler<DonneesBceRecues, DonneesBceRecuesHandler>();
        services.AddSeppConsumer<AffiliesDbContext>(configuration);

        // Énumérations métier échangées par leur nom (« A », « ComitePpt »…) dans l'API.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
