using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.PostesRisques.Adapters.Persistence;
using Sepp.PostesRisques.Adapters.Securite;
using Sepp.PostesRisques.Application;
using Sepp.PostesRisques.Application.Projections;

namespace Sepp.PostesRisques.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "PostesRisques";

    public static IServiceCollection AddPostesRisquesAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<PostesRisquesDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // ARC-31 : modèles de lecture alimentés par événements (idempotence par l'inbox).
        services.AddIntegrationEventHandler<AffectationModifiee, AffectationModifieeHandler>();
        services.AddIntegrationEventHandler<ExamenCloture, ExamenClotureHandler>();
        services.AddIntegrationEventHandler<ParametreLegalModifie, ParametreLegalModifieHandler>();
        services.AddSeppConsumer<PostesRisquesDbContext>(configuration);

        services.AddScoped<IPosteRepository, PosteRepository>();
        services.AddScoped<IPropositionPosteRisqueRepository, PropositionPosteRisqueRepository>();
        services.AddScoped<IRisqueRepository, RisqueRepository>();
        services.AddScoped<ISurchargeFrequenceRepository, SurchargeFrequenceRepository>();
        services.AddScoped<IListeNominativeRepository, ListeNominativeRepository>();
        services.AddScoped<IPropositionListeRepository, PropositionListeRepository>();
        services.AddScoped<IProjectionRepository, ProjectionRepository>();
        services.AddScoped<IPerimetreAffilies, HttpPerimetreAffilies>();

        // AFF-31 : durée de conservation par défaut tant que le paramètre légal n'a pas été reçu (5 ans).
        services.AddSingleton(new ConservationListesOptions
        {
            AnneesParDefaut = configuration.GetValue("ListesNominatives:ConservationAnnees", 5),
        });

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
