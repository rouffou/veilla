using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Sepp.BuildingBlocks.Web;

/// <summary>
/// Schéma de sécurité OAuth2 des documents OpenAPI (INT-01, ARC-30, ARC-40) : <c>oauth2</c> en code d'autorisation avec PKCE
/// (applications) et en client credentials (comptes techniques, API partenaires), exigé sur toute opération protégée ;
/// les sondes et les opérations <c>AllowAnonymous</c> n'en portent aucune. Les URL viennent de la configuration
/// <c>Authentication:*</c> : <c>AuthorizationUrl</c> et <c>TokenUrl</c> explicites, sinon déduites de <c>Authority</c>
/// (Microsoft Entra ID si l'émetteur est <c>login.microsoftonline.com</c>, sinon Keycloak local).
/// </summary>
public static class SecuriteOpenApi
{
    public const string NomSchema = "oauth2";

    public static IServiceCollection AddSeppOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer<SchemaOAuth2Transformer>();
            o.AddOperationTransformer<ExigenceSecuriteTransformer>();
        });
        return services;
    }

    internal static (Uri Autorisation, Uri Jeton)? Urls(IConfiguration configuration)
    {
        var section = configuration.GetSection("Authentication");
        var autorisation = section["AuthorizationUrl"];
        var jeton = section["TokenUrl"];
        var authority = section["Authority"]?.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(authority))
        {
            if (authority.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
            {
                var racine = authority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase) ? authority[..^"/v2.0".Length] : authority;
                autorisation = string.IsNullOrWhiteSpace(autorisation) ? $"{racine}/oauth2/v2.0/authorize" : autorisation;
                jeton = string.IsNullOrWhiteSpace(jeton) ? $"{racine}/oauth2/v2.0/token" : jeton;
            }
            else
            {
                autorisation = string.IsNullOrWhiteSpace(autorisation) ? $"{authority}/protocol/openid-connect/auth" : autorisation;
                jeton = string.IsNullOrWhiteSpace(jeton) ? $"{authority}/protocol/openid-connect/token" : jeton;
            }
        }

        return Uri.TryCreate(autorisation, UriKind.Absolute, out var a) && Uri.TryCreate(jeton, UriKind.Absolute, out var t)
            ? (a, t)
            : null;
    }

    /// <summary>Portées annoncées : <c>Authentication:Scopes</c> (portée → description), sinon <c>&lt;Audience&gt;/.default</c>.</summary>
    internal static Dictionary<string, string> Portees(IConfiguration configuration)
    {
        var section = configuration.GetSection("Authentication");
        var portees = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var portee in section.GetSection("Scopes").GetChildren())
        {
            portees[portee.Key] = portee.Value ?? portee.Key;
        }

        if (portees.Count == 0 && !string.IsNullOrWhiteSpace(section["Audience"]))
        {
            portees[$"{section["Audience"]}/.default"] = "Accès à l'API";
        }

        return portees;
    }
}

internal sealed class SchemaOAuth2Transformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (SecuriteOpenApi.Urls(configuration) is not { } urls)
        {
            return Task.CompletedTask;
        }

        var portees = SecuriteOpenApi.Portees(configuration);
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SecuriteOpenApi.NomSchema] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "OAuth2 : code d'autorisation avec PKCE (applications) ou client credentials (comptes techniques).",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = urls.Autorisation,
                    TokenUrl = urls.Jeton,
                    Scopes = new Dictionary<string, string>(portees, StringComparer.Ordinal),
                },
                ClientCredentials = new OpenApiOAuthFlow
                {
                    TokenUrl = urls.Jeton,
                    Scopes = new Dictionary<string, string>(portees, StringComparer.Ordinal),
                },
            },
        };
        return Task.CompletedTask;
    }
}

internal sealed class ExigenceSecuriteTransformer(IConfiguration configuration) : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (SecuriteOpenApi.Urls(configuration) is null)
        {
            return Task.CompletedTask;
        }

        // La politique de repli exige un utilisateur authentifié : seules les opérations AllowAnonymous (sondes, OpenAPI) y échappent.
        var anonyme = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
        if (anonyme)
        {
            return Task.CompletedTask;
        }

        var portees = SecuriteOpenApi.Portees(configuration).Keys.ToList();
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SecuriteOpenApi.NomSchema, context.Document)] = portees,
        });
        return Task.CompletedTask;
    }
}
