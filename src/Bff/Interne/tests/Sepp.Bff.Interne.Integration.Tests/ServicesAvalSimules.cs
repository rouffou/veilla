using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Sepp.Bff.Interne.Integration.Tests;

/// <summary>
/// Services aval simulés au niveau HTTP, derrière le relais (client YARP) comme derrière les clients typés : réponses
/// programmées par hôte, méthode et chemin, et capture de chaque requête reçue (tous les en-têtes, corps).
/// </summary>
public sealed class ServicesAvalSimules : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<(string Hote, string Methode, string Chemin), (HttpStatusCode Status, string Corps)> _reponses = new();

    public ConcurrentQueue<RequeteRecue> Requetes { get; } = new();

    public ConcurrentDictionary<string, bool> HotesInjoignables { get; } = new();

    public static string Hote(string service) => $"{service}.test";

    public void Repondre(string service, string chemin, object corps, HttpStatusCode status = HttpStatusCode.OK, string methode = "GET") =>
        _reponses[(Hote(service), methode, chemin)] = (status, JsonSerializer.Serialize(corps, Json));

    public void Injoignable(string service) => HotesInjoignables[Hote(service)] = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corps = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var hote = request.RequestUri!.Host;
        var entetes = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requetes.Enqueue(new RequeteRecue(request.Method.Method, hote, request.RequestUri.PathAndQuery, request.Headers.Host, entetes, corps));

        if (HotesInjoignables.ContainsKey(hote))
        {
            throw new HttpRequestException("Connexion refusée (simulation).");
        }

        if (!_reponses.TryGetValue((hote, request.Method.Method, request.RequestUri.AbsolutePath), out var reponse))
        {
            reponse = (HttpStatusCode.NotFound, """{"title":"introuvable","status":404,"code":"introuvable"}""");
        }

        var message = new HttpResponseMessage(reponse.Status)
        {
            Content = new StringContent(reponse.Corps, Encoding.UTF8, reponse.Status < HttpStatusCode.BadRequest ? "application/json" : "application/problem+json"),
        };
        message.Headers.TryAddWithoutValidation("Set-Cookie", "suivi=1; Path=/");
        return message;
    }
}

public sealed record RequeteRecue(string Methode, string Hote, string Chemin, string? EnTeteHost, IReadOnlyDictionary<string, string> EnTetes, string? Corps);
