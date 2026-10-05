using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sepp.Documents.Adapters.Externe;

/// <summary>Noms des clients HTTP du service (remplaçables dans les tests).</summary>
public static class ClientsHttp
{
    public const string Affilies = "documents-affilies";
    public const string Personnes = "documents-personnes";
    public const string JetonOidc = "documents-jeton-oidc";
}

/// <summary>
/// Compte technique OIDC du service (client credentials, configuration <c>Documents:CompteTechnique</c>) portant le rôle
/// <c>documents</c> : lecture de l'affilié et de la personne pour la langue des documents (NF-41). Le secret vient du
/// Key Vault en Azure, des secrets utilisateur en local.
/// </summary>
public sealed class OptionsCompteTechnique
{
    public Uri? TokenEndpoint { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? Scope { get; set; }
}

/// <summary>Adresses internes des services interrogés (configuration <c>Documents:ServicesInternes</c>).</summary>
public sealed class OptionsServicesInternes
{
    public Uri? Affilies { get; set; }

    public Uri? Personnes { get; set; }
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
                throw new JetonIndisponibleException("Compte technique non configuré (Documents:CompteTechnique : TokenEndpoint, ClientId, ClientSecret).");
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
