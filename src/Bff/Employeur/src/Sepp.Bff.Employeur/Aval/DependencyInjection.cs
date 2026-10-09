using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Sepp.Bff.Employeur.Aval;

/// <summary>Adresses des services aval (section <c>ServicesAval</c>) : noms de conteneurs en compose, FQDN internes en Container Apps.</summary>
public sealed class ServicesAvalOptions
{
    public const string Section = "ServicesAval";

    public Uri? Affilies { get; set; }

    public Uri? Personnes { get; set; }

    public Uri? PostesRisques { get; set; }

    public Uri? Obligations { get; set; }
}

public static class DependencyInjection
{
    /// <summary>
    /// Clients HTTP typés vers les services aval (ARC-40) : jeton et corrélation propagés par
    /// <see cref="PropagationJetonHandler"/> ; délai, reprises et disjoncteur hérités du socle (ARC-30).
    /// </summary>
    public static IServiceCollection AddServicesAval(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServicesAvalOptions>()
            .Bind(configuration.GetSection(ServicesAvalOptions.Section))
            .PostConfigure(o =>
            {
                // En Azure, Terraform injecte SEPP__SERVICES__<NOM> (infra/main.tf, service_urls) : repli sur ces clés.
                o.Affilies ??= Adresse(configuration, "Sepp:Services:Affilies");
                o.Personnes ??= Adresse(configuration, "Sepp:Services:Personnes");
                o.PostesRisques ??= Adresse(configuration, "Sepp:Services:Postes_Risques");
                o.Obligations ??= Adresse(configuration, "Sepp:Services:Obligations");
            })
            .Validate(o => o.Affilies is not null && o.Personnes is not null && o.PostesRisques is not null && o.Obligations is not null,
                $"Les adresses {ServicesAvalOptions.Section}:Affilies, :Personnes, :PostesRisques et :Obligations sont obligatoires.")
            .ValidateOnStart();

        services.AddTransient<PropagationJetonHandler>();

        services.AddHttpClient<IAffiliesApi, AffiliesApi>((sp, c) => c.BaseAddress = Base(sp, o => o.Affilies))
            .AddHttpMessageHandler<PropagationJetonHandler>();
        services.AddHttpClient<IPersonnesApi, PersonnesApi>((sp, c) => c.BaseAddress = Base(sp, o => o.Personnes))
            .AddHttpMessageHandler<PropagationJetonHandler>();
        services.AddHttpClient<IPostesRisquesApi, PostesRisquesApi>((sp, c) => c.BaseAddress = Base(sp, o => o.PostesRisques))
            .AddHttpMessageHandler<PropagationJetonHandler>();

        services.AddHttpClient<IObligationsApi, ObligationsApi>((sp, c) => c.BaseAddress = Base(sp, o => o.Obligations))
            .AddHttpMessageHandler<PropagationJetonHandler>();

        // Une annonce de reprise ou une proposition (POST) n'est pas idempotente : jamais rejouée automatiquement, seules les lectures le sont.
        services.PostConfigureAll<HttpStandardResilienceOptions>(o => o.Retry.DisableForUnsafeHttpMethods());
        return services;
    }

    private static Uri? Adresse(IConfiguration configuration, string cle) =>
        Uri.TryCreate(configuration[cle], UriKind.Absolute, out var uri) ? uri : null;

    private static Uri Base(IServiceProvider sp, Func<ServicesAvalOptions, Uri?> adresse)
    {
        var uri = adresse(sp.GetRequiredService<IOptions<ServicesAvalOptions>>().Value)!;
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
