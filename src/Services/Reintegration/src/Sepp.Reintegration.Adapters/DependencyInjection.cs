using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Referentiels;
using Sepp.Reintegration.Adapters.External;
using Sepp.Reintegration.Adapters.Persistence;
using Sepp.Reintegration.Adapters.Securite;
using Sepp.Reintegration.Adapters.Traitement;
using Sepp.Reintegration.Application;
using Sepp.Reintegration.Application.Projections;

namespace Sepp.Reintegration.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Reintegration";

    /// <summary>Nom du service émetteur des traces d'audit (zone médicale).</summary>
    public const string NomService = "reintegration";

    /// <summary>Valeur de <c>Reintegration:Adaptateurs:EHealth</c> sélectionnant le simulateur (données fictives).</summary>
    public const string Simulateur = "Simulateur";

    /// <summary>Valeur de <c>Reintegration:Adaptateurs:EHealth</c> sélectionnant l'adaptateur réel (squelette, INT-04).</summary>
    public const string Reel = "Reel";

    public static IServiceCollection AddReintegrationAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<ReintegrationDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // NF-04 : chaque accès au dossier de réintégration est tracé dans la partition médicale du journal d'audit.
        services.AddSeppAuditTrail(NomService, ZonesSensibilite.Medicale);

        // ARC-04, ARC-45 : clés propres à la zone médicale (section ZoneMedicale:Encryption), jamais partagées avec les autres
        // zones ; en Azure, secrets Key Vault protégés par HSM injectés par référence.
        services.AddSingleton(sp => new ChiffrementZoneMedicale(new FieldEncryptor(
            new ConfigurationFieldKeyProvider(sp.GetRequiredService<IConfiguration>().GetSection(ChiffrementZoneMedicale.Section)))));

        services.AddScoped<IIncapaciteRepository, IncapaciteRepository>();
        services.AddScoped<ITrajetRepository, TrajetRepository>();
        services.AddScoped<IEchangeEHealthRepository, EchangeEHealthRepository>();
        services.AddScoped<IProjectionRepository, ProjectionRepository>();
        services.AddScoped<IContexteAcces, ContexteAccesHttp>();

        // SAN-64 : canal eHealth, simulateur ou réel (squelette) ; aucune valeur implicite.
        var canal = configuration["Reintegration:Adaptateurs:EHealth"];
        switch (canal)
        {
            case Simulateur:
                services.AddSingleton<ICanalEHealth, CanalEHealthSimule>();
                break;
            case Reel:
                services.AddSingleton<ICanalEHealth, CanalEHealthReel>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Reintegration:Adaptateurs:EHealth doit valoir « {Simulateur} » (données fictives) ou « {Reel} » ; valeur actuelle : « {canal} ».");
        }

        services.Configure<OptionsTraitement>(configuration.GetSection("Reintegration:Traitement"));
        services.AddSingleton<TraitementReintegrationService>();
        services.AddHostedService(sp => sp.GetRequiredService<TraitementReintegrationService>());

        // ARC-31 : consommateurs idempotents (inbox + écriture par clé).
        services.AddIntegrationEventHandler<IncapaciteNotifiee, IncapaciteNotifieeHandler>();
        services.AddIntegrationEventHandler<RepriseEnregistree, RepriseEnregistreeHandler>();
        services.AddIntegrationEventHandler<ObligationCreee, ObligationCreeeHandler>();
        services.AddIntegrationEventHandler<AbsenceRendezVousConstatee, AbsenceRendezVousConstateeHandler>();
        services.AddIntegrationEventHandler<ParametreLegalModifie, ParametreLegalModifieHandler>();
        services.AddIntegrationEventHandler<JoursFeriesModifies, JoursFeriesModifiesHandler>();
        services.AddSeppConsumer<ReintegrationDbContext>(configuration);

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
