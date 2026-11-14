using System.Text.Json.Nodes;

using Sepp.Testing.Contrats;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Détection des ruptures du contrat OpenAPI (INT-01, ARC-30) : ruptures distinguées des ajouts.</summary>
public sealed class ContratOpenApiComparaisonTests
{
    private const string Reference = """
        {
          "paths": {
            "/a": { "get": { "parameters": [ { "in": "query", "name": "q", "required": false } ], "responses": { "200": {}, "404": {} } },
                    "delete": { "responses": { "204": {} } } }
          },
          "components": { "schemas": { "Dto": { "type": "object", "required": [ "id" ], "properties": { "id": { "type": "string" }, "nom": { "type": "string" } } } } }
        }
        """;

    private static (List<string> Ruptures, List<string> Ajouts) Comparer(string courant) =>
        ContratOpenApi.Comparer(JsonNode.Parse(Reference)!, JsonNode.Parse(courant)!);

    [Fact]
    public void Une_operation_un_code_de_reponse_et_une_propriete_requise_supprimes_sont_des_ruptures()
    {
        var (ruptures, ajouts) = Comparer("""
            { "paths": { "/a": { "get": { "parameters": [ { "in": "query", "name": "q", "required": false } ], "responses": { "200": {} } } } },
              "components": { "schemas": { "Dto": { "type": "object", "properties": { "nom": { "type": "string" } } } } } }
            """);

        ruptures.ShouldContain("opération supprimée : DELETE /a");
        ruptures.ShouldContain("GET /a : code de réponse 404 supprimé");
        ruptures.ShouldContain("schéma Dto : propriété requise id supprimée");
        ajouts.ShouldBeEmpty();
    }

    [Fact]
    public void Les_ajouts_compatibles_ne_sont_pas_des_ruptures()
    {
        var (ruptures, ajouts) = Comparer("""
            { "paths": { "/a": { "get": { "parameters": [ { "in": "query", "name": "q", "required": false } ], "responses": { "200": {}, "404": {}, "409": {} } },
                                 "delete": { "responses": { "204": {} } } },
                         "/b": { "get": { "responses": { "200": {} } } } },
              "components": { "schemas": { "Dto": { "type": "object", "required": [ "id" ], "properties": { "id": { "type": "string" }, "nom": { "type": "string" }, "age": { "type": "integer" } } } } } }
            """);

        ruptures.ShouldBeEmpty();
        ajouts.ShouldContain("opération ajoutée : GET /b");
        ajouts.ShouldContain("GET /a : code de réponse 409 ajouté");
        ajouts.ShouldContain("schéma Dto : propriété age ajoutée");
    }

    [Fact]
    public void Un_parametre_devenu_obligatoire_est_une_rupture()
    {
        var (ruptures, _) = Comparer("""
            { "paths": { "/a": { "get": { "parameters": [ { "in": "query", "name": "q", "required": true } ], "responses": { "200": {}, "404": {} } },
                                 "delete": { "responses": { "204": {} } } } },
              "components": { "schemas": { "Dto": { "type": "object", "required": [ "id" ], "properties": { "id": { "type": "string" }, "nom": { "type": "string" } } } } } }
            """);

        ruptures.ShouldContain("GET /a : paramètre query:q devenu obligatoire");
    }
}
