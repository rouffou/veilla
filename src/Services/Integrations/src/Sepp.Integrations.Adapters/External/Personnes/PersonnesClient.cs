using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Polly;

using Sepp.Integrations.Application.Externe;

namespace Sepp.Integrations.Adapters.External.Personnes;

/// <summary>Noms des clients HTTP du service (remplaçables dans les tests).</summary>
public static class ClientsHttp
{
    public const string Personnes = "integrations-personnes";
    public const string JetonOidc = "integrations-jeton-oidc";
}

/// <summary>
/// Appel du service Personnes (configuration <c>Integrations:Personnes</c>) : adresse interne et compte technique
/// OIDC (client credentials) portant le rôle <c>integrations</c>. Le secret vient de Key Vault ou des secrets utilisateur.
/// </summary>
public sealed class OptionsPersonnes
{
    public Uri? BaseAddress { get; set; }

    /// <summary>Point de jeton du fournisseur d'identité (ex. <c>…/realms/veilla/protocol/openid-connect/token</c>).</summary>
    public Uri? TokenEndpoint { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Portée demandée (facultative, ex. l'audience <c>sepp-api</c> selon le fournisseur).</summary>
    public string? Scope { get; set; }
}

/// <summary>
/// Client HTTP typé de l'API interne DIMONA du service Personnes (AFF-20). Le NISS ne transite que dans le corps de ces
/// appels internes, jamais dans une URL, un événement ou un journal (ARC-06, DAT-06). Résilience par défaut du socle
/// (délai, reprises, disjoncteur) : les points d'entrée de Personnes sont idempotents, une reprise ne duplique rien.
/// </summary>
internal sealed class PersonnesHttpClient(HttpClient http) : IPersonnesClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task<ResultatAppel<OccupationDimona>> EnregistrerEntreeDimonaAsync(EntreeDimonaPersonnes entree, CancellationToken cancellationToken) =>
        AppelerAsync(
            () => http.PostAsJsonAsync("api/v1/dimona/entrees", new
            {
                entree.ReferenceDimona,
                entree.Niss,
                Identite = new { entree.Identite.Nom, entree.Identite.Prenom, entree.Identite.DateNaissance, entree.Identite.Sexe, entree.Identite.Langue },
                entree.AffilieId,
                entree.AffilieUtilisateurId,
                entree.TypeTravailleur,
                entree.TypeContrat,
                entree.DateDebut,
                entree.DateFin,
            }, Json, cancellationToken),
            async reponse => (await reponse.Content.ReadFromJsonAsync<OccupationDimona>(Json, cancellationToken))!,
            cancellationToken);

    public Task<ResultatAppel<bool>> EnregistrerSortieDimonaAsync(string referenceDimona, DateOnly dateFin, CancellationToken cancellationToken) =>
        AppelerAsync(
            () => http.PostAsJsonAsync($"api/v1/dimona/{Uri.EscapeDataString(referenceDimona)}/sortie", new { dateFin }, Json, cancellationToken),
            _ => Task.FromResult(true),
            cancellationToken);

    /// <summary>
    /// Lacune documentée : le service Personnes n'expose pas d'API de mise à jour de l'identité par NISS accessible au
    /// compte technique (la correction d'identité se fait par identifiant de personne et la recherche par NISS exige
    /// personne:lire). La mutation reste en erreur, relançable, jusqu'à l'ajout de ce point d'entrée (voir README).
    /// </summary>
    public Task<ResultatAppel<bool>> AppliquerMutationIdentiteAsync(string niss, IdentiteRegistreNational identite, CancellationToken cancellationToken) =>
        Task.FromResult(ResultatAppel<bool>.Erreur("integrations.personnes-api-mutation-absente",
            "Le service Personnes n'expose pas encore de point d'entrée interne de mise à jour d'identité par NISS : mutation à relancer une fois ce point d'entrée disponible."));

    private async Task<ResultatAppel<T>> AppelerAsync<T>(Func<Task<HttpResponseMessage>> appel, Func<HttpResponseMessage, Task<T>> lire, CancellationToken cancellationToken)
    {
        if (http.BaseAddress is null)
        {
            return ResultatAppel<T>.Erreur("integrations.configuration-personnes", "Adresse du service Personnes non configurée (Integrations:Personnes:BaseAddress).");
        }

        try
        {
            using var reponse = await appel();
            if (reponse.IsSuccessStatusCode)
            {
                return ResultatAppel<T>.Succes(await lire(reponse));
            }

            var (code, detail) = await LireProblemeAsync(reponse, cancellationToken);
            return reponse.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity =>
                    ResultatAppel<T>.Rejet(code ?? "personnes.rejet", detail ?? $"Refusé par le service Personnes (HTTP {(int)reponse.StatusCode})."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    ResultatAppel<T>.Erreur("integrations.personnes-acces-refuse",
                        $"Accès refusé par le service Personnes (HTTP {(int)reponse.StatusCode}) : vérifier le compte technique et son rôle « integrations »."),
                _ => ResultatAppel<T>.Erreur(code ?? "integrations.personnes-indisponible",
                    detail ?? $"Échec de l'appel au service Personnes (HTTP {(int)reponse.StatusCode})."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or ExecutionRejectedException or JetonIndisponibleException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return ResultatAppel<T>.Erreur("integrations.personnes-indisponible",
                ex is JetonIndisponibleException ? ex.Message : $"Service Personnes injoignable ({ex.GetType().Name}).");
        }
    }

    private static async Task<(string? Code, string? Detail)> LireProblemeAsync(HttpResponseMessage reponse, CancellationToken cancellationToken)
    {
        if (reponse.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json"))
        {
            return (null, null);
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(await reponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var racine = document.RootElement;
            string? Texte(string nom) => racine.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String ? valeur.GetString() : null;
            return (Texte("code") ?? Texte("title"), Texte("detail"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}

/// <summary>Jeton du compte technique indisponible (configuration incomplète ou refus du fournisseur d'identité).</summary>
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

/// <summary>
/// Jeton d'accès du compte technique (OAuth2 client credentials, authentification <c>client_secret_post</c>),
/// mis en cache jusqu'à une minute avant son expiration.
/// </summary>
public sealed class FournisseurJetonClient(IHttpClientFactory fabrique, OptionsPersonnes options, TimeProvider horloge) : IDisposable
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
                throw new JetonIndisponibleException(
                    "Configuration Integrations:Personnes incomplète (TokenEndpoint, ClientId, ClientSecret) : compte technique non configuré.");
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

            var client = fabrique.CreateClient(ClientsHttp.JetonOidc);
            using var contenu = new FormUrlEncodedContent(formulaire);
            using var reponse = await client.PostAsync(options.TokenEndpoint, contenu, cancellationToken);
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

/// <summary>Ajoute le jeton du compte technique à chaque appel du service Personnes.</summary>
internal sealed class JetonClientHandler(FournisseurJetonClient fournisseur) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await fournisseur.ObtenirAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
