using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sepp.Testing.Contrats;

/// <summary>Requête reçue par un <see cref="FournisseurHttpSimule"/>.</summary>
public sealed record RequeteHttpRecue(string Methode, string Hote, string CheminEtQuery, string? Autorisation, string? Corps);

/// <summary>
/// Double HTTP d'un ou plusieurs services fournisseurs pour tester un adaptateur de service à service (annuaire de Communications,
/// source linguistique de Documents, BCE d'Affiliés, dossier de santé…) : gestionnaire primaire du client typé (hôtes simulés
/// <c>&lt;service&gt;.test</c>). Chaque réponse programmée est confrontée dès sa programmation à l'instantané OpenAPI du fournisseur
/// (<see cref="ControleContratDouble"/>), et chaque requête émise par l'adaptateur l'est aussi (<see cref="ExigerRequetesConformes"/>) :
/// un double qui s'écarte du contrat (champ inventé ou absent, énumération à la mauvaise casse, route inexistante) échoue au lieu de
/// valider à tort l'adaptateur (#436, #476).
/// </summary>
public sealed class FournisseurHttpSimule(IReadOnlyDictionary<string, string>? alias = null) : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ControleContratDouble _controle = new(alias);

    private readonly ConcurrentDictionary<(string Hote, string Methode, string Chemin), (HttpStatusCode Statut, string Corps)> _reponses = new();

    /// <summary>Requêtes reçues, dans l'ordre d'arrivée.</summary>
    public ConcurrentQueue<RequeteHttpRecue> Requetes { get; } = new();

    /// <summary>Programme une réponse JSON ; échoue immédiatement si elle n'est pas possible chez le fournisseur.</summary>
    public void Repondre(string hote, string chemin, object corps, HttpStatusCode statut = HttpStatusCode.OK, string methode = "GET")
    {
        var json = corps as string ?? JsonSerializer.Serialize(corps, Json);
        _controle.ControlerReponse(hote, methode, chemin, (int)statut, json);
        _reponses[(hote, methode, chemin)] = (statut, json);
    }

    /// <summary>Programme la réponse conforme au contrat (<see cref="ExemplesContrat.Reponse"/>), avec des valeurs de <paramref name="surcharges"/>.</summary>
    public void RepondreSelonContrat(
        string hote, string fournisseur, string chemin, IReadOnlyDictionary<string, object?>? surcharges = null, HttpStatusCode statut = HttpStatusCode.OK, string methode = "GET") =>
        Repondre(hote, chemin, ExemplesContrat.Reponse(fournisseur, methode, ModeleDeChemin(fournisseur, methode, chemin), (int)statut, surcharges), statut, methode);

    /// <summary>Programme une erreur <c>problem+json</c> (par ex. 404 : ressource inconnue).</summary>
    public void Probleme(string hote, string chemin, HttpStatusCode statut, string code, string methode = "GET") =>
        Repondre(hote, chemin, JsonSerializer.Serialize(new { title = code, status = (int)statut, detail = $"Détail {code}", code }, Json), statut, methode);

    /// <summary>Échoue si une requête émise vers un hôte simulé ne correspond pas à une opération conforme de l'instantané.</summary>
    public void ExigerRequetesConformes() => _controle.ExigerRequetesConformes();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corps = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var hote = request.RequestUri!.Host;
        Requetes.Enqueue(new RequeteHttpRecue(request.Method.Method, hote, request.RequestUri.PathAndQuery, request.Headers.Authorization?.ToString(), corps));
        _controle.ControlerRequete(hote, request.Method.Method, request.RequestUri.PathAndQuery, corps);

        if (!_reponses.TryGetValue((hote, request.Method.Method, request.RequestUri.AbsolutePath), out var reponse))
        {
            reponse = (HttpStatusCode.NotFound, """{"title":"introuvable","status":404,"code":"introuvable"}""");
        }

        return new HttpResponseMessage(reponse.Statut)
        {
            Content = new StringContent(reponse.Corps, Encoding.UTF8, reponse.Statut < HttpStatusCode.BadRequest ? "application/json" : "application/problem+json"),
        };
    }

    /// <summary>Chemin modèle de l'instantané correspondant au chemin réel (pour y lire le schéma de la réponse).</summary>
    private static string ModeleDeChemin(string fournisseur, string methode, string chemin)
    {
        var paths = ContratConsommateur.Instantane(fournisseur)["paths"]?.AsObject() ?? [];
        var segments = chemin.Trim('/').Split('/');
        return paths
            .Where(p => p.Value?[methode.ToLowerInvariant()] is not null)
            .Select(p => p.Key)
            .Where(modele => modele.Trim('/').Split('/') is var m && m.Length == segments.Length
                && m.Zip(segments).All(s => (s.First.StartsWith('{') && s.First.EndsWith('}')) || string.Equals(s.First, s.Second, StringComparison.Ordinal)))
            .OrderBy(modele => modele.Count(c => c == '{'))
            .FirstOrDefault() ?? chemin;
    }
}

/// <summary>
/// Réponses d'exemple <b>valides par construction</b> au regard de l'instantané OpenAPI d'un fournisseur : toutes les propriétés
/// non nullables du schéma sont renseignées (énumération : première valeur, tableau : un élément), les propriétés nullables valent
/// <c>null</c>. Le test de l'adaptateur n'a plus qu'à surcharger les valeurs qu'il exerce (<c>fiche.langue</c>, <c>contacts</c>…).
/// </summary>
public static class ExemplesContrat
{
    private const string GuidExemple = "0192a5c8-0000-7000-8000-000000000001";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>JSON de la réponse <paramref name="statut"/> de l'opération <paramref name="methode"/> <paramref name="chemin"/> (modèle de l'instantané).</summary>
    /// <param name="surcharges">Valeurs imposées, par chemin de propriétés séparées par des points (un tableau se remplace en entier).</param>
    public static string Reponse(string fournisseur, string methode, string chemin, int statut = 200, IReadOnlyDictionary<string, object?>? surcharges = null)
    {
        var doc = ContratConsommateur.Instantane(fournisseur);
        var operation = ContratConsommateur.TrouverOperation(doc, methode, chemin)
            ?? throw new InvalidOperationException($"Opération {methode} {chemin} absente de l'instantané {fournisseur}.");
        var schema = ContratConsommateur.SchemaDeReponse(doc, operation, statut)
            ?? throw new InvalidOperationException($"Réponse {statut} de {methode} {chemin} non décrite par l'instantané {fournisseur} : déclarer Produces<T> sur l'endpoint.");
        var exemple = Generer(doc, schema, 0);
        foreach (var (cle, valeur) in surcharges ?? new Dictionary<string, object?>())
        {
            Imposer(exemple, cle.Split('.'), JsonSerializer.SerializeToNode(valeur, Web));
        }

        return exemple?.ToJsonString() ?? "null";
    }

    private static void Imposer(JsonNode? racine, string[] chemin, JsonNode? valeur)
    {
        var courant = racine;
        for (var i = 0; i < chemin.Length - 1; i++)
        {
            courant = courant is JsonArray tableau ? tableau.FirstOrDefault() : courant;
            courant = courant?[chemin[i]];
        }

        courant = courant is JsonArray liste ? liste.FirstOrDefault() : courant;
        if (courant is JsonObject objet)
        {
            objet[chemin[^1]] = valeur;
        }
        else
        {
            throw new InvalidOperationException($"Surcharge « {string.Join('.', chemin)} » : propriété parente introuvable dans l'exemple.");
        }
    }

    private static JsonNode? Generer(JsonNode doc, JsonNode schema, int profondeur)
    {
        var resolu = ContratConsommateur.Resoudre(doc, schema);
        if (resolu is null || profondeur > 8)
        {
            return null;
        }

        if (resolu["enum"]?.AsArray() is { Count: > 0 } valeurs)
        {
            return valeurs[0]!.DeepClone();
        }

        var type = TypeNonNul(resolu);
        if (type == "object" || resolu["properties"] is not null)
        {
            var objet = new JsonObject();
            foreach (var (nom, propriete) in resolu["properties"]?.AsObject() ?? [])
            {
                objet[nom] = ContratConsommateur.EstNullable(propriete) ? null : Generer(doc, propriete!, profondeur + 1);
            }

            return objet;
        }

        return type switch
        {
            "array" => new JsonArray(resolu["items"] is { } elements ? Generer(doc, elements, profondeur + 1) : null),
            "integer" or "number" => JsonValue.Create(1),
            "boolean" => JsonValue.Create(false),
            _ => JsonValue.Create(resolu["format"]?.GetValue<string>() switch
            {
                "uuid" => GuidExemple,
                "date" => "2026-01-15",
                "date-time" => "2026-01-15T09:00:00+00:00",
                _ => "exemple",
            }),
        };
    }

    private static string? TypeNonNul(JsonNode schema) => schema["type"] switch
    {
        JsonArray types => types.Select(t => t?.ToString()).FirstOrDefault(t => t != "null"),
        { } type => type.ToString(),
        _ => null,
    };
}
