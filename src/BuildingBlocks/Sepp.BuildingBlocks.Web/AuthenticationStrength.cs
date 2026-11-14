using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.BuildingBlocks.Web;

/// <summary>Traitement d'une authentification trop faible sur une route qui exige l'authentification forte (NF-01).</summary>
public enum AuthenticationStrengthMode
{
    /// <summary>Aucun contrôle (à n'utiliser que pour un diagnostic local).</summary>
    Disabled,

    /// <summary>Mode souple : l'accès est accordé, l'insuffisance est journalisée (défaut en Development, Test et Testing).</summary>
    Log,

    /// <summary>Mode strict : 401 <c>insufficient_user_authentication</c> (RFC 9470) ; défaut hors développement, imposé en Azure.</summary>
    Enforce,
}

/// <summary>
/// Niveaux d'authentification acceptés (section <c>Authentication:Strength</c>). Une authentification est forte si le jeton
/// porte au moins une des valeurs acceptées : claim <c>acr</c> (niveau Keycloak, par ex. <c>mfa</c>), une valeur du claim
/// <c>amr</c> (méthodes : <c>otp</c> en Keycloak, <c>mfa</c> en Entra ID) ou une valeur du claim <c>acrs</c> (contexte
/// d'authentification Entra ID lié à une stratégie d'accès conditionnel, par ex. <c>c1</c>). Valeurs documentées dans
/// <c>docs/securite/authentification-forte.md</c>.
/// </summary>
public sealed class AuthenticationStrengthOptions
{
    public const string Section = "Authentication:Strength";

    /// <summary>Niveaux de démonstration locale (realm Keycloak <c>veilla</c>), appliqués quand rien n'est configuré hors Azure.</summary>
    public static readonly IReadOnlyList<string> AcrLocaux = ["mfa"];

    /// <summary>Méthodes de démonstration locale : OTP Keycloak, ou MFA déclarée par l'émetteur.</summary>
    public static readonly IReadOnlyList<string> AmrLocaux = ["otp", "mfa"];

    /// <summary><c>null</c> : <see cref="AuthenticationStrengthMode.Log"/> en Development, Test et Testing, sinon <see cref="AuthenticationStrengthMode.Enforce"/>.</summary>
    public AuthenticationStrengthMode? Mode { get; set; }

    /// <summary>Valeurs acceptées du claim <c>acr</c>.</summary>
    public string[] AcceptedAcr { get; set; } = [];

    /// <summary>Valeurs acceptées du claim <c>amr</c> (une seule suffit).</summary>
    public string[] AcceptedAmr { get; set; } = [];

    /// <summary>Contextes d'authentification Entra ID acceptés (claim <c>acrs</c>, valeurs <c>c1</c> à <c>c99</c>).</summary>
    public string[] AcceptedAuthenticationContexts { get; set; } = [];

    /// <summary>Valeur <c>acr_values</c> annoncée dans le défi (RFC 9470) pour que le client demande la montée en niveau.</summary>
    public string? ChallengeAcrValues { get; set; }

    /// <summary>Ancienneté maximale de l'authentification (claim <c>auth_time</c>) ; <c>null</c> : pas de limite.</summary>
    public TimeSpan? MaxAge { get; set; }

    internal bool AucuneValeurAcceptee =>
        AcceptedAcr.Length == 0 && AcceptedAmr.Length == 0 && AcceptedAuthenticationContexts.Length == 0;
}

/// <summary>Exigence d'authentification forte (NF-01) : MFA, eID ou itsme pour signer et accéder aux données médicales.</summary>
public sealed class AuthenticationStrengthRequirement : IAuthorizationRequirement
{
    public static readonly AuthenticationStrengthRequirement Instance = new();

    private AuthenticationStrengthRequirement()
    {
    }
}

/// <summary>
/// Authentification forte (NF-01) : politique réutilisable, application automatique aux permissions des zones médicale et
/// psychosociale, et défi de montée en niveau (RFC 9470).
/// </summary>
public static partial class AuthenticationStrength
{
    /// <summary>Nom de la politique, pour <c>RequireAuthorization</c> ou <see cref="RequireAuthenticationStrength{TBuilder}"/>.</summary>
    public const string PolicyName = "authentification-forte";

    public const string ErrorCode = "insufficient_user_authentication";

    private static readonly string[] PrefixesSensibles = ["dossier-sante:", "dossier-psy:"];

    /// <summary>
    /// Permissions des zones sensibles qui n'ouvrent aucun contenu médical ou psychosocial à une personne interne : dépôt d'une
    /// demande de copie par le travailleur (portail, fédération CSAM en Azure) et compte technique du service Documents
    /// (client credentials, sans utilisateur).
    /// </summary>
    private static readonly HashSet<string> Exemptees = new(StringComparer.Ordinal)
    {
        Permissions.CopieDossierDemander,
        Permissions.DossierSanteTransmettreDocuments,
    };

    /// <summary>Une route protégée par cette permission exige-t-elle l'authentification forte ? (<c>dossier-sante:*</c>, <c>dossier-psy:*</c>).</summary>
    public static bool IsRequiredFor(string permission) =>
        PrefixesSensibles.Any(p => permission.StartsWith(p, StringComparison.Ordinal)) && !Exemptees.Contains(permission);

    /// <summary>Exige l'authentification forte sur la route ou le groupe (par ex. signature d'une décision, SAN-32).</summary>
    public static TBuilder RequireAuthenticationStrength<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PolicyName);

    /// <summary>Le jeton atteste-t-il une authentification forte selon les niveaux acceptés ?</summary>
    public static bool IsSatisfiedBy(ClaimsPrincipal user, AuthenticationStrengthOptions options, DateTimeOffset maintenant)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(options);

        var niveau = user.FindAll("acr").Any(c => options.AcceptedAcr.Contains(c.Value, StringComparer.Ordinal))
            || user.FindAll("amr").Any(c => options.AcceptedAmr.Contains(c.Value, StringComparer.Ordinal))
            || user.FindAll("acrs").Any(c => options.AcceptedAuthenticationContexts.Contains(c.Value, StringComparer.Ordinal));
        if (!niveau || options.MaxAge is not { } ancienneteMax)
        {
            return niveau;
        }

        return long.TryParse(user.FindFirstValue("auth_time"), out var secondes)
            && maintenant - DateTimeOffset.FromUnixTimeSeconds(secondes) <= ancienneteMax;
    }

    /// <summary>
    /// Valeur de l'en-tête <c>WWW-Authenticate</c> du défi (RFC 9470, §3) : <c>error</c>, <c>acr_values</c> et <c>max_age</c>
    /// si configurés, et, pour Entra ID, le paramètre <c>claims</c> (demande du contexte d'authentification, encodée en base64)
    /// que MSAL rejoue lors de la nouvelle demande de jeton.
    /// </summary>
    public static string Challenge(AuthenticationStrengthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var parametres = new List<string>
        {
            $"error=\"{ErrorCode}\"",
            "error_description=\"A stronger authentication is required\"",
        };
        if (!string.IsNullOrWhiteSpace(options.ChallengeAcrValues))
        {
            parametres.Add($"acr_values=\"{options.ChallengeAcrValues}\"");
        }

        if (options.MaxAge is { } max)
        {
            parametres.Add($"max_age={(long)max.TotalSeconds}");
        }

        if (options.AcceptedAuthenticationContexts.Length > 0)
        {
            object acrs = options.AcceptedAuthenticationContexts.Length == 1
                ? new { essential = true, value = options.AcceptedAuthenticationContexts[0] }
                : new { essential = true, values = options.AcceptedAuthenticationContexts };
            var claims = JsonSerializer.Serialize(new { access_token = new { acrs } });
            parametres.Add($"claims=\"{Convert.ToBase64String(Encoding.UTF8.GetBytes(claims))}\"");
        }

        return "Bearer " + string.Join(", ", parametres);
    }

    /// <summary>Options (validées au démarrage), gestionnaire d'exigence et traduction du refus en défi 401.</summary>
    internal static void AddAuthenticationStrengthServices(this WebApplicationBuilder builder)
    {
        var horsAzure = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test") || builder.Environment.IsEnvironment("Testing");
        builder.Services.AddOptions<AuthenticationStrengthOptions>()
            .Bind(builder.Configuration.GetSection(AuthenticationStrengthOptions.Section))
            .PostConfigure(o =>
            {
                o.Mode ??= horsAzure ? AuthenticationStrengthMode.Log : AuthenticationStrengthMode.Enforce;
                if (horsAzure && o.AucuneValeurAcceptee)
                {
                    o.AcceptedAcr = [.. AuthenticationStrengthOptions.AcrLocaux];
                    o.AcceptedAmr = [.. AuthenticationStrengthOptions.AmrLocaux];
                    o.ChallengeAcrValues ??= AuthenticationStrengthOptions.AcrLocaux[0];
                }
            })
            .Validate(o => o.Mode != AuthenticationStrengthMode.Enforce || !o.AucuneValeurAcceptee,
                $"{AuthenticationStrengthOptions.Section} : en mode Enforce, au moins une valeur acceptée (AcceptedAcr, AcceptedAmr ou AcceptedAuthenticationContexts) est obligatoire.")
            .Validate(o => o.ChallengeAcrValues is null || ValeurSure().IsMatch(o.ChallengeAcrValues),
                $"{AuthenticationStrengthOptions.Section}:ChallengeAcrValues : caractères admis A-Z, a-z, 0-9, « : . _ - » et espace.")
            .Validate(o => o.AcceptedAuthenticationContexts.All(c => ContexteEntra().IsMatch(c)),
                $"{AuthenticationStrengthOptions.Section}:AcceptedAuthenticationContexts : valeurs c1 à c99 (contextes d'authentification Entra ID).")
            .ValidateOnStart();
        builder.Services.AddSingleton<IAuthorizationHandler, AuthenticationStrengthHandler>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, AuthenticationStrengthResultHandler>());
    }

    [GeneratedRegex("^[A-Za-z0-9:._ -]{1,128}$")]
    internal static partial Regex ValeurSure();

    [GeneratedRegex("^c([1-9]|[1-9][0-9])$")]
    private static partial Regex ContexteEntra();
}

/// <summary>
/// Évalue <see cref="AuthenticationStrengthRequirement"/> selon le mode : accès accordé et insuffisance journalisée
/// (<see cref="AuthenticationStrengthMode.Log"/>), ou exigence non satisfaite (<see cref="AuthenticationStrengthMode.Enforce"/>).
/// Le journal ne porte que la route et les niveaux présentés, jamais de donnée du dossier (NF-04).
/// </summary>
internal sealed partial class AuthenticationStrengthHandler(
    IOptionsMonitor<AuthenticationStrengthOptions> options,
    TimeProvider horloge,
    ILogger<AuthenticationStrengthHandler> logger) : AuthorizationHandler<AuthenticationStrengthRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthenticationStrengthRequirement requirement)
    {
        var configuration = options.CurrentValue;
        if (configuration.Mode == AuthenticationStrengthMode.Disabled
            || AuthenticationStrength.IsSatisfiedBy(context.User, configuration, horloge.GetUtcNow()))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var route = (context.Resource as HttpContext)?.GetEndpoint() is RouteEndpoint endpoint ? endpoint.RoutePattern.RawText : null;
        var acr = Assainir(context.User.FindFirstValue("acr"));
        var amr = string.Join(' ', context.User.FindAll("amr").Select(c => Assainir(c.Value)));
        if (configuration.Mode == AuthenticationStrengthMode.Log)
        {
            AuthentificationInsuffisanteToleree(logger, route ?? "?", acr, amr);
            context.Succeed(requirement);
        }
        else
        {
            AuthentificationInsuffisanteRefusee(logger, route ?? "?", acr, amr);
        }

        return Task.CompletedTask;
    }

    private static string Assainir(string? valeur) =>
        valeur is null ? "-" : AuthenticationStrength.ValeurSure().IsMatch(valeur) ? valeur : "(invalide)";

    [LoggerMessage(Level = LogLevel.Warning, Message = "NF-01 : authentification forte absente sur {Route} (acr={Acr}, amr={Amr}) ; accès accordé en mode Log.")]
    private static partial void AuthentificationInsuffisanteToleree(ILogger logger, string route, string acr, string amr);

    [LoggerMessage(Level = LogLevel.Information, Message = "NF-01 : authentification forte exigée sur {Route} (acr={Acr}, amr={Amr}) ; montée en niveau demandée.")]
    private static partial void AuthentificationInsuffisanteRefusee(ILogger logger, string route, string acr, string amr);
}

/// <summary>
/// Refus dû à la seule authentification forte : 401 avec <c>WWW-Authenticate: Bearer error="insufficient_user_authentication"</c>
/// (RFC 9470) et ProblemDetails, pour que le client relance l'authentification au niveau requis. Si une autre exigence échoue
/// aussi (permission absente), la réponse reste le 403 habituel : monter en niveau n'y changerait rien.
/// </summary>
internal sealed class AuthenticationStrengthResultHandler(IOptionsMonitor<AuthenticationStrengthOptions> options) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaut = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure is { FailCalled: false } echec
            && echec.FailedRequirements.Any()
            && echec.FailedRequirements.All(r => r is AuthenticationStrengthRequirement))
        {
            var configuration = options.CurrentValue;
            context.Response.Headers[HeaderNames.WWWAuthenticate] = AuthenticationStrength.Challenge(configuration);
            await Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "authentification.niveau-insuffisant",
                    detail: "Cette action exige une authentification forte (NF-01). Authentifiez-vous de nouveau avec un second facteur, l'eID ou itsme.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "authentification.niveau-insuffisant",
                        ["acr_values"] = configuration.ChallengeAcrValues,
                    })
                .ExecuteAsync(context);
            return;
        }

        await _defaut.HandleAsync(next, context, policy, authorizeResult);
    }
}
