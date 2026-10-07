using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Communications.Application.Evenements;
using Sepp.Communications.Application.Expedition;
using Sepp.Communications.Application.Messages;

namespace Sepp.Communications.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCommunicationsApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateurMessages>();
        services.AddScoped<ExpediteurMessages>();

        services.AddScoped<IQueryHandler<RechercherMessages, PageMessagesDto>, RechercherMessagesHandler>();
        services.AddScoped<IQueryHandler<ObtenirMessage, MessageDetailDto>, ObtenirMessageHandler>();
        services.AddScoped<ICommandHandler<EnvoyerMessage, Guid>, EnvoyerMessageHandler>();
        services.AddScoped<ICommandHandler<RelancerMessage, Unit>, RelancerMessageHandler>();
        services.AddScoped<ICommandHandler<ExpedierMessagesEchus, ExpeditionDto>, ExpedierMessagesEchusHandler>();
        return services;
    }
}
