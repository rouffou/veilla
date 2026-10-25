using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Adapters.Externe;
using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Adapters.Rattrapage;
using Sepp.Affilies.Adapters.Securite;
using Sepp.Affilies.Application;
using Sepp.Affilies.Application.Bce;
using Sepp.Affilies.Application.Occupations;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Personnes;

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
        services.AddScoped<IOccupationRepository, OccupationRepository>();
        services.AddScoped<IPerimetreUtilisateur, PerimetreJeton>();
        services.AddScoped<IContextePersonne, ContextePersonneJeton>();

        // ADR 0011 : rattrapage des projections des consommateurs (état courant des affiliés), déclenché par l'exploitation.
        services.AddSeppRepublication<AffiliesDbContext>(configuration);
        services.AddRepublicationSource<RepublicationAffilies>();

        // AFF-01, AFF-02, INT-04 : données d'entreprise lues chez Intégrations avec le compte technique du service (rôle affilies).
        ClientsInternes.Ajouter(
            services,
            configuration.GetSection("Affilies:ServicesInternes").Get<OptionsServicesInternes>() ?? new OptionsServicesInternes(),
            configuration.GetSection("Affilies:CompteTechnique").Get<OptionsCompteTechnique>() ?? new OptionsCompteTechnique());

        // Mise à jour de l'affilié depuis la BCE (ARC-31 : consommateur idempotent, abonnement affilies ← integrations).
        services.AddIntegrationEventHandler<DonneesBceRecues, DonneesBceRecuesHandler>();
        // POR-11 (#340) : occupations projetées depuis Personnes pour ne répondre qu'aux employeurs de la personne du jeton
        // (abonnement affilies <- personnes, filtré sur ces deux sujets dans infra/variables.tf).
        services.AddIntegrationEventHandler<OccupationDebutee, OccupationDebuteeHandler>();
        services.AddIntegrationEventHandler<OccupationTerminee, OccupationTermineeHandler>();
        // ADR 0011 : rattrapage de cette projection par les instantanés d'occupation republiés par Personnes.
        services.AddIntegrationEventHandler<OccupationInstantanee, OccupationInstantaneeHandler>();
        services.AddSeppConsumer<AffiliesDbContext>(configuration);

        // Énumérations métier échangées par leur nom (« A », « ComitePpt »…) dans l'API.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
