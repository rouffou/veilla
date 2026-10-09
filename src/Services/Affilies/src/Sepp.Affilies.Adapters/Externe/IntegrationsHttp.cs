using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Application;
using Sepp.Affilies.Domain.Affilies;

namespace Sepp.Affilies.Adapters.Externe;

/// <summary>Noms des clients HTTP du service (remplaçables dans les tests).</summary>
public static class ClientsHttp
{
    public const string Integrations = "affilies-integrations";
    public const string JetonOidc = "affilies-jeton-oidc";
}

/// <summary>
/// Compte technique OIDC du service (client credentials, configuration <c>Affilies:CompteTechnique</c>) portant le rôle
/// <c>affilies</c> : lecture des données d'entreprise de la BCE chez Intégrations. Le secret vient du Key Vault en Azure,
/// des secrets utilisateur en local.
/// </summary>
public sealed class OptionsCompteTechnique
{
    public Uri? TokenEndpoint { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? Scope { get; set; }
}

/// <summary>Adresses internes des services interrogés (configuration <c>Affilies:ServicesInternes</c>).</summary>
public sealed class OptionsServicesInternes
{
    public Uri? Integrations { get; set; }
}

public sealed class JetonIndisponibleException : Exception
{
    public JetonIndisponibleException(string message) : base(message)
    {
    }

    public JetonIndisponibleException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public JetonIndisponibleException()
    {
    }
}

/// <summary>Jeton du compte technique, mis en cache jusqu'à une minute avant son expiration.</summary>
public sealed class FournisseurJetonTechnique(IHttpClientFactory fabrique, OptionsCompteTechnique options, TimeProvider horloge) : IDisposable
{
    private readonly SemaphoreSlim _verrou = new(1, 1);
    private string? _jeton;
    private DateTimeOffset _expiration;

    public async Task<string> ObtenirAsync(CancellationToken cancellationToken)
    {
        if (_jeton is { } jeton && horloge.GetUtcNow() < _expiration)
        {
            return jeton;
        }

        await _verrou.WaitAsync(cancellationToken);
        try
        {
            if (_jeton is { } recent && horloge.GetUtcNow() < _expiration)
            {
                return recent;
            }

            if (options.TokenEndpoint is null || string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new JetonIndisponibleException("Compte technique non configuré (Affilies:CompteTechnique : TokenEndpoint, ClientId, ClientSecret).");
            }

            var formulaire = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
            };
            if (!string.IsNullOrWhiteSpace(options.Scope))
            {
                formulaire["scope"] = options.Scope;
            }

            using var contenu = new FormUrlEncodedContent(formulaire);
            using var reponse = await fabrique.CreateClient(ClientsHttp.JetonOidc).PostAsync(options.TokenEndpoint, contenu, cancellationToken);
            if (!reponse.IsSuccessStatusCode)
            {
                throw new JetonIndisponibleException($"Jeton refusé par le fournisseur d'identité (HTTP {(int)reponse.StatusCode}).");
            }

            var corps = await reponse.Content.ReadFromJsonAsync<ReponseJeton>(cancellationToken);
            if (string.IsNullOrWhiteSpace(corps?.AccessToken))
            {
                throw new JetonIndisponibleException("Réponse du fournisseur d'identité sans jeton d'accès.");
            }

            _jeton = corps.AccessToken;
            _expiration = horloge.GetUtcNow().AddSeconds(Math.Max(0, (corps.ExpiresIn ?? 300) - 60));
            return _jeton;
        }
        finally
        {
            _verrou.Release();
        }
    }

    public void Dispose() => _verrou.Dispose();

    private sealed record ReponseJeton(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);
}

/// <summary>Ajoute le jeton du compte technique aux appels des services internes.</summary>
internal sealed class JetonTechniqueHandler(FournisseurJetonTechnique fournisseur) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await fournisseur.ObtenirAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Lecture de <c>GET /api/v1/bce/entreprises/{numeroBce}</c> du service Intégrations (INT-04) avec le compte technique.
/// Absence de données (404) → <c>null</c> ; toute autre panne → <see cref="EntrepriseBceIndisponibleException"/> (reprise du message).
/// </summary>
internal sealed class EntrepriseBceHttp(IHttpClientFactory fabrique) : IEntrepriseBceClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<EntrepriseBce?> LireAsync(string numeroBce, CancellationToken cancellationToken)
    {
        var http = fabrique.CreateClient(ClientsHttp.Integrations);
        if (http.BaseAddress is null)
        {
            throw new EntrepriseBceIndisponibleException("Adresse du service Intégrations non configurée (Affilies:ServicesInternes:Integrations).");
        }

        try
        {
            using var reponse = await http.GetAsync(new Uri($"api/v1/bce/entreprises/{Uri.EscapeDataString(numeroBce)}", UriKind.Relative), cancellationToken);
            if (reponse.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!reponse.IsSuccessStatusCode)
            {
                throw new EntrepriseBceIndisponibleException($"Lecture des données BCE refusée ou en échec chez Intégrations (HTTP {(int)reponse.StatusCode}).");
            }

            var dto = await reponse.Content.ReadFromJsonAsync<EntrepriseBceReponse>(Json, cancellationToken)
                      ?? throw new EntrepriseBceIndisponibleException("Réponse vide d'Intégrations.");
            return new EntrepriseBce(
                dto.NumeroBce, dto.Denomination, dto.FormeJuridique, dto.CodeNace, dto.DateExtraction,
                (dto.UnitesEtablissement ?? []).Select(u => new UniteBce(
                    u.Numero, u.Denomination, u.Adresse.Rue, u.Adresse.Numero, u.Adresse.Boite, u.Adresse.CodePostal, u.Adresse.Localite, u.Adresse.CodePays, u.DateDebut)).ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or JetonIndisponibleException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new EntrepriseBceIndisponibleException($"Service Intégrations injoignable ou réponse illisible ({ex.GetType().Name}).", ex);
        }
    }

    private sealed record EntrepriseBceReponse(
        string NumeroBce,
        string Denomination,
        string FormeJuridique,
        string CodeNace,
        DateOnly DateExtraction,
        IReadOnlyList<UniteReponse>? UnitesEtablissement);

    private sealed record UniteReponse(string Numero, string Denomination, AdresseReponse Adresse, DateOnly? DateDebut);

    private sealed record AdresseReponse(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string CodePays);
}

internal static class ClientsInternes
{
    public static void Ajouter(IServiceCollection services, OptionsServicesInternes adresses, OptionsCompteTechnique compte)
    {
        services.AddSingleton(compte);
        services.AddSingleton<FournisseurJetonTechnique>();
        services.AddTransient<JetonTechniqueHandler>();
        services.AddHttpClient(ClientsHttp.JetonOidc);
        services.AddHttpClient(ClientsHttp.Integrations, c => c.BaseAddress = adresses.Integrations).AddHttpMessageHandler<JetonTechniqueHandler>();
        services.AddScoped<IEntrepriseBceClient, EntrepriseBceHttp>();
    }
}
