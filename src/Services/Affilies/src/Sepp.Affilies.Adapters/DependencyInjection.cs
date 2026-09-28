using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Adapters.Securite;
using Sepp.Affilies.Application;
using Sepp.BuildingBlocks.Infrastructure;

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
        services.AddScoped<IPerimetreUtilisateur, PerimetreJeton>();

        // Énumérations métier échangées par leur nom (« A », « ComitePpt »…) dans l'API.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
