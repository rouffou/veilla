using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sepp.BuildingBlocks.Web;

/// <summary>
/// CORS des BFF de portail (ARC-42, ARC-43, NF-03) : le portail est servi depuis une autre origine que le BFF.
/// La liste vient de <c>Cors:OriginesAutorisees</c> (en Azure : <c>CORS__ORIGINESAUTORISEES__0</c>… injectées par Terraform).
/// Origines exactes uniquement : jamais de joker, jamais <c>AllowAnyOrigin</c>. Pas de credentials : les portails
/// authentifient par jeton porteur (en-tête <c>Authorization</c>), sans cookie.
/// </summary>
public static class PortailCors
{
    public const string Section = "Cors:OriginesAutorisees";

    /// <summary>En-têtes de requête utilisés par les portails (jeton, JSON, langue, corrélation).</summary>
    private static readonly string[] EnTetesAutorises = ["Authorization", "Content-Type", "Accept-Language", CorrelationIdMiddleware.Header];

    /// <summary>En-têtes de réponse lisibles par les portails (nom du fichier exporté, corrélation).</summary>
    private static readonly string[] EnTetesExposes = ["Content-Disposition", CorrelationIdMiddleware.Header];

    /// <summary>
    /// Valide et normalise les origines. En production : liste non vide, https obligatoire. Hors production (Development,
    /// Test) : http accepté, liste vide acceptée (aucune origine autorisée). Joker, chemin, requête, fragment ou
    /// identifiants dans l'URL sont toujours refusés.
    /// </summary>
    /// <exception cref="InvalidOperationException">Liste vide en production ou origine invalide.</exception>
    public static IReadOnlyList<string> ValiderOrigines(IEnumerable<string?>? origines, bool production)
    {
        var liste = (origines ?? []).ToList();
        if (liste.Count == 0)
        {
            return production
                ? throw new InvalidOperationException($"{Section} : au moins une origine de portail est obligatoire (CORS__ORIGINESAUTORISEES__0).")
                : [];
        }

        var resultat = new List<string>(liste.Count);
        foreach (var brute in liste)
        {
            var erreur = Verifier(brute, production, out var normalisee);
            if (erreur is not null)
            {
                throw new InvalidOperationException($"{Section} : origine « {brute} » invalide ({erreur}).");
            }

            if (!resultat.Contains(normalisee, StringComparer.Ordinal))
            {
                resultat.Add(normalisee);
            }
        }

        return resultat;
    }

    private static string? Verifier(string? origine, bool production, out string normalisee)
    {
        normalisee = string.Empty;
        if (string.IsNullOrWhiteSpace(origine))
        {
            return "valeur vide";
        }

        if (origine.Contains('*', StringComparison.Ordinal))
        {
            return "joker interdit";
        }

        var texte = origine.Trim();
        if (!Uri.TryCreate(texte, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return "URL absolue attendue, par exemple https://portail.example.be";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && (production || uri.Scheme != Uri.UriSchemeHttp))
        {
            return production ? "https obligatoire" : "http ou https attendu";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/" || texte.EndsWith('/'))
        {
            return "une origine est un schéma, un hôte et un port, sans chemin, requête ni identifiants";
        }

        normalisee = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return null;
    }

    /// <summary>
    /// Enregistre la politique CORS par défaut du BFF à partir de <c>Cors:OriginesAutorisees</c> et échoue au démarrage si
    /// la configuration est invalide (voir <see cref="ValiderOrigines"/>).
    /// </summary>
    /// <param name="builder">Hôte du BFF.</param>
    /// <param name="methodes">Méthodes HTTP utilisées par le portail.</param>
    public static WebApplicationBuilder AddSeppPortailCors(this WebApplicationBuilder builder, params string[] methodes)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var hors = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test") || builder.Environment.IsEnvironment("Testing");
        var origines = ValiderOrigines(builder.Configuration.GetSection(Section).Get<string[]>(), production: !hors);

        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (origines.Count > 0)
            {
                p.WithOrigins([.. origines]);
            }

            p.WithMethods(methodes)
                .WithHeaders(EnTetesAutorises)
                .WithExposedHeaders(EnTetesExposes)
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        }));
        return builder;
    }
}
