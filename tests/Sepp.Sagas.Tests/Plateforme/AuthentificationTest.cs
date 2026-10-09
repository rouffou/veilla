using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sepp.Sagas.Tests.Plateforme;

/// <summary>
/// Authentification de test, sur le modèle des fixtures d'intégration des services : rôles, utilisateur et périmètre
/// (claims <c>affilie_id</c> et <c>personne_id</c>) passés dans des en-têtes.
/// </summary>
internal sealed class AuthentificationTest(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Schema = "Test";
    public const string RolesEntete = "X-Test-Roles";
    public const string UtilisateurEntete = "X-Test-User";
    public const string AffilieEntete = "X-Test-Affilie";
    public const string PersonneEntete = "X-Test-Personne";

    public static void Ajouter(IServiceCollection services)
    {
        services.AddAuthentication(Schema).AddScheme<AuthenticationSchemeOptions, AuthentificationTest>(Schema, _ => { });
        services.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultAuthenticateScheme = Schema;
            o.DefaultChallengeScheme = Schema;
        });
    }

    public static HttpClient Configurer(HttpClient client, string role, string utilisateur, Guid? affilie, Guid? personne)
    {
        client.DefaultRequestHeaders.Add(RolesEntete, role);
        client.DefaultRequestHeaders.Add(UtilisateurEntete, utilisateur);
        if (affilie is { } a)
        {
            client.DefaultRequestHeaders.Add(AffilieEntete, a.ToString());
        }

        if (personne is { } p)
        {
            client.DefaultRequestHeaders.Add(PersonneEntete, p.ToString());
        }

        return client;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesEntete, out var roles))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => new Claim("roles", r))
            .Append(new Claim("sub", Request.Headers.TryGetValue(UtilisateurEntete, out var utilisateur) ? utilisateur.ToString() : "test-user"));
        if (Request.Headers.TryGetValue(AffilieEntete, out var affilies))
        {
            claims = claims.Concat(affilies.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => new Claim("affilie_id", a)));
        }

        if (Request.Headers.TryGetValue(PersonneEntete, out var personne))
        {
            claims = claims.Append(new Claim("personne_id", personne.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Schema, "sub", "roles"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Schema)));
    }
}
