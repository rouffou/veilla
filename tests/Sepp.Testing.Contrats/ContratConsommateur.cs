using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Shouldly;

namespace Sepp.Testing.Contrats;

/// <summary>
/// Attente d'un consommateur HTTP interne envers une opération d'un fournisseur (ARC-23, ARC-30) : ce que le consommateur
/// appelle (méthode, chemin, paramètres de requête, champs du corps) et ce qu'il lit dans la réponse. Les attentes sont
/// déclarées dans le projet de tests du consommateur et confrontées à l'instantané OpenAPI du fournisseur
/// (<c>docs/api/openapi/&lt;fournisseur&gt;.json</c>) par <see cref="ContratConsommateur"/>.
/// </summary>
/// <param name="Consommateur">Qui appelle (par ex. <c>bff-travailleur</c>), pour les messages d'écart.</param>
/// <param name="Fournisseur">Nom de l'instantané du fournisseur, par ex. <c>documents</c>.</param>
/// <param name="Methode">Méthode HTTP (GET, POST…).</param>
/// <param name="Chemin">Chemin modèle tel que dans l'instantané, par ex. <c>/api/v1/documents/{id}</c>.</param>
public sealed record AttenteConsommateur(string Consommateur, string Fournisseur, string Methode, string Chemin)
{
    /// <summary>Paramètres de requête (<c>?nom=</c>) que le consommateur envoie.</summary>
    public IReadOnlyList<string> Query { get; init; } = [];

    /// <summary>Champs (de premier niveau) du corps JSON envoyé ; <c>null</c> si le consommateur n'envoie pas de corps, vide pour un corps « {} ».</summary>
    public IReadOnlyList<string>? CorpsRequete { get; init; }

    /// <summary>Champs de la réponse que le consommateur lit (premier niveau, d'un objet ou des éléments d'un tableau).</summary>
    public IReadOnlyList<string> ChampsReponse { get; init; } = [];

    /// <summary>Code HTTP de succès attendu.</summary>
    public string CodeSucces { get; init; } = "200";

    internal string Id => $"{Consommateur} → {Fournisseur} {Methode.ToUpperInvariant()} {Chemin}";
}

/// <summary>Résultat d'une vérification : écarts (échec) et attentes que l'instantané ne permet pas encore de vérifier.</summary>
public sealed record ResultatContrat(IReadOnlyList<string> Ecarts, IReadOnlyList<string> NonVerifiables)
{
    public void ExigerSansEcart()
    {
        if (Ecarts.Count == 0)
        {
            return;
        }

        throw new ShouldAssertException(
            $"{Ecarts.Count} écart(s) entre les attentes du consommateur et le contrat du fournisseur (instantané docs/api/openapi) :{Environment.NewLine}"
            + string.Join(Environment.NewLine, Ecarts.Select(e => $"  - {e}"))
            + $"{Environment.NewLine}Corriger le consommateur, ou le fournisseur si l'évolution est voulue (puis régénérer l'instantané : bash tools/regenerer-openapi.sh).");
    }
}

/// <summary>
/// Test de contrat consommateur-fournisseur sans dépendance supplémentaire : l'instantané OpenAPI du fournisseur
/// (INT-01, #430) tient lieu de contrat. Trois usages :
/// <list type="bullet">
/// <item><see cref="Verifier(IEnumerable{AttenteConsommateur})"/> : les attentes déclarées par un consommateur sont satisfaites par le
/// fournisseur (opération, paramètres, corps, code de succès, champs lus) ; une évolution du fournisseur qui casse un consommateur échoue.</item>
/// <item><see cref="VerifierRequete(string, string, string, string?)"/> : une requête réellement émise par le code du consommateur
/// (capturée par un double de test) correspond à une opération de l'instantané.</item>
/// <item><see cref="VerifierReponse(string, string, string, int, string)"/> : une réponse programmée dans un double de test est possible
/// chez le fournisseur (route existante ; si l'instantané décrit la réponse : propriétés, énumérations à la casse exacte). C'est le
/// garde-fou du bug #436, où le double renvoyait « Personne » alors que le service publie « personne ».</item>
/// </list>
/// Limite connue : tant qu'un service ne déclare pas le type de ses réponses (<c>Produces&lt;T&gt;</c>), l'instantané ne décrit
/// pas le corps de réponse ; les champs lus sont alors signalés « non vérifiables » (voir <c>docs/architecture/strategie-tests.md</c>).
/// </summary>
public static class ContratConsommateur
{
    private static readonly ConcurrentDictionary<string, JsonNode> Instantanes = new(StringComparer.Ordinal);

    /// <summary>Confronte les attentes aux instantanés des fournisseurs.</summary>
    public static ResultatContrat Verifier(IEnumerable<AttenteConsommateur> attentes) => Verifier(attentes, Instantane);

    /// <summary>Variante testable : <paramref name="instantane"/> fournit le document d'un fournisseur.</summary>
    public static ResultatContrat Verifier(IEnumerable<AttenteConsommateur> attentes, Func<string, JsonNode> instantane)
    {
        ArgumentNullException.ThrowIfNull(attentes);
        ArgumentNullException.ThrowIfNull(instantane);
        var ecarts = new List<string>();
        var nonVerifiables = new List<string>();
        foreach (var attente in attentes)
        {
            var doc = instantane(attente.Fournisseur);
            var operation = TrouverOperation(doc, attente.Methode, attente.Chemin);
            if (operation is null)
            {
                ecarts.Add($"{attente.Id} : opération absente du fournisseur");
                continue;
            }

            VerifierParametres(attente, operation, ecarts);
            VerifierCorps(attente, doc, operation, ecarts);
            VerifierReponseAttendue(attente, doc, operation, ecarts, nonVerifiables);
        }

        return new ResultatContrat(ecarts, nonVerifiables);
    }

    /// <summary>
    /// Écarts d'une requête émise (<paramref name="cheminEtQuery"/> : chemin réel et chaîne de requête, <paramref name="corpsJson"/> :
    /// corps ou <c>null</c>) par rapport à l'instantané de <paramref name="fournisseur"/>.
    /// </summary>
    public static IReadOnlyList<string> VerifierRequete(string fournisseur, string methode, string cheminEtQuery, string? corpsJson) =>
        VerifierRequete(Instantane(fournisseur), fournisseur, methode, cheminEtQuery, corpsJson);

    public static IReadOnlyList<string> VerifierRequete(JsonNode doc, string fournisseur, string methode, string cheminEtQuery, string? corpsJson)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(cheminEtQuery);
        var (chemin, query) = Decouper(cheminEtQuery);
        var id = $"requête {methode.ToUpperInvariant()} {chemin} vers {fournisseur}";
        var operation = TrouverOperation(doc, methode, chemin);
        if (operation is null)
        {
            return [$"{id} : aucune opération correspondante dans l'instantané"];
        }

        var ecarts = new List<string>();
        var declares = Parametres(operation, "query");
        foreach (var nom in query.Where(n => !declares.ContainsKey(n)))
        {
            ecarts.Add($"{id} : paramètre de requête {nom} inconnu du fournisseur");
        }

        foreach (var (nom, _) in declares.Where(p => p.Value && !query.Contains(p.Key)))
        {
            ecarts.Add($"{id} : paramètre de requête obligatoire {nom} absent");
        }

        var corps = operation["requestBody"];
        if (corpsJson is not null && corps is null && corpsJson.Trim() != "{}")
        {
            ecarts.Add($"{id} : corps envoyé alors que le fournisseur n'en attend pas");
        }
        else if (corpsJson is null && corps?["required"]?.GetValue<bool>() == true)
        {
            ecarts.Add($"{id} : corps obligatoire absent");
        }
        else if (corpsJson is not null && SchemaDuCorps(doc, corps) is { } schema)
        {
            using var json = JsonDocument.Parse(corpsJson);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                ComparerChamps(id, doc, schema, json.RootElement.EnumerateObject().Select(p => p.Name).ToList(), ecarts, ignorerCasse: true);
            }
        }

        return ecarts;
    }

    /// <summary>Écarts d'une réponse programmée dans un double de test par rapport à l'instantané du fournisseur.</summary>
    public static IReadOnlyList<string> VerifierReponse(string fournisseur, string methode, string chemin, int status, string corpsJson) =>
        VerifierReponse(Instantane(fournisseur), fournisseur, methode, chemin, status, corpsJson);

    public static IReadOnlyList<string> VerifierReponse(JsonNode doc, string fournisseur, string methode, string chemin, int status, string corpsJson)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(corpsJson);
        var id = $"double {methode.ToUpperInvariant()} {chemin} ({fournisseur})";
        var operation = TrouverOperation(doc, methode, chemin);
        if (operation is null)
        {
            return [$"{id} : route inconnue du fournisseur (le double invente une opération)"];
        }

        if (status >= 400 || SchemaDeReponse(doc, operation, status) is not { } schema)
        {
            return [];
        }

        var ecarts = new List<string>();
        using var json = JsonDocument.Parse(corpsJson);
        ValiderValeur(id, doc, schema, json.RootElement, "$", ecarts);
        return ecarts;
    }

    /// <summary>Document d'instantané d'un fournisseur (mis en cache).</summary>
    public static JsonNode Instantane(string fournisseur) => Instantanes.GetOrAdd(fournisseur, nom =>
    {
        var chemin = ContratOpenApi.CheminInstantane(nom);
        File.Exists(chemin).ShouldBeTrue($"Instantané OpenAPI du fournisseur « {nom} » absent : {chemin}");
        return JsonNode.Parse(File.ReadAllText(chemin))!;
    });

    private static void VerifierParametres(AttenteConsommateur attente, JsonNode operation, List<string> ecarts)
    {
        var declares = Parametres(operation, "query");
        foreach (var nom in attente.Query.Where(n => !declares.ContainsKey(n)))
        {
            ecarts.Add($"{attente.Id} : paramètre de requête {nom} inconnu du fournisseur (supprimé ou renommé ?)");
        }

        foreach (var (nom, _) in declares.Where(p => p.Value && !attente.Query.Contains(p.Key)))
        {
            ecarts.Add($"{attente.Id} : le fournisseur exige le paramètre de requête {nom}, que le consommateur n'envoie pas");
        }
    }

    private static void VerifierCorps(AttenteConsommateur attente, JsonNode doc, JsonNode operation, List<string> ecarts)
    {
        var corps = operation["requestBody"];
        if (attente.CorpsRequete is null)
        {
            if (corps?["required"]?.GetValue<bool>() == true)
            {
                ecarts.Add($"{attente.Id} : le fournisseur exige un corps que le consommateur n'envoie pas");
            }

            return;
        }

        if (corps is null)
        {
            // Un corps vide « {} » est ignoré par un fournisseur qui n'en attend pas (POST d'action sans paramètre).
            if (attente.CorpsRequete.Count > 0)
            {
                ecarts.Add($"{attente.Id} : le consommateur envoie un corps que le fournisseur n'accepte pas");
            }

            return;
        }

        if (SchemaDuCorps(doc, corps) is { } schema)
        {
            ComparerChamps(attente.Id, doc, schema, [.. attente.CorpsRequete], ecarts, ignorerCasse: false);
        }
    }

    private static void VerifierReponseAttendue(AttenteConsommateur attente, JsonNode doc, JsonNode operation, List<string> ecarts, List<string> nonVerifiables)
    {
        var reponses = operation["responses"]?.AsObject() ?? [];
        if (!reponses.ContainsKey(attente.CodeSucces))
        {
            ecarts.Add($"{attente.Id} : code de succès {attente.CodeSucces} non déclaré par le fournisseur (déclarés : {string.Join(", ", reponses.Select(r => r.Key))})");
            return;
        }

        if (attente.ChampsReponse.Count == 0)
        {
            return;
        }

        var statut = int.Parse(attente.CodeSucces, CultureInfo.InvariantCulture);
        if (SchemaDeReponse(doc, operation, statut) is not { } schema)
        {
            nonVerifiables.Add($"{attente.Id} : champs lus ({string.Join(", ", attente.ChampsReponse)}) non vérifiables, réponse non typée chez le fournisseur");
            return;
        }

        if (schema["type"]?.GetValue<string>() == "array" && Resoudre(doc, schema["items"]) is { } element)
        {
            schema = element;
        }

        var proprietes = schema["properties"]?.AsObject() ?? [];
        foreach (var champ in attente.ChampsReponse.Where(c => !proprietes.ContainsKey(c)))
        {
            ecarts.Add($"{attente.Id} : champ de réponse {champ} absent du contrat du fournisseur (supprimé ou renommé ?)");
        }
    }

    private static void ComparerChamps(string id, JsonNode doc, JsonNode schema, List<string> champs, List<string> ecarts, bool ignorerCasse)
    {
        var resolu = Resoudre(doc, schema)!;
        var comparateur = ignorerCasse ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var proprietes = (resolu["properties"]?.AsObject() ?? []).Select(p => p.Key).ToHashSet(comparateur);
        // Le générateur OpenAPI de .NET liste toutes les propriétés d'un record comme « required », y compris les nullables,
        // que la désérialisation accepte pourtant absentes : seules les propriétés non nullables sont exigées.
        var obligatoires = (resolu["required"]?.AsArray() ?? [])
            .Select(n => n!.GetValue<string>())
            .Where(n => !EstNullable(resolu["properties"]?[n]))
            .ToList();
        var envoyes = champs.ToHashSet(comparateur);
        foreach (var champ in champs.Where(c => !proprietes.Contains(c)))
        {
            ecarts.Add($"{id} : champ de corps {champ} inconnu du fournisseur (supprimé ou renommé ?)");
        }

        foreach (var champ in obligatoires.Where(c => !envoyes.Contains(c)))
        {
            ecarts.Add($"{id} : champ de corps obligatoire {champ} non envoyé");
        }
    }

    /// <summary>Validation d'une valeur JSON contre un schéma : propriétés connues et obligatoires, énumérations à la casse exacte.</summary>
    private static void ValiderValeur(string id, JsonNode doc, JsonNode? schema, JsonElement valeur, string chemin, List<string> ecarts)
    {
        var resolu = Resoudre(doc, schema);
        if (resolu is null)
        {
            return;
        }

        if (resolu["enum"]?.AsArray() is { } valeurs && valeur.ValueKind == JsonValueKind.String)
        {
            var attendues = valeurs.Select(v => v?.ToString()).ToList();
            if (!attendues.Contains(valeur.GetString(), StringComparer.Ordinal))
            {
                ecarts.Add($"{id} : {chemin} = « {valeur.GetString()} » hors énumération du fournisseur ({string.Join(", ", attendues)}) ; la casse compte");
            }

            return;
        }

        switch (valeur.ValueKind)
        {
            case JsonValueKind.Array:
                var index = 0;
                foreach (var element in valeur.EnumerateArray())
                {
                    ValiderValeur(id, doc, resolu["items"], element, $"{chemin}[{index++}]", ecarts);
                }

                break;
            case JsonValueKind.Object when resolu["properties"] is JsonObject proprietes:
                var presentes = valeur.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var nom in presentes.Where(n => !proprietes.ContainsKey(n)))
                {
                    ecarts.Add($"{id} : {chemin}.{nom} inconnu du fournisseur (le double invente un champ)");
                }

                foreach (var requis in (resolu["required"]?.AsArray() ?? []).Select(n => n!.GetValue<string>()).Where(n => !presentes.Contains(n)))
                {
                    ecarts.Add($"{id} : {chemin}.{requis} obligatoire chez le fournisseur mais absent du double");
                }

                foreach (var propriete in valeur.EnumerateObject().Where(p => proprietes.ContainsKey(p.Name)))
                {
                    ValiderValeur(id, doc, proprietes[propriete.Name], propriete.Value, $"{chemin}.{propriete.Name}", ecarts);
                }

                break;
            default:
                break;
        }
    }

    private static bool EstNullable(JsonNode? propriete) =>
        propriete?["type"] is JsonArray types && types.Any(t => t?.ToString() == "null");

    private static JsonNode? SchemaDuCorps(JsonNode doc, JsonNode? corps) =>
        Resoudre(doc, corps?["content"]?["application/json"]?["schema"]);

    /// <summary>Schéma de la réponse JSON de code <paramref name="statut"/> (ou, à défaut, de la première réponse 2xx typée).</summary>
    private static JsonNode? SchemaDeReponse(JsonNode doc, JsonNode operation, int statut)
    {
        var reponses = operation["responses"]?.AsObject() ?? [];
        var reponse = reponses.TryGetPropertyValue(statut.ToString(CultureInfo.InvariantCulture), out var exacte)
            ? exacte
            : reponses.FirstOrDefault(r => r.Key.StartsWith('2')).Value;
        var contenu = reponse?["content"]?.AsObject();
        var json = contenu?.FirstOrDefault(c => c.Key.Contains("json", StringComparison.OrdinalIgnoreCase)).Value;
        return Resoudre(doc, json?["schema"]);
    }

    private static JsonNode? Resoudre(JsonNode doc, JsonNode? noeud)
    {
        const string prefixe = "#/components/schemas/";
        for (var garde = 0; garde < 16 && noeud?["$ref"]?.GetValue<string>() is { } reference; garde++)
        {
            noeud = reference.StartsWith(prefixe, StringComparison.Ordinal) ? doc["components"]?["schemas"]?[reference[prefixe.Length..]] : null;
        }

        // « Corps facultatif » : oneOf [null, X] (ou anyOf) se réduit à X.
        foreach (var variante in new[] { "oneOf", "anyOf" })
        {
            if (noeud?[variante]?.AsArray() is { } choix
                && choix.Where(c => c?["type"]?.ToString() != "null").ToList() is [{ } unique])
            {
                return Resoudre(doc, unique);
            }
        }

        return noeud;
    }

    private static Dictionary<string, bool> Parametres(JsonNode operation, string emplacement) =>
        (operation["parameters"]?.AsArray() ?? [])
            .Where(p => p?["in"]?.GetValue<string>() == emplacement)
            .ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!["required"]?.GetValue<bool>() == true, StringComparer.Ordinal);

    private static (string Chemin, HashSet<string> Query) Decouper(string cheminEtQuery)
    {
        var separateur = cheminEtQuery.IndexOf('?', StringComparison.Ordinal);
        if (separateur < 0)
        {
            return (cheminEtQuery, []);
        }

        var noms = cheminEtQuery[(separateur + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Uri.UnescapeDataString(p.Split('=')[0]))
            .ToHashSet(StringComparer.Ordinal);
        return (cheminEtQuery[..separateur], noms);
    }

    /// <summary>Opération correspondant à <paramref name="chemin"/> (réel ou modèle) : égalité exacte d'abord, sinon le modèle le plus littéral.</summary>
    private static JsonNode? TrouverOperation(JsonNode doc, string methode, string chemin)
    {
        var paths = doc["paths"]?.AsObject() ?? [];
        var verbe = methode.ToLowerInvariant();
        if (paths.TryGetPropertyValue(chemin, out var exact) && exact?[verbe] is { } direct)
        {
            return direct;
        }

        var segments = chemin.Trim('/').Split('/');
        return paths
            .Select(p => (Modele: p.Key.Trim('/').Split('/'), Operation: p.Value?[verbe]))
            .Where(c => c.Operation is not null && c.Modele.Length == segments.Length
                        && c.Modele.Zip(segments).All(z => z.First == z.Second || (z.First.StartsWith('{') && z.First.EndsWith('}'))))
            .OrderByDescending(c => c.Modele.Count(s => !s.StartsWith('{')))
            .Select(c => c.Operation)
            .FirstOrDefault();
    }
}
