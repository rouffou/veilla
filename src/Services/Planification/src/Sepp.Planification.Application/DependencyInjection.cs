using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Application.Ressources;
using Sepp.Planification.Application.Synchronisation;
using Sepp.Planification.Application.Urgences;

namespace Sepp.Planification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPlanificationApplication(this IServiceCollection services)
    {
        services.AddScoped<ParametresPlanification>();
        services.AddScoped<PriseDeRendezVous>();
        services.AddScoped<Annulation>();
        services.AddScoped<Indisponibilites>();
        services.AddScoped<ReservationUrgence>();
        services.AddScoped<EmissionRappels>();
        services.AddScoped<SynchronisationAgendas>();

        // Tous les gestionnaires de commandes et de requêtes de l'assembly, enregistrés sous leur interface.
        var gestionnaires = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                .Select(i => (Service: i, Implementation: t)));
        foreach (var (service, implementation) in gestionnaires)
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }
}
