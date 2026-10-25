
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

using Sepp.Bff.Interne.Securite;
using Sepp.BuildingBlocks.Web;

using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Sepp.Bff.Interne.Relais;

/// <summary>
/// Relais de l'application interne vers les services (ADR 0013) : <c>/{service}/api/v1/{reste}</c> →
/// <c>{adresse du service}/api/v1/{reste}</c>, pour la seule liste fermée <see cref="ServicesRelayes"/>. Aucune autre forme de
/// chemin n'a de route (404) : ni sondes, ni OpenAPI, ni autre version d'API. Les en-têtes sont transmis sur liste blanche
/// (<see cref="EnTetesTransmis"/>) : rien d'autre ne part vers le service, en particulier ni <c>Cookie</c>, ni <c>Host</c>
/// d'origine, ni <c>X-Forwarded-*</c>, et le BFF n'ajoute aucun en-tête d'identité (le service ne connaît l'utilisateur que
/// par son jeton, qu'il revalide). Pas de reprise automatique : une écriture n'est jamais rejouée.
/// </summary>
public static class RelaisServices
{
    /// <summary>Délai d'inactivité d'un appel relayé (aucun octet reçu ni envoyé) : au-delà, 504 (ARC-30).</summary>
    public static readonly TimeSpan DelaiActivite = TimeSpan.FromSeconds(30);

    /// <summary>Délai d'établissement de la connexion vers un service.</summary>
    public static readonly TimeSpan DelaiConnexion = TimeSpan.FromSeconds(5);

    public const string EnTeteMotifAcces = "X-Motif-Acces";
    public const string EnTeteBrisDeGlace = "X-Bris-De-Glace";

    /// <summary>
    /// En-têtes de requête recopiés vers le service. <c>X-Correlation-Id</c> n'en fait pas partie : le BFF transmet sa propre
    /// valeur, normalisée par <see cref="CorrelationIdMiddleware"/>, jamais la chaîne reçue.
    /// </summary>
    public static readonly string[] EnTetesTransmis =
        [HeaderNames.Authorization, HeaderNames.Accept, HeaderNames.AcceptLanguage, EnTeteMotifAcces, EnTeteBrisDeGlace];

    private const string Reste = "reste";

    public static IServiceCollection AddRelaisServices(this IServiceCollection services, IReadOnlyDictionary<string, Uri> adresses)
    {
        ArgumentNullException.ThrowIfNull(adresses);
        services.AddSingleton<IForwarderHttpClientFactory, ClientRelaisFactory>();
        services.AddReverseProxy()
            .LoadFromMemory(Routes(), Clusters(adresses))
            .AddTransforms(Transformer);
        return services;
    }

    public static IEndpointConventionBuilder MapRelaisServices(this IEndpointRouteBuilder app) =>
        app.MapReverseProxy(pipeline =>
        {
            pipeline.Use(RefuserCheminDetourneAsync);
            pipeline.Use(TraduireEchecAsync);
        });

    internal static IReadOnlyList<RouteConfig> Routes() =>
    [
        .. ServicesRelayes.Tous.Select(s => new RouteConfig
        {
            RouteId = s.Segment,
            ClusterId = s.Segment,
            AuthorizationPolicy = PerimetreInterne.Politique,
            Match = new RouteMatch { Path = $"/{s.Segment}/api/v1/{{**{Reste}}}" },
        }.WithTransformPathRouteValues($"/api/v1/{{**{Reste}}}")),
    ];

    internal static IReadOnlyList<ClusterConfig> Clusters(IReadOnlyDictionary<string, Uri> adresses) =>
    [
        .. ServicesRelayes.Tous.Select(s => new ClusterConfig
        {
            ClusterId = s.Segment,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
            {
                [s.Segment] = new() { Address = adresses[s.Segment].AbsoluteUri },
            },
            HttpRequest = new ForwarderRequestConfig { ActivityTimeout = DelaiActivite },
        }),
    ];

    private static void Transformer(TransformBuilderContext contexte)
    {
        // Liste blanche : aucun en-tête de la requête n'est recopié par défaut, pas de X-Forwarded-* ni de Host d'origine.
        contexte.CopyRequestHeaders = false;
        contexte.UseDefaultForwarders = false;
        contexte.AddOriginalHost(false);
        contexte.AddRequestTransform(RecopierEnTetes);

        // Un service n'a pas à poser de cookie dans le navigateur de l'utilisateur via le BFF.
        contexte.AddResponseHeaderRemove(HeaderNames.SetCookie);
    }

    private static ValueTask RecopierEnTetes(RequestTransformContext contexte)
    {
        var entrants = contexte.HttpContext.Request.Headers;
        var sortants = contexte.ProxyRequest.Headers;
        foreach (var nom in EnTetesTransmis)
        {
            if (entrants.TryGetValue(nom, out var valeurs) && !StringValues.IsNullOrEmpty(valeurs))
            {
                sortants.TryAddWithoutValidation(nom, (IEnumerable<string?>)valeurs);
            }
        }

        // Identifiant de corrélation normalisé par le middleware (ARC-47).
        sortants.TryAddWithoutValidation(CorrelationIdMiddleware.Header, contexte.HttpContext.TraceIdentifier);

        // En-têtes du corps : type de contenu (JSON, multipart des imports et pièces jointes) et longueur.
        if (contexte.ProxyRequest.Content is { } contenu)
        {
            var requete = contexte.HttpContext.Request;
            if (!string.IsNullOrEmpty(requete.ContentType) && System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(requete.ContentType, out var type))
            {
                contenu.Headers.ContentType = type;
            }

            if (requete.ContentLength is { } longueur)
            {
                contenu.Headers.ContentLength = longueur;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Défense en profondeur contre la sortie de <c>/api/v1</c> : segment « . » ou « .. », point, barre ou barre inverse encodés,
    /// barre inverse. Kestrel normalise déjà les segments de points ; ce filtre couvre les formes encodées qu'il laisse passer.
    /// </summary>
    internal static bool CheminDetourne(string? chemin, string? cible)
    {
        static bool Suspect(string valeur) =>
            valeur.Contains('\\', StringComparison.Ordinal)
            || valeur.Contains("%2e", StringComparison.OrdinalIgnoreCase)
            || valeur.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || valeur.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            || valeur.Split('/').Any(segment => segment is "." or "..");

        var cheminCible = cible?.Split('?', 2)[0];
        return (chemin is not null && Suspect(chemin)) || (cheminCible is not null && Suspect(cheminCible));
    }

    private static async Task RefuserCheminDetourneAsync(HttpContext http, Func<Task> suivant)
    {
        if (CheminDetourne(http.Request.Path.Value, http.Features.Get<IHttpRequestFeature>()?.RawTarget))
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "relais.chemin-invalide",
                    detail: "Chemin de requête invalide.",
                    extensions: new Dictionary<string, object?> { ["code"] = "relais.chemin-invalide" })
                .ExecuteAsync(http);
            return;
        }

        await suivant();
    }

    /// <summary>Service injoignable ou trop lent : ProblemDetails explicite (503 ou 504) au lieu du 502 vide de YARP.</summary>
    private static async Task TraduireEchecAsync(HttpContext http, Func<Task> suivant)
    {
        await suivant();
        var echec = http.Features.Get<IForwarderErrorFeature>();
        if (echec is null || http.Response.HasStarted)
        {
            return;
        }

        var delai = echec.Error is ForwarderError.RequestTimedOut;
        var code = delai ? "service-aval.delai-depasse" : "service-aval.indisponible";
        var service = http.GetRouteModel().Config.ClusterId;
        await Results.Problem(
                statusCode: delai ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status503ServiceUnavailable,
                title: code,
                detail: "Ce service est momentanément indisponible. Veuillez réessayer dans quelques instants.",
                extensions: new Dictionary<string, object?> { ["code"] = code, ["service"] = service })
            .ExecuteAsync(http);
    }

    /// <summary>Client HTTP du relais : délai de connexion borné ; YARP ne rejoue jamais une requête.</summary>
    private sealed class ClientRelaisFactory : ForwarderHttpClientFactory
    {
        protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
        {
            base.ConfigureHandler(context, handler);
            handler.ConnectTimeout = DelaiConnexion;
        }
    }
}
