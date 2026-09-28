using Microsoft.Extensions.DependencyInjection;

using Sepp.Audit.Application.Journal;
using Sepp.BuildingBlocks.Application;

namespace Sepp.Audit.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAuditApplication(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<RechercherEntrees, PageDto<EntreeAuditDto>>, RechercherEntreesHandler>();
        services.AddScoped<IQueryHandler<ObtenirEntree, EntreeAuditDto>, ObtenirEntreeHandler>();
        services.AddScoped<IQueryHandler<VerifierIntegrite, IntegriteDto>, VerifierIntegriteHandler>();
        services.AddScoped<ICommandHandler<PurgerJournal, ResultatPurge>, PurgerJournalHandler>();
        return services;
    }
}
