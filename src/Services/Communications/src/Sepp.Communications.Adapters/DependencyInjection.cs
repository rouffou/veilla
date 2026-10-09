using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Communications.Adapters.Annuaire;
using Sepp.Communications.Adapters.Envois;
using Sepp.Communications.Adapters.Expedition;
using Sepp.Communications.Adapters.Persistence;
using Sepp.Communications.Application;
using Sepp.Communications.Application.Evenements;
using Sepp.Contracts.Audit;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Planification;

namespace Sepp.Communications.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Communications";

    /// <summary>Valeur de <c>Communications:Adaptateurs:&lt;Canal&gt;</c> sélectionnant le simulateur (aucun envoi réel).</summary>
    public const string Simulateur = "Simulateur";

    /// <summary>Valeur de <c>Communications:Adaptateurs:&lt;Canal&gt;</c> sélectionnant l'adaptateur réel.</summary>
    public const string Reel = "Reel";

    public static IServiceCollection AddCommunicationsAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<CommunicationsDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);
        services.AddScoped<IMessageRepository, MessageRepository>();

        services.AddSingleton(configuration.GetSection("Communications:Liens").Get<OptionsLiens>() ?? new OptionsLiens());
        services.Configure<OptionsExpedition>(configuration.GetSection("Communications:Expedition"));
        services.AddHostedService<ExpeditionHostedService>();

        AddCanaux(services, configuration);
        AddAnnuaire(services, configuration);

        // DOC-03 à DOC-05, SAN-10, SAN-11, SAN-13 : messages déclenchés par les événements des autres services (ARC-31).
        services.AddIntegrationEventHandler<DocumentPublie, DocumentPublieHandler>();
        services.AddIntegrationEventHandler<RendezVousAnnule, RendezVousAnnuleHandler>();
        services.AddIntegrationEventHandler<ConvocationEmise, ConvocationEmiseHandler>();
        services.AddIntegrationEventHandler<RappelRendezVousDu, RappelRendezVousDuHandler>();
        services.AddIntegrationEventHandler<BrisDeGlaceSignale, BrisDeGlaceSignaleHandler>();
        services.AddSeppConsumer<CommunicationsDbContext>(configuration);

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    /// <summary>Un port par canal (DOC-03) : simulateur ou adaptateur réel, sans défaut implicite.</summary>
    private static void AddCanaux(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<BoiteEnvoiSimulee>();
        services.Configure<OptionsSmtp>(configuration.GetSection("Communications:Smtp"));

        Canal<IPortailNotifications, PortailNotificationsSimule, PortailNotifications>(services, configuration, "Portail");
        Canal<IEnvoiEmail, EnvoiEmailSimule, EnvoiEmailSmtp>(services, configuration, "Email");
        Canal<IEnvoiSms, EnvoiSmsSimule, EnvoiSmsNonRaccorde>(services, configuration, "Sms");
        Canal<IEnvoiCourrier, EnvoiCourrierSimule, EnvoiCourrierNonRaccorde>(services, configuration, "Courrier");
        Canal<IEnvoiRecommandeElectronique, EnvoiRecommandeElectroniqueSimule, EnvoiRecommandeElectroniqueNonRaccorde>(services, configuration, "RecommandeElectronique");
        Canal<IEBoxEntreprise, EBoxEntrepriseSimulee, EBoxEntrepriseNonRaccordee>(services, configuration, "EBoxEntreprise");
        Canal<IEBoxCitoyen, EBoxCitoyenSimulee, EBoxCitoyenNonRaccordee>(services, configuration, "EBoxCitoyen");
    }

    private static void Canal<TPort, TSimulateur, TReel>(IServiceCollection services, IConfiguration configuration, string nom)
        where TPort : class, ICanalEnvoi
        where TSimulateur : class, TPort
        where TReel : class, TPort
    {
        var choix = configuration[$"Communications:Adaptateurs:{nom}"];
        switch (choix)
        {
            case Simulateur:
                services.AddSingleton<TPort, TSimulateur>();
                break;
            case Reel:
                services.AddSingleton<TPort, TReel>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Communications:Adaptateurs:{nom} doit valoir « {Simulateur} » (aucun envoi réel) ou « {Reel} » ; valeur actuelle : « {choix} ».");
        }

        services.AddSingleton<ICanalEnvoi>(sp => sp.GetRequiredService<TPort>());
    }

    private static void AddAnnuaire(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Communications:Annuaire").Get<OptionsAnnuaire>() ?? new OptionsAnnuaire();
        services.AddSingleton(options);
        var choix = configuration["Communications:Adaptateurs:Annuaire"];
        switch (choix)
        {
            case Simulateur:
                services.AddSingleton<AnnuaireSimule>();
                services.AddSingleton<IAnnuaireDestinataires>(sp => sp.GetRequiredService<AnnuaireSimule>());
                break;
            case Reel:
                AjouterClientsInternes(
                    services,
                    configuration.GetSection("Communications:ServicesInternes").Get<OptionsServicesInternes>() ?? new OptionsServicesInternes(),
                    configuration.GetSection("Communications:CompteTechnique").Get<OptionsCompteTechnique>() ?? new OptionsCompteTechnique());
                services.AddScoped<IAnnuaireDestinataires, AnnuaireHttp>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Communications:Adaptateurs:Annuaire doit valoir « {Simulateur} » ou « {Reel} » ; valeur actuelle : « {choix} ».");
        }
    }

    private static void AjouterClientsInternes(IServiceCollection services, OptionsServicesInternes adresses, OptionsCompteTechnique compte)
    {
        services.AddSingleton(compte);
        services.AddSingleton<FournisseurJetonTechnique>();
        services.AddTransient<JetonTechniqueHandler>();
        services.AddHttpClient(ClientsHttp.JetonOidc);
        services.AddHttpClient(ClientsHttp.Affilies, c => c.BaseAddress = adresses.Affilies).AddHttpMessageHandler<JetonTechniqueHandler>();
        services.AddHttpClient(ClientsHttp.Personnes, c => c.BaseAddress = adresses.Personnes).AddHttpMessageHandler<JetonTechniqueHandler>();
    }
}
