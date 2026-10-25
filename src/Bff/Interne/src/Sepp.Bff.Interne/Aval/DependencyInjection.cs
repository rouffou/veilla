using Microsoft.Extensions.Http.Resilience;

namespace Sepp.Bff.Interne.Aval;

public static class DependencyInjection
{
    /// <summary>
    /// Clients HTTP typés des écrans composites (ARC-40) : jeton, corrélation et langue propagés par
    /// <see cref="PropagationJetonHandler"/> ; délai, reprises des seules lectures et disjoncteur hérités du socle (ARC-30).
    /// Les adresses sont celles de la liste fermée du relais (<see cref="Relais.ServicesRelayes"/>).
    /// </summary>
    public static IServiceCollection AddServicesAval(this IServiceCollection services, IReadOnlyDictionary<string, Uri> adresses)
    {
        ArgumentNullException.ThrowIfNull(adresses);
        services.AddTransient<PropagationJetonHandler>();

        services.AddHttpClient<IPersonnesApi, PersonnesApi>(c => c.BaseAddress = adresses["personnes"])
            .AddHttpMessageHandler<PropagationJetonHandler>();
        services.AddHttpClient<IAffiliesApi, AffiliesApi>(c => c.BaseAddress = adresses["affilies"])
            .AddHttpMessageHandler<PropagationJetonHandler>();

        // Une écriture n'est jamais rejouée automatiquement, seules les lectures le sont.
        services.PostConfigureAll<HttpStandardResilienceOptions>(o => o.Retry.DisableForUnsafeHttpMethods());
        return services;
    }
}
