using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

using Sepp.Testing.Contrats;

namespace Sepp.Bff.Employeur.Integration.Tests;

/// <summary>
/// Services aval simulés au niveau HTTP (gestionnaire primaire des clients typés) : réponses programmées par
/// hôte, méthode et chemin, et capture de chaque requête reçue (en-têtes, corps) pour vérifier la propagation.
/// </summary>
public sealed class ServicesAvalSimules : HttpMessageHandler
{
    public const string Affilies = "affilies.test";
    public const string Personnes = "personnes.test";
    public const string PostesRisques = "postes-risques.test";
    public const string Obligations = "obligations.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<(string Hote, string Methode, string Chemin), (HttpStatusCode Status, string Corps)> _reponses = new();

    // Contrôle de contrat (ARC-23) : réponses programmées et requêtes émises confrontées aux instantanés OpenAPI des fournisseurs.
    private readonly ControleContratDouble _controle = new();

    public ConcurrentQueue<RequeteRecue> Requetes { get; } = new();

    public ConcurrentDictionary<string, bool> HotesInjoignables { get; } = new();

    public void Repondre(string hote, string chemin, object corps, HttpStatusCode status = HttpStatusCode.OK, string methode = "GET") =>
        _reponses[(hote, methode, chemin)] = (status, Controler(hote, methode, chemin, status, corps));

    private string Controler(string hote, string methode, string chemin, HttpStatusCode status, object corps)
    {
        var json = JsonSerializer.Serialize(corps, Json);
        _controle.ControlerReponse(hote, methode, chemin, (int)status, json);
        return json;
    }

    /// <summary>Échoue si une requête émise vers un service simulé ne correspond pas à une opération de son contrat OpenAPI.</summary>
    public void ExigerRequetesConformes() => _controle.ExigerRequetesConformes();

    public void Probleme(string hote, string chemin, HttpStatusCode status, string code, string methode = "GET") =>
        Repondre(hote, chemin, new { title = code, status = (int)status, detail = $"Détail {code}", code }, status, methode);

    public void Injoignable(string hote) => HotesInjoignables[hote] = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corps = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var hote = request.RequestUri!.Host;
        Requetes.Enqueue(new RequeteRecue(
            request.Method.Method,
            hote,
            request.RequestUri.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Correlation-Id", out var c) ? c.Single() : null,
            corps));
        _controle.ControlerRequete(hote, request.Method.Method, request.RequestUri.PathAndQuery, corps);

        if (HotesInjoignables.ContainsKey(hote))
        {
            throw new HttpRequestException("Connexion refusée (simulation).");
        }

        if (!_reponses.TryGetValue((hote, request.Method.Method, request.RequestUri.AbsolutePath), out var reponse))
        {
            reponse = (HttpStatusCode.NotFound, """{"title":"introuvable","status":404,"code":"introuvable"}""");
        }

        return new HttpResponseMessage(reponse.Status)
        {
            Content = new StringContent(reponse.Corps, Encoding.UTF8, reponse.Status < HttpStatusCode.BadRequest ? "application/json" : "application/problem+json"),
        };
    }
}

public sealed record RequeteRecue(string Methode, string Hote, string Chemin, string? Authorization, string? Correlation, string? Corps);
