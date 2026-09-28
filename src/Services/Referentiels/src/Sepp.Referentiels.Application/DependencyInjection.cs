using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Referentiels.Application.Calendrier;
using Sepp.Referentiels.Application.Nomenclatures;
using Sepp.Referentiels.Application.Parametres;

namespace Sepp.Referentiels.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReferentielsApplication(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ListerParametres, IReadOnlyList<ParametreDto>>, ListerParametresHandler>();
        services.AddScoped<IQueryHandler<ObtenirParametre, ParametreDto>, ObtenirParametreHandler>();
        services.AddScoped<ICommandHandler<DefinirValeurParametre, Unit>, DefinirValeurParametreHandler>();

        services.AddScoped<IQueryHandler<ListerJoursFeries, IReadOnlyList<JourFerieDto>>, ListerJoursFeriesHandler>();
        services.AddScoped<IQueryHandler<CalculerEcheance, EcheanceDto>, CalculerEcheanceHandler>();
        services.AddScoped<ICommandHandler<AjouterJourFerie, Unit>, AjouterJourFerieHandler>();

        services.AddScoped<IQueryHandler<ListerNomenclatures, IReadOnlyList<NomenclatureDto>>, ListerNomenclaturesHandler>();
        services.AddScoped<IQueryHandler<ObtenirNomenclature, NomenclatureDto>, ObtenirNomenclatureHandler>();
        services.AddScoped<ICommandHandler<CreerNomenclature, Guid>, CreerNomenclatureHandler>();
        services.AddScoped<ICommandHandler<AjouterEntree, Unit>, AjouterEntreeHandler>();

        services.AddScoped<InitialiserParametresLegaux>();
        return services;
    }
}
