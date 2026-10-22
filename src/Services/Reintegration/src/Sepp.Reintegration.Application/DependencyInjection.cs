using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Reintegration.Application.Echanges;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Application.Minuteries;
using Sepp.Reintegration.Application.Trajets;

namespace Sepp.Reintegration.Application;

public static class DependencyInjection
{
    /// <summary>Cas d'usage (tous les <c>ICommandHandler</c> et <c>IQueryHandler</c> de l'assemblage) et services applicatifs.</summary>
    public static IServiceCollection AddReintegrationApplication(this IServiceCollection services, OptionsReintegration? options = null)
    {
        var interfaces = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var service in type.GetInterfaces().Where(i => i.IsGenericType && interfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(service, type);
            }
        }

        services.AddSingleton(options ?? new OptionsReintegration());
        services.AddScoped<GardeReintegration>();
        services.AddScoped<AccesSuivi>();
        services.AddScoped<ParametresReintegration>();
        services.AddScoped<MinuteriesDossier>();
        services.AddScoped<ModificationDossier>();
        services.AddScoped<EtapeTrajetMedicale>();
        services.AddScoped<CompensationTrajet>();
        services.AddScoped<InformationMedecinConseil>();
        services.AddScoped<EnvoiEchangesEHealth>();
        services.AddScoped<ReceptionEchangesEHealth>();
        services.AddScoped<TraiterMinuteries>();
        return services;
    }
}
