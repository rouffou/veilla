using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace Sepp.Testing.Contrats;

/// <summary>
/// Contrat OpenAPI d'un service ou d'un BFF (INT-01, ARC-30) : le document servi par l'hôte en mémoire est comparé à l'instantané
/// versionné <c>docs/api/openapi/&lt;nom&gt;.json</c>. Tout écart fait échouer le test ; les ruptures (opération, code de réponse,
/// paramètre ou propriété supprimés, paramètre ou propriété devenu obligatoire) sont distinguées des ajouts. Régénération :
/// <c>bash tools/regenerer-openapi.sh</c> (variable <c>SEPP_OPENAPI_REGENERER=1</c>).
/// </summary>
public static class ContratOpenApi
{
    public const string Categorie = "ContratOpenApi";
    public const string VariableRegeneration = "SEPP_OPENAPI_REGENERER";

    private const string ConnexionFactice = "Host=127.0.0.1;Port=1;Database=contrat;Username=contrat;Password=contrat;Timeout=1";

    private const string Consigne =
        "Si le changement est voulu, régénérer l'instantané puis le committer : bash tools/regenerer-openapi.sh " +
        $"(ou {VariableRegeneration}=1 dotnet test --solution <solution>.slnx --filter-trait \"Categorie={Categorie}\").";

    private static readonly JsonSerializerOptions Ecriture = new() { WriteIndented = true };
    private static readonly string[] Methodes = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    /// <summary>Démarre l'hôte, lit <c>/openapi/v1.json</c> et le confronte à l'instantané de <paramref name="nom"/>.</summary>
    /// <param name="nom">Nom du fichier d'instantané (sans extension), par ex. <c>referentiels</c>.</param>
    /// <param name="ct">Jeton d'annulation du test.</param>
    /// <param name="connexion">Nom de la chaîne de connexion PostgreSQL du service (factice, jamais ouverte) ; absent pour un BFF.</param>
    /// <param name="configurer">Réglages propres à l'hôte pour qu'il démarre sans dépendance externe.</param>
    public static async Task VerifierAsync<TProgram>(string nom, CancellationToken ct, string? connexion = null, Action<IWebHostBuilder>? configurer = null)
        where TProgram : class
    {
        await using var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(b =>
        {
            // Environnement « Development » : le dépôt y porte la configuration locale complète du service (clés de
            // chiffrement de développement, adaptateurs simulés, adresses des services en aval) ; l'hôte démarre ainsi
            // sans coffre ni base. Les valeurs ci-dessous, appliquées en dernier, rendent le document indépendant de la machine.
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, configuration) =>
            {
                var valeurs = new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = "https://idp.exemple.test/realms/veilla",
                    ["Authentication:Audience"] = "sepp-api",
                    ["Database:MigrateOnStartup"] = "false",
                };
                if (connexion is not null)
                {
                    valeurs[$"ConnectionStrings:{connexion}"] = ConnexionFactice;
                }

                configuration.AddInMemoryCollection(valeurs);
            });

            configurer?.Invoke(b);

            // Isolement : le document ne dépend d'aucun traitement de fond. Les services hébergés du service (outbox, planificateurs,
            // consommateurs) ne démarrent pas : ils ouvriraient un DbContext et figeraient, dans le modèle EF mis en cache par
            // processus, le chiffreur de cet hôte (clés de développement) au détriment des autres tests de l'assemblée.
            b.ConfigureTestServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService) && !EstServiceHebergeDuFramework(d)).ToList())
                {
                    services.Remove(d);
                }
            });
        });

        using var client = factory.CreateClient();
        var reponse = await client.GetAsync("/openapi/v1.json", ct);
        reponse.EnsureSuccessStatusCode();
        var courant = Normaliser(JsonNode.Parse(await reponse.Content.ReadAsStringAsync(ct))!);

        var chemin = CheminInstantane(nom);
        if (Environment.GetEnvironmentVariable(VariableRegeneration) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            await File.WriteAllTextAsync(chemin, courant.ToJsonString(Ecriture) + "\n", ct);
            return;
        }

        File.Exists(chemin).ShouldBeTrue($"Instantané OpenAPI absent : {chemin}. {Consigne}");
        var reference = Normaliser(JsonNode.Parse(await File.ReadAllTextAsync(chemin, ct))!);
        if (JsonNode.DeepEquals(reference, courant))
        {
            return;
        }

        var (ruptures, ajouts) = Comparer(reference, courant);
        var message = new StringBuilder($"Le contrat OpenAPI de « {nom} » diffère de l'instantané docs/api/openapi/{nom}.json.").AppendLine();
        if (ruptures.Count > 0)
        {
            message.AppendLine($"RUPTURES ({ruptures.Count}) : un changement incompatible exige une nouvelle version de l'API (ARC-30).");
            ruptures.ForEach(r => message.AppendLine($"  - {r}"));
        }

        if (ajouts.Count > 0)
        {
            message.AppendLine($"Ajouts et autres modifications ({ajouts.Count}) :");
            ajouts.ForEach(a => message.AppendLine($"  + {a}"));
        }

        message.AppendLine(Consigne);
        throw new ShouldAssertException(message.ToString());
    }

    private static bool EstServiceHebergeDuFramework(ServiceDescriptor d) =>
        d.ImplementationType?.Assembly.GetName().Name?.StartsWith("Microsoft.", StringComparison.Ordinal) == true;

    /// <summary>Compare deux documents normalisés ; retourne les ruptures et les autres écarts.</summary>
    public static (List<string> Ruptures, List<string> Ajouts) Comparer(JsonNode reference, JsonNode courant)
    {
        var ruptures = new List<string>();
        var ajouts = new List<string>();

        var chemins = reference["paths"]?.AsObject() ?? [];
        var cheminsCourants = courant["paths"]?.AsObject() ?? [];
        foreach (var (chemin, definition) in chemins)
        {
            if (!cheminsCourants.TryGetPropertyValue(chemin, out var definitionCourante))
            {
                ruptures.Add($"chemin supprimé : {chemin}");
                continue;
            }

            foreach (var methode in Methodes)
            {
                var operation = definition?[methode];
                var operationCourante = definitionCourante?[methode];
                var id = $"{methode.ToUpperInvariant()} {chemin}";
                if (operation is null)
                {
                    if (operationCourante is not null)
                    {
                        ajouts.Add($"opération ajoutée : {id}");
                    }

                    continue;
                }

                if (operationCourante is null)
                {
                    ruptures.Add($"opération supprimée : {id}");
                    continue;
                }

                CompareOperation(id, operation, operationCourante, ruptures, ajouts);
            }
        }

        foreach (var (chemin, definition) in cheminsCourants.Where(c => !chemins.ContainsKey(c.Key)))
        {
            foreach (var methode in Methodes.Where(m => definition?[m] is not null))
            {
                ajouts.Add($"opération ajoutée : {methode.ToUpperInvariant()} {chemin}");
            }
        }

        var schemas = reference["components"]?["schemas"]?.AsObject() ?? [];
        var schemasCourants = courant["components"]?["schemas"]?.AsObject() ?? [];
        foreach (var (nom, schema) in schemas)
        {
            if (!schemasCourants.TryGetPropertyValue(nom, out var schemaCourant))
            {
                ruptures.Add($"schéma supprimé : {nom}");
                continue;
            }

            CompareSchema($"schéma {nom}", schema, schemaCourant, ruptures, ajouts);
        }

        foreach (var (nom, _) in schemasCourants.Where(s => !schemas.ContainsKey(s.Key)))
        {
            ajouts.Add($"schéma ajouté : {nom}");
        }

        if (ruptures.Count == 0 && ajouts.Count == 0)
        {
            ajouts.Add("autres modifications du document (descriptions, sécurité, métadonnées) : voir le diff Git de l'instantané régénéré");
        }

        return (ruptures, ajouts);
    }

    /// <summary>Tri récursif des clés et retrait des éléments propres à la machine (adresses des serveurs).</summary>
    public static JsonNode Normaliser(JsonNode document)
    {
        var racine = Trier(document)!.AsObject();
        racine.Remove("servers");
        return racine;
    }

    private static void CompareOperation(string id, JsonNode operation, JsonNode courante, List<string> ruptures, List<string> ajouts)
    {
        var reponses = operation["responses"]?.AsObject() ?? [];
        var reponsesCourantes = courante["responses"]?.AsObject() ?? [];
        foreach (var (code, _) in reponses.Where(r => !reponsesCourantes.ContainsKey(r.Key)))
        {
            ruptures.Add($"{id} : code de réponse {code} supprimé");
        }

        foreach (var (code, _) in reponsesCourantes.Where(r => !reponses.ContainsKey(r.Key)))
        {
            ajouts.Add($"{id} : code de réponse {code} ajouté");
        }

        var parametres = Parametres(operation);
        var parametresCourants = Parametres(courante);
        foreach (var (cle, obligatoire) in parametres)
        {
            if (!parametresCourants.TryGetValue(cle, out var obligatoireCourant))
            {
                ruptures.Add($"{id} : paramètre {cle} supprimé");
            }
            else if (!obligatoire && obligatoireCourant)
            {
                ruptures.Add($"{id} : paramètre {cle} devenu obligatoire");
            }
        }

        foreach (var (cle, obligatoire) in parametresCourants.Where(p => !parametres.ContainsKey(p.Key)))
        {
            (obligatoire ? ruptures : ajouts).Add($"{id} : paramètre {cle} ajouté{(obligatoire ? " (obligatoire)" : string.Empty)}");
        }

        var corps = operation["requestBody"] is not null;
        var corpsCourant = courante["requestBody"] is not null;
        if (corps && !corpsCourant)
        {
            ruptures.Add($"{id} : corps de requête supprimé");
        }
        else if (!corps && corpsCourant)
        {
            (courante["requestBody"]?["required"]?.GetValue<bool>() == true ? ruptures : ajouts).Add($"{id} : corps de requête ajouté");
        }
    }

    private static Dictionary<string, bool> Parametres(JsonNode operation) =>
        (operation["parameters"]?.AsArray() ?? [])
            .Where(p => p is not null)
            .GroupBy(p => $"{p!["in"]}:{p["name"]}")
            .ToDictionary(g => g.Key, g => g.First()!["required"]?.GetValue<bool>() == true, StringComparer.Ordinal);

    private static void CompareSchema(string id, JsonNode? schema, JsonNode? courant, List<string> ruptures, List<string> ajouts)
    {
        var proprietes = schema?["properties"]?.AsObject() ?? [];
        var proprietesCourantes = courant?["properties"]?.AsObject() ?? [];
        var obligatoires = Obligatoires(schema);
        var obligatoiresCourantes = Obligatoires(courant);

        foreach (var (nom, _) in proprietes.Where(p => !proprietesCourantes.ContainsKey(p.Key)))
        {
            ruptures.Add($"{id} : propriété {(obligatoires.Contains(nom) ? "requise " : string.Empty)}{nom} supprimée");
        }

        foreach (var (nom, _) in proprietesCourantes.Where(p => !proprietes.ContainsKey(p.Key)))
        {
            var requise = obligatoiresCourantes.Contains(nom);
            (requise ? ruptures : ajouts).Add($"{id} : propriété {nom} ajoutée{(requise ? " (requise)" : string.Empty)}");
        }

        foreach (var nom in obligatoiresCourantes.Where(n => proprietes.ContainsKey(n) && !obligatoires.Contains(n)))
        {
            ruptures.Add($"{id} : propriété {nom} devenue requise");
        }

        foreach (var (nom, propriete) in proprietes)
        {
            if (proprietesCourantes.TryGetPropertyValue(nom, out var proprieteCourante)
                && propriete?["type"]?.ToJsonString() != proprieteCourante?["type"]?.ToJsonString())
            {
                ruptures.Add($"{id} : type de la propriété {nom} modifié");
            }
        }
    }

    private static HashSet<string> Obligatoires(JsonNode? schema) =>
        (schema?["required"]?.AsArray() ?? []).Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

    private static JsonNode? Trier(JsonNode? noeud) => noeud switch
    {
        JsonObject objet => new JsonObject(objet
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Trier(p.Value)))),
        JsonArray tableau => new JsonArray(tableau.Select(Trier).ToArray()),
        null => null,
        _ => noeud.DeepClone(),
    };

    private static string CheminInstantane(string nom)
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "Sepp.slnx")))
        {
            dossier = dossier.Parent;
        }

        dossier.ShouldNotBeNull("Racine du dépôt (Sepp.slnx) introuvable.");
        return Path.Combine(dossier.FullName, "docs", "api", "openapi", $"{nom}.json");
    }
}
