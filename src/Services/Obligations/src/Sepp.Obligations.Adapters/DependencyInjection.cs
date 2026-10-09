using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.BffEmployeur;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Planification;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.Reintegration;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Adapters.Persistence;
using Sepp.Obligations.Adapters.Securite;
using Sepp.Obligations.Adapters.Traitement;
using Sepp.Obligations.Application;
using Sepp.Obligations.Application.Projections;
using Sepp.Obligations.Application.Reprises;
using Sepp.Obligations.Domain.Calcul;

namespace Sepp.Obligations.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Obligations";

    public static IServiceCollection AddObligationsAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<ObligationsDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // ARC-31 : modèles de lecture alimentés par événements (idempotence par l'inbox), SAN-01, SAN-04.
        services.AddIntegrationEventHandler<AffectationModifiee, AffectationModifieeHandler>();
        services.AddIntegrationEventHandler<ProfilRisquePosteModifie, ProfilRisquePosteModifieHandler>();
        services.AddIntegrationEventHandler<RegleSurveillanceModifiee, RegleSurveillanceModifieeHandler>();
        services.AddIntegrationEventHandler<SurchargeFrequenceDefinie, SurchargeFrequenceDefinieHandler>();
        services.AddIntegrationEventHandler<OccupationDebutee, OccupationDebuteeHandler>();
        services.AddIntegrationEventHandler<OccupationTerminee, OccupationTermineeHandler>();
        services.AddIntegrationEventHandler<EtatParticulierDeclare, EtatParticulierDeclareHandler>();
        services.AddIntegrationEventHandler<ExamenCloture, ExamenClotureHandler>();
        services.AddIntegrationEventHandler<ParametreLegalModifie, ParametreLegalModifieHandler>();
        services.AddIntegrationEventHandler<JoursFeriesModifies, JoursFeriesModifiesHandler>();
        services.AddIntegrationEventHandler<RendezVousPlanifie, RendezVousPlanifieHandler>();
        services.AddIntegrationEventHandler<RendezVousAnnule, RendezVousAnnuleHandler>();
        services.AddIntegrationEventHandler<RepriseAnnoncee, RepriseAnnonceeHandler>();
        services.AddIntegrationEventHandler<IncapaciteNotifiee, IncapaciteNotifieeHandler>();
        services.AddIntegrationEventHandler<TrajetDemarre, TrajetDemarreHandler>();
        services.AddIntegrationEventHandler<TrajetTermine, TrajetTermineHandler>();
        services.AddIntegrationEventHandler<ListeNominativeGeneree, ListeNominativeGenereeHandler>();

        // ARC-33 : jalons du processus de reprise.
        services.AddIntegrationEventHandler<ConvocationEnvoyee, ConvocationEnvoyeeHandler>();
        services.AddIntegrationEventHandler<ConvocationNonRemise, ConvocationNonRemiseHandler>();
        services.AddIntegrationEventHandler<UrgenceNonCouverte, UrgenceNonCouverteHandler>();
        services.AddIntegrationEventHandler<AbsenceRendezVousConstatee, AbsenceRendezVousConstateeHandler>();
        services.AddIntegrationEventHandler<RendezVousReplanifie, RendezVousReplanifieHandler>();
        services.AddIntegrationEventHandler<DecisionEmise, DecisionEmiseHandler>();
        services.AddIntegrationEventHandler<DocumentPublie, DocumentPublieHandler>();
        services.AddSeppConsumer<ObligationsDbContext>(configuration);

        services.AddScoped<IObligationRepository, ObligationRepository>();
        services.AddScoped<IDemandeRepository, DemandeRepository>();
        services.AddScoped<IProjectionRepository, ProjectionRepository>();
        services.AddScoped<IProcessusRepriseRepository, ProcessusRepriseRepository>();
        services.AddScoped<IDecisionRecueRepository, DecisionRecueRepository>();
        services.AddScoped<IPerimetreAffilies, HttpPerimetreAffilies>();

        // Réglages de lecture et d'organisation (section « Obligations ») : pas des délais légaux, qui viennent des politiques légales (ARC-21).
        var options = new OptionsObligations();
        configuration.GetSection("Obligations").Bind(options);
        services.AddSingleton(options);
        var calcul = new OptionsCalcul();
        configuration.GetSection("Obligations:Calcul").Bind(calcul);
        services.AddSingleton(calcul);

        // Processus de reprise (ARC-33) : seuils « à valider » et replanification automatique (désactivée par défaut).
        var reprise = new OptionsReprise();
        configuration.GetSection("Obligations:Reprise").Bind(reprise);
        services.AddSingleton(reprise);
        services.Configure<OptionsMinuteriesReprise>(configuration.GetSection("Obligations:Reprise"));
        services.AddSingleton<MinuteriesRepriseService>();
        services.AddHostedService(sp => sp.GetRequiredService<MinuteriesRepriseService>());

        services.Configure<OptionsTraitementPeriodique>(configuration.GetSection("Obligations:Traitement"));
        services.AddHostedService<TraitementPeriodiqueService>();

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
