using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Domain.Affilies;

namespace Sepp.Affilies.Application.Historique;

/// <summary>
/// AFF-05 — Instantané JSON de la fiche et calcul des valeurs « avant / après » : seules les parties modifiées
/// sont conservées. Les listes d'éléments identifiés (unités, sites, contacts…) sont comparées par identifiant.
/// </summary>
public static class Instantane
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>État de l'affilié hors identifiant et version (portés par l'entrée d'historique).</summary>
    public static JsonObject De(Affilie affilie)
    {
        var json = JsonSerializer.SerializeToNode(affilie.ToDto(), Options)!.AsObject();
        json.Remove("id");
        json.Remove("version");
        return json;
    }

    /// <summary>Différences entre deux instantanés ; <c>null</c> si rien n'a changé.</summary>
    public static (JsonNode? Avant, JsonNode? Apres)? Difference(JsonNode? avant, JsonNode? apres)
    {
        if (JsonNode.DeepEquals(avant, apres))
        {
            return null;
        }

        if (avant is JsonObject objetAvant && apres is JsonObject objetApres)
        {
            return DifferenceObjets(objetAvant, objetApres);
        }

        if (avant is JsonArray listeAvant && apres is JsonArray listeApres && Identifiables(listeAvant) && Identifiables(listeApres))
        {
            return DifferenceListes(listeAvant, listeApres);
        }

        return (avant?.DeepClone(), apres?.DeepClone());
    }

    private static (JsonNode?, JsonNode?) DifferenceObjets(JsonObject avant, JsonObject apres)
    {
        var resultatAvant = new JsonObject();
        var resultatApres = new JsonObject();
        if (avant["id"] is { } id)
        {
            resultatAvant["id"] = id.DeepClone();
            resultatApres["id"] = id.DeepClone();
        }

        foreach (var cle in avant.Select(p => p.Key).Union(apres.Select(p => p.Key)))
        {
            if (Difference(avant[cle], apres[cle]) is { } difference)
            {
                resultatAvant[cle] = difference.Avant;
                resultatApres[cle] = difference.Apres;
            }
        }

        return (resultatAvant, resultatApres);
    }

    private static (JsonNode?, JsonNode?) DifferenceListes(JsonArray avant, JsonArray apres)
    {
        var parIdAvant = avant.ToDictionary(Id);
        var parIdApres = apres.ToDictionary(Id);
        var resultatAvant = new JsonArray();
        var resultatApres = new JsonArray();
        foreach (var id in parIdAvant.Keys.Concat(parIdApres.Keys.Where(k => !parIdAvant.ContainsKey(k))))
        {
            parIdAvant.TryGetValue(id, out var elementAvant);
            parIdApres.TryGetValue(id, out var elementApres);
            if (elementAvant is null)
            {
                resultatApres.Add(elementApres!.DeepClone());
            }
            else if (elementApres is null)
            {
                resultatAvant.Add(elementAvant.DeepClone());
            }
            else if (Difference(elementAvant, elementApres) is { } difference)
            {
                resultatAvant.Add(difference.Avant);
                resultatApres.Add(difference.Apres);
            }
        }

        return (resultatAvant, resultatApres);
    }

    private static bool Identifiables(JsonArray liste) =>
        liste.All(e => e is JsonObject o && o["id"] is JsonValue);

    private static string Id(JsonNode? element) => element!["id"]!.GetValue<string>();
}
