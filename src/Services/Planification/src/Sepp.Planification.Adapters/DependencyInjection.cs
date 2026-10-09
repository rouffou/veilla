using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Communications;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Referentiels;
using Sepp.Planification.Adapters.External;
using Sepp.Planification.Adapters.Persistence;
using Sepp.Planification.Adapters.Securite;
using Sepp.Planification.Adapters.Taches;
using Sepp.Planification.Application;
using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.Projections;

namespace Sepp.Planification.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Planification";

    /// <summary>Valeur de <c>Planification:Adaptateurs:&lt;Systeme&gt;</c> sélectionnant le simulateur (données fictives).</summary>
    public const string Simulateur = "Simulateur";

    /// <summary>Valeur de <c>Planification:Adaptateurs:&lt;Systeme&gt;</c> sélectionnant l'adaptateur réel.</summary>
    public const string Reel = "Reel";

    public static IServiceCollection AddPlanificationAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<PlanificationDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        services.AddScoped<ILieuRepository, LieuRepository>();
        services.AddScoped<IRessourceRepository, RessourceRepository>();
        services.AddScoped<IAbsenceRepository, AbsenceRepository>();
        services.AddScoped<IModeleAgendaRepository, ModeleAgendaRepository>();
        services.AddScoped<IDureeStandardRepository, DureeStandardRepository>();
        services.AddScoped<ICreneauRepository, CreneauRepository>();
        services.AddScoped<IRendezVousRepository, RendezVousRepository>();
        services.AddScoped<IConvocationRepository, ConvocationRepository>();
        services.AddScoped<IPreferenceConvocationRepository, PreferenceConvocationRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IObligationRepository, ObligationRepository>();
        services.AddScoped<IParametreLocalRepository, ParametreLocalRepository>();
        services.AddScoped<ICalendrierLocalRepository, CalendrierLocalRepository>();

        services.AddSingleton(configuration.GetSection("Planification:Parametres").Get<OptionsPlanification>() ?? new OptionsPlanification());
        services.AddScoped<IPerimetreUtilisateur, PerimetreHttp>();
        AddAdaptateursExternes(services, configuration);

        services.Configure<OptionsTaches>(configuration.GetSection("Planification:Taches"));
        services.AddSingleton<TachesPlanifiees>();
        services.AddHostedService(sp => sp.GetRequiredService<TachesPlanifiees>());

        // PLA-04, PLA-06 : obligations dues (service Obligations) et paramètres légaux (service Référentiels), ARC-31.
        services.AddIntegrationEventHandler<ObligationCreee, ObligationCreeeHandler>();
        services.AddIntegrationEventHandler<ObligationEchue, ObligationEchueHandler>();
        services.AddIntegrationEventHandler<ParametreLegalModifie, ParametreLegalModifieHandler>();
        services.AddIntegrationEventHandler<JoursFeriesModifies, JoursFeriesModifiesHandler>();

        // Saga de reprise (ARC-33) : compensation, replanification urgente et retour de l'envoi effectif des convocations.
        services.AddIntegrationEventHandler<ObligationCloturee, ObligationClotureeHandler>();
        services.AddIntegrationEventHandler<PlanificationUrgenteDemandee, PlanificationUrgenteDemandeeHandler>();
        services.AddIntegrationEventHandler<MessageEnvoye, MessageEnvoyeHandler>();
        services.AddIntegrationEventHandler<MessageAbandonne, MessageAbandonneHandler>();
        services.AddSeppConsumer<PlanificationDbContext>(configuration);

        services.AddExceptionHandler<Api.ChevauchementExceptionHandler>();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    /// <summary>ARC-05 : un adaptateur par système externe, simulateur ou réel selon la configuration (aucun défaut implicite).</summary>
    private static void AddAdaptateursExternes(IServiceCollection services, IConfiguration configuration)
    {
        switch (Choix(configuration, "OutilRh"))
        {
            case Simulateur:
                var conges = configuration.GetSection("Planification:Simulateurs:Conges").Get<List<CongeSimule>>() ?? [];
                services.AddSingleton(new SimulateurOutilRh(conges));
                services.AddSingleton<IOutilRh>(sp => sp.GetRequiredService<SimulateurOutilRh>());
                break;
            default:
                services.AddSingleton<IOutilRh, OutilRhAdaptateurReel>();
                break;
        }

        switch (Choix(configuration, "AgendaExterne"))
        {
            case Simulateur:
                services.AddSingleton<SimulateurAgendaExterne>();
                services.AddSingleton<IAgendaExterne>(sp => sp.GetRequiredService<SimulateurAgendaExterne>());
                break;
            default:
                services.AddSingleton<IAgendaExterne, AgendaExterneAdaptateurReel>();
                break;
        }
    }

    private static string Choix(IConfiguration configuration, string systeme)
    {
        var valeur = configuration[$"Planification:Adaptateurs:{systeme}"];
        return valeur switch
        {
            Simulateur or Reel => valeur,
            _ => throw new InvalidOperationException(
                $"Planification:Adaptateurs:{systeme} doit valoir « {Simulateur} » (données fictives) ou « {Reel} » ; valeur actuelle : « {valeur} »."),
        };
    }
}
