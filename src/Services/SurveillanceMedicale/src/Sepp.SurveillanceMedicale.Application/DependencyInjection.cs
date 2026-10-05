using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Decisions;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Application.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Application.Protocoles;
using Sepp.SurveillanceMedicale.Application.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application;

public static class DependencyInjection
{
    /// <summary>Cas d'usage (tous les <c>ICommandHandler</c> et <c>IQueryHandler</c> de l'assemblage) et services applicatifs.</summary>
    public static IServiceCollection AddSurveillanceMedicaleApplication(this IServiceCollection services, OptionsSurveillanceMedicale? options = null)
    {
        var interfaces = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var service in type.GetInterfaces().Where(i => i.IsGenericType && interfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(service, type);
            }
        }

        services.AddSingleton(options ?? new OptionsSurveillanceMedicale());
        services.AddScoped<GardeDossier>();
        services.AddScoped<ParametresMedicaux>();
        services.AddScoped<AccesExamens>();
        services.AddScoped<AccesDecisions>();
        services.AddScoped<AccesDeclarationsMp>();
        services.AddScoped<RisquesPersonne>();
        services.AddScoped<ExportDossiers>();
        services.AddScoped<InitialiserProtocoles>();
        return services;
    }
}
