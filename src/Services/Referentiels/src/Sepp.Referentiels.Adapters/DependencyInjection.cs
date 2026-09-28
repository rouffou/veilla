using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Referentiels.Adapters.Persistence;
using Sepp.Referentiels.Application;

namespace Sepp.Referentiels.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Referentiels";

    public static IServiceCollection AddReferentielsAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<ReferentielsDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);
        services.AddScoped<IParametreLegalRepository, ParametreLegalRepository>();
        services.AddScoped<INomenclatureRepository, NomenclatureRepository>();
        services.AddScoped<ICalendrierRepository, CalendrierRepository>();
        return services;
    }
}
