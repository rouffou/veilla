using System.Text.Json.Nodes;

using Sepp.Testing.Contrats;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// Le test de contrat consommateur-fournisseur (ARC-23) doit détecter ce qu'il prétend détecter : on le confronte à un
/// fournisseur fictif dont le contrat décrit, lui, ses réponses (aucun service réel ne le fait encore).
/// </summary>
public sealed class ContratConsommateurComparaisonTests
{
    private const string Fournisseur = """
        {
          "paths": {
            "/api/v1/documents": {
              "get": {
                "parameters": [
                  { "in": "query", "name": "typeDestinataire", "schema": { "$ref": "#/components/schemas/TypeDestinataire" } },
                  { "in": "query", "name": "destinataireId", "required": true, "schema": { "type": "string" } }
                ],
                "responses": { "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Document" } } } } } }
              },
              "post": {
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Demande" } } } },
                "responses": { "200": { "description": "OK" } }
              }
            },
            "/api/v1/documents/{id}": {
              "get": { "responses": { "200": { "description": "OK" } } }
            },
            "/api/v1/documents/modeles": {
              "get": { "responses": { "200": { "description": "OK" } } }
            },
            "/api/v1/actions": {
              "post": { "responses": { "200": { "description": "OK" } } }
            },
            "/api/v1/annulations": {
              "post": {
                "requestBody": { "content": { "application/json": { "schema": { "oneOf": [ { "type": "null" }, { "$ref": "#/components/schemas/Annulation" } ] } } } },
                "responses": { "200": { "description": "OK" } }
              }
            }
          },
          "components": {
            "schemas": {
              "TypeDestinataire": { "enum": ["personne", "affilie"] },
              "Document": {
                "type": "object",
                "properties": { "id": { "type": "string" }, "typeDestinataire": { "$ref": "#/components/schemas/TypeDestinataire" }, "statut": { "type": "string" } },
                "required": ["id", "typeDestinataire", "statut"]
              },
              "Demande": {
                "type": "object",
                "properties": { "modele": { "type": "string" }, "langue": { "type": ["null", "string"] } },
                "required": ["modele", "langue"]
              },
              "Annulation": { "type": "object", "properties": { "raison": { "type": ["null", "string"] } }, "required": ["raison"] }
            }
          }
        }
        """;

    private static readonly JsonNode Doc = JsonNode.Parse(Fournisseur)!;

    private static ResultatContrat Verifier(params AttenteConsommateur[] attentes) => ContratConsommateur.Verifier(attentes, _ => Doc);

    private static AttenteConsommateur Attente(string methode, string chemin) => new("test", "fournisseur", methode, chemin);

    [Fact]
    public void Une_attente_satisfaite_ne_produit_aucun_ecart()
    {
        var resultat = Verifier(
            Attente("GET", "/api/v1/documents") with { Query = ["typeDestinataire", "destinataireId"], ChampsReponse = ["id", "statut"] },
            Attente("POST", "/api/v1/documents") with { CorpsRequete = ["modele"] },
            Attente("GET", "/api/v1/documents/{id}"));

        resultat.Ecarts.ShouldBeEmpty();
    }

    [Fact]
    public void Une_operation_supprimee_du_fournisseur_est_detectee()
    {
        var resultat = Verifier(Attente("DELETE", "/api/v1/documents/{id}"), Attente("GET", "/api/v1/archives"));

        resultat.Ecarts.Count.ShouldBe(2);
        resultat.Ecarts.ShouldAllBe(e => e.Contains("opération absente", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_parametre_renomme_ou_devenu_obligatoire_est_detecte()
    {
        var resultat = Verifier(Attente("GET", "/api/v1/documents") with { Query = ["type"] });

        resultat.Ecarts.ShouldContain(e => e.Contains("paramètre de requête type inconnu", StringComparison.Ordinal));
        resultat.Ecarts.ShouldContain(e => e.Contains("exige le paramètre de requête destinataireId", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_champ_de_corps_inconnu_ou_obligatoire_non_envoye_est_detecte()
    {
        var resultat = Verifier(Attente("POST", "/api/v1/documents") with { CorpsRequete = ["nom"] });

        resultat.Ecarts.ShouldContain(e => e.Contains("champ de corps nom inconnu", StringComparison.Ordinal));
        resultat.Ecarts.ShouldContain(e => e.Contains("champ de corps obligatoire modele non envoyé", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_propriete_nullable_listee_required_n_est_pas_exigee()
    {
        // Le générateur OpenAPI de .NET place toutes les propriétés d'un record dans « required », nullables comprises.
        Verifier(Attente("POST", "/api/v1/documents") with { CorpsRequete = ["modele"] }).Ecarts.ShouldBeEmpty();
        Verifier(Attente("POST", "/api/v1/annulations") with { CorpsRequete = [] }).Ecarts.ShouldBeEmpty();
    }

    [Fact]
    public void Un_corps_envoye_a_une_operation_qui_n_en_accepte_pas_est_detecte_sauf_s_il_est_vide()
    {
        Verifier(Attente("POST", "/api/v1/actions") with { CorpsRequete = ["x"] }).Ecarts.ShouldNotBeEmpty();
        Verifier(Attente("POST", "/api/v1/actions") with { CorpsRequete = [] }).Ecarts.ShouldBeEmpty();
    }

    [Fact]
    public void Un_corps_obligatoire_non_envoye_est_detecte()
    {
        Verifier(Attente("POST", "/api/v1/documents")).Ecarts.ShouldContain(e => e.Contains("exige un corps", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_champ_de_reponse_supprime_est_detecte_quand_la_reponse_est_decrite()
    {
        var resultat = Verifier(Attente("GET", "/api/v1/documents") with { Query = ["typeDestinataire", "destinataireId"], ChampsReponse = ["id", "publieLe"] });

        resultat.Ecarts.ShouldHaveSingleItem().ShouldContain("champ de réponse publieLe absent");
    }

    [Fact]
    public void Les_champs_lus_d_une_reponse_non_decrite_sont_signales_non_verifiables()
    {
        var resultat = Verifier(Attente("GET", "/api/v1/documents/{id}") with { ChampsReponse = ["id"] });

        resultat.Ecarts.ShouldBeEmpty();
        resultat.NonVerifiables.ShouldHaveSingleItem();
    }

    [Fact]
    public void Un_code_de_succes_non_declare_est_detecte()
    {
        Verifier(Attente("GET", "/api/v1/documents/{id}") with { CodeSucces = "204" }).Ecarts.ShouldHaveSingleItem().ShouldContain("code de succès 204");
    }

    [Fact]
    public void Les_ecarts_sont_remontes_dans_l_echec()
    {
        var resultat = Verifier(Attente("GET", "/api/v1/archives"));

        Should.Throw<ShouldAssertException>(resultat.ExigerSansEcart).Message.ShouldContain("/api/v1/archives");
    }

    [Theory]
    [InlineData("personne", 0)]
    [InlineData("Personne", 1)]
    [InlineData("PERSONNE", 1)]
    public void Un_double_dont_l_enumeration_n_a_pas_la_casse_du_fournisseur_est_detecte(string valeur, int ecartsAttendus)
    {
        // Le bug #436 : le double renvoyait « Personne » alors que le service publie « personne ».
        var corps = $$"""[{ "id": "1", "typeDestinataire": "{{valeur}}", "statut": "Publie" }]""";

        var ecarts = ContratConsommateur.VerifierReponse(Doc, "fournisseur", "GET", "/api/v1/documents", 200, corps);

        ecarts.Count.ShouldBe(ecartsAttendus);
    }

    [Fact]
    public void Un_double_qui_invente_un_champ_ou_omet_un_champ_obligatoire_est_detecte()
    {
        var ecarts = ContratConsommateur.VerifierReponse(Doc, "fournisseur", "GET", "/api/v1/documents", 200, """[{ "id": "1", "inconnu": 1 }]""");

        ecarts.ShouldContain(e => e.Contains("$[0].inconnu inconnu", StringComparison.Ordinal));
        ecarts.ShouldContain(e => e.Contains("$[0].typeDestinataire obligatoire", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_double_sur_une_route_inconnue_est_detecte_mais_pas_une_reponse_d_erreur_sur_une_route_connue()
    {
        ContratConsommateur.VerifierReponse(Doc, "fournisseur", "GET", "/api/v1/archives", 200, "[]").ShouldHaveSingleItem();
        ContratConsommateur.VerifierReponse(Doc, "fournisseur", "GET", "/api/v1/documents", 404, """{ "code": "introuvable" }""").ShouldBeEmpty();
    }

    [Fact]
    public void Une_requete_emise_est_confrontee_a_la_route_aux_parametres_et_au_corps()
    {
        var guid = Guid.CreateVersion7();

        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "GET", $"/api/v1/documents/{guid}", null).ShouldBeEmpty();
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "GET", "/api/v1/documents?destinataireId=1&typeDestinataire=personne", null).ShouldBeEmpty();
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "GET", "/api/v1/documents?typeDestinataire=personne", null)
            .ShouldHaveSingleItem().ShouldContain("destinataireId absent");
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "GET", "/api/v1/documents?destinataireId=1&x=2", null)
            .ShouldHaveSingleItem().ShouldContain("paramètre de requête x inconnu");
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "POST", "/api/v1/documents", """{ "Modele": "m" }""").ShouldBeEmpty();
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "POST", "/api/v1/documents", """{ "nom": "m" }""").ShouldNotBeEmpty();
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "DELETE", "/api/v1/documents/1", null).ShouldHaveSingleItem();
    }

    [Fact]
    public void Le_modele_le_plus_litteral_l_emporte_sur_un_parametre_de_chemin()
    {
        ContratConsommateur.VerifierRequete(Doc, "fournisseur", "GET", "/api/v1/documents/modeles", null).ShouldBeEmpty();
    }

    [Fact]
    public void Les_instantanes_reels_sont_lisibles_par_le_test_de_contrat()
    {
        // Garde-fou sur la réalité : une attente invraisemblable est bien rejetée par un instantané versionné.
        var resultat = ContratConsommateur.Verifier(
        [
            new AttenteConsommateur("test", "documents", "GET", "/api/v1/documents") { Query = ["typeDestinataire", "parametreInexistant"] },
        ]);

        resultat.Ecarts.ShouldHaveSingleItem().ShouldContain("parametreInexistant");
    }
}
