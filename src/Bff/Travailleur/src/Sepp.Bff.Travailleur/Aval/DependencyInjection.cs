using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Sepp.Bff.Travailleur.Aval;

/// <summary>Adresses des services aval (section <c>ServicesAval</c>) : noms de conteneurs en compose, FQDN internes en Container Apps.</summary>
public sealed class ServicesAvalOptions
{
    public const string Section = "ServicesAval";

    public Uri? Planification { get; set; }

    public Uri? SurveillanceMedicale { get; set; }

    public Uri? Obligations { get; set; }

    public Uri? Documents { get; set; }
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
                o.Planification ??= Adresse(configuration, "Sepp:Services:Planification");
                o.SurveillanceMedicale ??= Adresse(configuration, "Sepp:Services:Surveillance_Medicale");
                o.Obligations ??= Adresse(configuration, "Sepp:Services:Obligations");
                o.Documents ??= Adresse(configuration, "Sepp:Services:Documents");
            })
            .Validate(o => o.Planification is not null && o.SurveillanceMedicale is not null && o.Obligations is not null && o.Documents is not null,
                $"Les adresses {ServicesAvalOptions.Section}:Planification, :SurveillanceMedicale, :Obligations et :Documents sont obligatoires.")
            .ValidateOnStart();

        services.AddTransient<PropagationJetonHandler>();

        services.AddHttpClient<IPlanificationApi, PlanificationApi>((sp, c) => c.BaseAddress = Base(sp, o => o.Planification))
            .AddHttpMessageHandler<PropagationJetonHandler>();
        services.AddHttpClient<ISurveillanceMedicaleApi, SurveillanceMedicaleApi>((sp, c) => c.BaseAddress = Base(sp, o => o.SurveillanceMedicale))
            .AddHttpMessageHandler<PropagationJetonHandler>();
        services.AddHttpClient<IObligationsApi, ObligationsApi>((sp, c) => c.BaseAddress = Base(sp, o => o.Obligations))
            .AddHttpMessageHandler<PropagationJetonHandler>();
        services.AddHttpClient<IDocumentsApi, DocumentsApi>((sp, c) => c.BaseAddress = Base(sp, o => o.Documents))
            .AddHttpMessageHandler<PropagationJetonHandler>();

        // Une réservation, une annulation, un questionnaire ou une demande (POST) n'est pas idempotent : jamais rejoué
        // automatiquement, seules les lectures le sont.
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
