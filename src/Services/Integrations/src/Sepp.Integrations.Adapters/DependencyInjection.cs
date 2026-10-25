using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Contracts.Affilies;
using Sepp.Integrations.Adapters.External;
using Sepp.Integrations.Adapters.External.Personnes;
using Sepp.Integrations.Adapters.Persistence;
using Sepp.Integrations.Adapters.Planification;
using Sepp.Integrations.Application;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Externe;

namespace Sepp.Integrations.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Integrations";

    /// <summary>Valeur de <c>Integrations:Adaptateurs:&lt;Flux&gt;</c> sélectionnant le simulateur (données fictives).</summary>
    public const string Simulateur = "Simulateur";

    /// <summary>Valeur de <c>Integrations:Adaptateurs:&lt;Flux&gt;</c> sélectionnant l'adaptateur réel de l'organisme.</summary>
    public const string Reel = "Reel";

    public static IServiceCollection AddIntegrationsAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<IntegrationsDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // ARC-44, ARC-45, ADR 0012 : chiffrement des charges utiles par enveloppe (section Encryption) ; DEK enveloppées
        // par la clé racine du service dans le coffre de sa zone, déballées au démarrage.
        services.AddSingleton(sp => ChiffrementChamps.Charger(
            sp.GetRequiredService<IConfiguration>().GetSection("Encryption"), sp.GetRequiredService<IHostEnvironment>(), sp.GetService<IOperationsCleRacine>()).Cles);
        services.AddSingleton<FieldEncryptor>();

        services.AddScoped<IJournalFluxRepository, JournalFluxRepository>();
        services.AddScoped<IPositionFluxRepository, PositionFluxRepository>();
        services.AddScoped<ICorrespondanceRepository, CorrespondanceRepository>();
        services.AddScoped<IEntrepriseBceRepository, EntrepriseBceRepository>();
        services.AddSingleton(configuration.GetSection("Integrations:Conservation").Get<ConservationChargesUtiles>() ?? new ConservationChargesUtiles());

        AddAdaptateursExternes(services, configuration);
        AddClientPersonnes(services, configuration);

        services.Configure<OptionsPlanification>(configuration.GetSection("Integrations:Planification"));
        services.AddSingleton<PlanificateurFlux>();
        services.AddHostedService(sp => sp.GetRequiredService<PlanificateurFlux>());

        // §15.3 : correspondances numéro BCE ↔ affilié apprises des événements du service Affiliés (ARC-31).
        services.AddIntegrationEventHandler<AffilieCree, AffilieCreeHandler>();
        services.AddIntegrationEventHandler<AffilieModifie, AffilieModifieHandler>();
        services.AddSeppConsumer<IntegrationsDbContext>(configuration);

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    /// <summary>ARC-05 : un adaptateur par flux, simulateur ou réel selon la configuration (aucun défaut implicite).</summary>
    private static void AddAdaptateursExternes(IServiceCollection services, IConfiguration configuration)
    {
        var bcss = configuration.GetSection("Integrations:Bcss").Get<ConfigurationBcss>() ?? new ConfigurationBcss();
        services.AddSingleton(bcss);

        switch (Choix(configuration, "Bce"))
        {
            case Simulateur:
                services.AddScoped<IRegistreBce, SimulateurBce>();
                break;
            default:
                services.AddScoped<IRegistreBce, BceAdaptateurReel>();
                break;
        }

        switch (Choix(configuration, "Dimona"))
        {
            case Simulateur:
                services.AddScoped<IFluxDimona, SimulateurDimona>();
                break;
            default:
                services.AddScoped<IFluxDimona, DimonaAdaptateurReel>();
                break;
        }

        switch (Choix(configuration, "RegistreNational"))
        {
            case Simulateur:
                services.AddScoped<IRegistreNational, SimulateurRegistreNational>();
                break;
            default:
                services.AddScoped<IRegistreNational, RegistreNationalAdaptateurReel>();
                break;
        }

        // SAN-60 : notifications d'incapacité reçues par eHealth (Lot 3).
        switch (Choix(configuration, "Incapacites"))
        {
            case Simulateur:
                services.AddScoped<IFluxIncapacites, SimulateurIncapacites>();
                break;
            default:
                services.AddScoped<IFluxIncapacites, IncapacitesEHealthAdaptateurReel>();
                break;
        }
    }

    private static string Choix(IConfiguration configuration, string flux)
    {
        var valeur = configuration[$"Integrations:Adaptateurs:{flux}"];

        // Les trois adaptateurs réels sont des squelettes (INT-04) : refusés hors production.
        GardeAdaptateurs.RefuserReelNonImplementeHorsProduction(configuration, $"Integrations:Adaptateurs:{flux}", valeur, reelImplemente: false);
        return valeur switch
        {
            Simulateur or Reel => valeur,
            _ => throw new InvalidOperationException(
                $"Integrations:Adaptateurs:{flux} doit valoir « {Simulateur} » (données fictives) ou « {Reel} » (organisme) ; valeur actuelle : « {valeur} »."),
        };
    }

    /// <summary>Client HTTP typé du service Personnes, authentifié par le compte technique (client credentials OIDC).</summary>
    private static void AddClientPersonnes(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Integrations:Personnes").Get<OptionsPersonnes>() ?? new OptionsPersonnes();
        services.AddSingleton(options);
        services.AddSingleton<FournisseurJetonClient>();
        services.AddTransient<JetonClientHandler>();
        services.AddHttpClient(ClientsHttp.JetonOidc);
        services.AddHttpClient<IPersonnesClient, PersonnesHttpClient>(ClientsHttp.Personnes, client =>
            {
                if (options.BaseAddress is { } adresse)
                {
                    client.BaseAddress = adresse;
                }
            })
            .AddHttpMessageHandler<JetonClientHandler>();
    }
}
