using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Sepp.Bff.Travailleur.Integration.Tests;

/// <summary>
/// Services aval simulés au niveau HTTP (gestionnaire primaire des clients typés) : réponses programmées par
/// hôte, méthode et chemin, et capture de chaque requête reçue (en-têtes, corps) pour vérifier la propagation.
/// </summary>
public sealed class ServicesAvalSimules : HttpMessageHandler
{
    public const string Planification = "planification.test";
    public const string SurveillanceMedicale = "surveillance-medicale.test";
    public const string Obligations = "obligations.test";
    public const string Documents = "documents.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<(string Hote, string Methode, string Chemin), (HttpStatusCode Status, string Corps)> _reponses = new();

    private readonly ConcurrentDictionary<(string Hote, string Chemin), (byte[] Contenu, string Nom)> _fichiers = new();

    public ConcurrentQueue<RequeteRecue> Requetes { get; } = new();

    public ConcurrentDictionary<string, bool> HotesInjoignables { get; } = new();

    public void Repondre(string hote, string chemin, object corps, HttpStatusCode status = HttpStatusCode.OK, string methode = "GET") =>
        _reponses[(hote, methode, chemin)] = (status, JsonSerializer.Serialize(corps, Json));

    /// <summary>Réponse binaire (PDF) avec son <c>Content-Disposition</c>.</summary>
    public void RepondreFichier(string hote, string chemin, byte[] contenu, string nomFichier) => _fichiers[(hote, chemin)] = (contenu, nomFichier);

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

        if (HotesInjoignables.ContainsKey(hote))
        {
            throw new HttpRequestException("Connexion refusée (simulation).");
        }

        if (_fichiers.TryGetValue((hote, request.RequestUri.AbsolutePath), out var fichier))
        {
            var pdf = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fichier.Contenu) };
            pdf.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            pdf.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = fichier.Nom };
            return pdf;
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
