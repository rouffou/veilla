using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sepp.BuildingBlocks.Application;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Application.Consultation;
using Sepp.Obligations.Application.Gestion;
using Sepp.Obligations.Domain.Calcul;

namespace Sepp.Obligations.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddObligationsApplication(this IServiceCollection services)
    {
        // Réglages par défaut ; l'adaptateur les remplace par la configuration (section « Obligations »).
        services.TryAddSingleton(new OptionsObligations());
        services.TryAddSingleton(new OptionsCalcul());
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<RecalculObligations>();
        services.AddScoped<Projections.MiseAJourProjection>();
        services.AddScoped<TraitementEcheances>();

        services.AddScoped<IQueryHandler<ListerObligationsPersonne, IReadOnlyList<ObligationDto>>, ListerObligationsPersonneHandler>();
        services.AddScoped<IQueryHandler<ListerObligationsAffilie, IReadOnlyList<ObligationDto>>, ListerObligationsAffilieHandler>();
        services.AddScoped<IQueryHandler<SynthetiserObligationsAffilie, SyntheseObligationsDto>, SynthetiserObligationsAffilieHandler>();
        services.AddScoped<IQueryHandler<ObtenirObligation, ObligationDto>, ObtenirObligationHandler>();
        services.AddScoped<IQueryHandler<ObtenirTraceCalcul, TraceObligationDto>, ObtenirTraceCalculHandler>();
        services.AddScoped<IQueryHandler<ListerRegroupables, IReadOnlyList<PropositionRendezVousDto>>, ListerRegroupablesHandler>();
        services.AddScoped<IQueryHandler<ListerAlertes, IReadOnlyList<AlerteDto>>, ListerAlertesHandler>();

        services.AddScoped<ICommandHandler<ChangerStatutObligation, Unit>, ChangerStatutObligationHandler>();
        services.AddScoped<ICommandHandler<EnregistrerDemandeTravailleur, Guid>, EnregistrerDemandeTravailleurHandler>();
        services.AddScoped<ICommandHandler<DemanderRecalcul, int>, DemanderRecalculHandler>();
        services.AddScoped<ICommandHandler<TraiterEcheances, ResultatTraitement>, TraiterEcheancesHandler>();
        return services;
    }
}
