using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Flux;

namespace Sepp.Integrations.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationsApplication(this IServiceCollection services)
    {
        services.AddScoped<RegistreCorrespondances>();
        services.AddScoped<TraitementEchanges>();
        services.AddScoped<ExecutionFlux>();
        services.AddScoped<PurgeChargesUtiles>();

        services.AddScoped<IQueryHandler<ListerJournal, PageDto<EchangeFluxDto>>, ListerJournalHandler>();
        services.AddScoped<IQueryHandler<ObtenirEchange, EchangeFluxDto>, ObtenirEchangeHandler>();
        services.AddScoped<IQueryHandler<ObtenirVolumes, VolumesDto>, ObtenirVolumesHandler>();
        services.AddScoped<ICommandHandler<RelancerEchange, EchangeFluxDto>, RelancerEchangeHandler>();
        services.AddScoped<ICommandHandler<LancerFlux, RapportExecutionDto>, LancerFluxHandler>();
        services.AddScoped<ICommandHandler<ConsulterBce, EchangeFluxDto>, ConsulterBceHandler>();

        services.AddScoped<IQueryHandler<ListerCorrespondances, IReadOnlyList<CorrespondanceDto>>, ListerCorrespondancesHandler>();
        services.AddScoped<ICommandHandler<DefinirCorrespondance, CorrespondanceDto>, DefinirCorrespondanceHandler>();
        services.AddScoped<IQueryHandler<ObtenirEntrepriseBce, EntrepriseBceDto>, ObtenirEntrepriseBceHandler>();
        return services;
    }
}
