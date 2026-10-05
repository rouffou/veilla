using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Sepp.Bff.Employeur.Integration.Tests;

/// <summary>
/// BFF complet (pipeline ASP.NET Core, validation réelle du jeton JWT, clients typés, résilience) face à des
/// services aval simulés au niveau HTTP : bornage par affilie_id, propagation du jeton, traduction des erreurs.
/// </summary>
public sealed class BffEmployeurApiTests : IAsyncLifetime
{
    private const string Emetteur = "https://idp.test/realms/veilla";

    private static readonly SymmetricSecurityKey Cle = new(RandomNumberGenerator.GetBytes(32));

    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly Guid _autreAffilieDuJeton = Guid.CreateVersion7();
    private readonly ServicesAvalSimules _aval = new();
    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ServicesAval:Affilies", $"http://{ServicesAvalSimules.Affilies}");
            b.UseSetting("ServicesAval:Personnes", $"http://{ServicesAvalSimules.Personnes}");
            b.UseSetting("ServicesAval:PostesRisques", $"http://{ServicesAvalSimules.PostesRisques}");
            b.UseSetting("Cors:Origines:0", "http://localhost:4201");
            b.ConfigureTestServices(s =>
            {
                // Jetons signés par une clé de test : même validation (émetteur, audience sepp-api, signature, expiration).
                s.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.Authority = null;
                    o.Configuration = new OpenIdConnectConfiguration { Issuer = Emetteur };
                    o.TokenValidationParameters.ValidIssuer = Emetteur;
                    o.TokenValidationParameters.IssuerSigningKey = Cle;
                });
                s.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = _aval));
                s.PostConfigureAll<HttpStandardResilienceOptions>(o =>
                {
                    o.Retry.Delay = TimeSpan.Zero;
                    o.Retry.MaxRetryAttempts = 1;
                });
            });
        });
        _ = _factory.Server;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static string Jeton(string role, IEnumerable<Guid> affilies, string audience = "sepp-api") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emetteur,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "employeur-1",
                ["roles"] = new[] { role },
                ["affilie_id"] = affilies.Select(a => a.ToString()).ToArray(),
            },
            SigningCredentials = new SigningCredentials(Cle, SecurityAlgorithms.HmacSha256),
        });

    private HttpClient Client(string? jeton = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton ?? Jeton("employeur", [_affilie, _autreAffilieDuJeton]));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode attendu = HttpStatusCode.OK)
    {
        var texte = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(attendu, texte);
        return JsonDocument.Parse(texte).RootElement;
    }

    private void AffilieConnu(Guid id, string denomination) =>
        _aval.Repondre(ServicesAvalSimules.Affilies, $"/api/v1/affilies/{id}", new
        {
            id,
            version = 1,
            fiche = new
            {
                numeroBce = "0123.456.749",
                denomination,
                formeJuridique = "SRL",
                codeNace = "62010",
                commissionParitaire = "200",
                categorieTarifaire = "B",
                dateAffiliation = "2020-01-01",
                dateFin = (string?)null,
                langue = "Fr",
                regimeLinguistique = "Francophone",
                statut = "Actif",
                groupeId = (Guid?)null,
            },
            unitesEtablissement = Array.Empty<object>(),
            contacts = Array.Empty<object>(),
            organesConcertation = Array.Empty<object>(),
            operations = Array.Empty<object>(),
        });

    private void Travailleurs(params (string Nom, string Prenom)[] travailleurs) =>
        _aval.Repondre(ServicesAvalSimules.Personnes, $"/api/v1/affilies/{_affilie}/travailleurs",
            travailleurs.Select(t => new { id = Guid.CreateVersion7(), nissMasque = "90.01.01-***.**", nom = t.Nom, prenom = t.Prenom, dateNaissance = "1990-01-01" }));

    private object Poste(Guid id, string intitule, Guid? affilie = null, params string[] risques) => new
    {
        id,
        affilieId = affilie ?? _affilie,
        intitule,
        description = (string?)null,
        metierTypeCode = (string?)null,
        statut = "Actif",
        risques = risques.Select(r => new
        {
            risqueId = Guid.CreateVersion7(),
            risqueCode = r,
            niveauExposition = "Eleve",
            valideParCpmtId = "cpmt-1",
            dateAvisCppt = "2026-01-10",
            documentAvisCpptId = Guid.CreateVersion7(),
            propositionId = Guid.CreateVersion7(),
            valideDu = "2026-01-15",
            valideJusquAu = (string?)null,
        }),
    };

    [Fact]
    public async Task Sans_jeton_le_BFF_repond_401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/affilies", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_jeton_d_une_autre_audience_est_refuse()
    {
        var response = await Client(Jeton("employeur", [_affilie], audience: "autre-api")).GetAsync("/api/v1/affilies", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_profil_interne_n_utilise_pas_le_BFF_employeur()
    {
        var response = await Client(Jeton("cpmt", [])).GetAsync("/api/v1/affilies", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Les_affilies_du_jeton_sont_lus_avec_le_jeton_et_la_correlation_de_l_utilisateur()
    {
        AffilieConnu(_affilie, "Boulangerie Dupont");
        AffilieConnu(_autreAffilieDuJeton, "Atelier Lambert");
        var jeton = Jeton("sipp", [_affilie, _autreAffilieDuJeton]);
        using var client = Client(jeton);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-portail-42");

        var json = await Json(await client.GetAsync("/api/v1/affilies", Ct));

        json.GetProperty("affilies").EnumerateArray().Select(a => a.GetProperty("denomination").GetString())
            .ShouldBe(["Atelier Lambert", "Boulangerie Dupont"]);
        _aval.Requetes.Count.ShouldBe(2);
        _aval.Requetes.ShouldAllBe(r => r.Authorization == $"Bearer {jeton}" && r.Correlation == "corr-portail-42");
    }

    [Fact]
    public async Task Un_affilie_hors_du_jeton_est_refuse_sans_appel_aval()
    {
        var response = await Client().GetAsync($"/api/v1/affilies/{Guid.CreateVersion7()}/travailleurs", Ct);

        var probleme = await Json(response, HttpStatusCode.Forbidden);
        probleme.GetProperty("code").GetString().ShouldBe("perimetre.interdit");
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Les_travailleurs_sont_pagines_et_le_NISS_n_est_jamais_relaye()
    {
        Travailleurs(("Dupont", "Jean"), ("Martin", "Léa"), ("Lefèvre", "Hélène"));

        var response = await Client().GetAsync($"/api/v1/affilies/{_affilie}/travailleurs?recherche=lea&page=1&taille=10", Ct);
        var texte = await response.Content.ReadAsStringAsync(Ct);
        var json = await Json(response);

        json.GetProperty("total").GetInt32().ShouldBe(1);
        json.GetProperty("elements")[0].GetProperty("nom").GetString().ShouldBe("Martin");
        texte.ShouldNotContain("niss", Case.Insensitive);
    }

    [Fact]
    public async Task Le_tableau_de_bord_compte_ce_qui_existe_et_annonce_le_reste_comme_a_venir()
    {
        Travailleurs(("Dupont", "Jean"), ("Martin", "Léa"));
        _aval.Repondre(ServicesAvalSimules.PostesRisques, "/api/v1/postes", new[] { Poste(Guid.CreateVersion7(), "Soudeur", null, "BRUIT"), Poste(Guid.CreateVersion7(), "Comptable") });
        _aval.Repondre(ServicesAvalSimules.PostesRisques, "/api/v1/propositions-poste-risque", Array.Empty<object>());
        _aval.Repondre(ServicesAvalSimules.PostesRisques, "/api/v1/propositions-liste-nominative", Array.Empty<object>());

        var json = await Json(await Client().GetAsync($"/api/v1/affilies/{_affilie}/tableau-de-bord", Ct));

        var indicateurs = json.GetProperty("indicateurs").EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!);
        indicateurs["travailleurs"].GetProperty("valeur").GetInt32().ShouldBe(2);
        indicateurs["postesActifs"].GetProperty("valeur").GetInt32().ShouldBe(2);
        indicateurs["postesExposes"].GetProperty("valeur").GetInt32().ShouldBe(1);
        indicateurs["examensDus"].GetProperty("disponible").GetBoolean().ShouldBeFalse();
        indicateurs["examensDus"].GetProperty("valeur").ValueKind.ShouldBe(JsonValueKind.Null);
        indicateurs["soldeUnites"].GetProperty("raison").GetString().ShouldBe("fonctionnalite-a-venir");
        _aval.Requetes.Where(r => r.Hote == ServicesAvalSimules.PostesRisques).ShouldAllBe(r => r.Chemin.Contains($"affilieId={_affilie}"));
    }

    [Fact]
    public async Task Un_service_aval_injoignable_donne_un_503_explicite()
    {
        _aval.Injoignable(ServicesAvalSimules.PostesRisques);

        var probleme = await Json(await Client().GetAsync($"/api/v1/affilies/{_affilie}/postes", Ct), HttpStatusCode.ServiceUnavailable);

        probleme.GetProperty("code").GetString().ShouldBe("service-aval.indisponible");
        probleme.GetProperty("service").GetString().ShouldBe("postes-risques");
    }

    [Fact]
    public async Task Un_refus_du_service_aval_est_relaye_avec_son_code()
    {
        _aval.Probleme(ServicesAvalSimules.Personnes, $"/api/v1/affilies/{_affilie}/travailleurs", HttpStatusCode.Forbidden, "perimetre.interdit");

        var probleme = await Json(await Client().GetAsync($"/api/v1/affilies/{_affilie}/travailleurs", Ct), HttpStatusCode.Forbidden);

        probleme.GetProperty("code").GetString().ShouldBe("perimetre.interdit");
        probleme.GetProperty("detail").GetString().ShouldBe("Détail perimetre.interdit");
    }

    [Fact]
    public async Task Une_erreur_interne_du_service_aval_devient_un_502_sans_detail_technique()
    {
        _aval.Repondre(ServicesAvalSimules.Affilies, $"/api/v1/affilies/{_affilie}", new { stack = "NullReferenceException at ..." }, HttpStatusCode.InternalServerError);

        var response = await Client().GetAsync($"/api/v1/affilies/{_affilie}", Ct);
        var texte = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        texte.ShouldContain("service-aval.reponse-inattendue");
        texte.ShouldNotContain("NullReferenceException");
    }

    [Fact]
    public async Task La_liste_nominative_se_telecharge_en_CSV_sans_NISS()
    {
        var listeId = Guid.CreateVersion7();
        var posteId = Guid.CreateVersion7();
        var personneId = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.PostesRisques, $"/api/v1/listes-nominatives/{listeId}", new
        {
            id = listeId,
            affilieId = _affilie,
            type = "PosteSecurite",
            version = 2,
            dateReference = "2026-09-01",
            dateGeneration = "2026-09-01T08:00:00Z",
            genereePar = "gestionnaire-1",
            documentId = (Guid?)null,
            conserverJusquAu = "2031-09-01",
            propositionId = (Guid?)null,
            nombreLignes = 1,
            lignes = new[] { new { personneId, posteId, codesRisques = new[] { "BRUIT" }, dateDerniereEvaluation = "2026-03-01", origine = "Calcul" } },
        });
        _aval.Repondre(ServicesAvalSimules.PostesRisques, "/api/v1/postes", new[] { Poste(posteId, "Cariste") });
        _aval.Repondre(ServicesAvalSimules.Personnes, $"/api/v1/affilies/{_affilie}/travailleurs",
            new[] { new { id = personneId, nissMasque = "90.01.01-***.**", nom = "Dupont", prenom = "Jean", dateNaissance = "1990-01-01" } });

        var response = await Client().GetAsync($"/api/v1/affilies/{_affilie}/listes-nominatives/{listeId}/csv?langue=fr", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("liste-nominative-poste-securite-v2-2026-09-01.csv");
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Ct)).TrimStart('﻿');
        csv.ShouldStartWith("Nom;Prénom;Poste;Risques;Date de la dernière évaluation;Origine\r\n");
        csv.ShouldContain("Dupont;Jean;Cariste;BRUIT;2026-03-01;Calcul");
        csv.ShouldNotContain("***");
        _aval.Requetes.ShouldContain(r => r.Chemin == $"/api/v1/affilies/{_affilie}/travailleurs?date=2026-09-01");
    }

    [Fact]
    public async Task Une_proposition_de_poste_est_transmise_au_service_avec_le_jeton_de_l_employeur()
    {
        var posteId = Guid.CreateVersion7();
        var propositionId = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.PostesRisques, $"/api/v1/postes/{posteId}", Poste(posteId, "Soudeur"));
        _aval.Repondre(ServicesAvalSimules.PostesRisques, $"/api/v1/postes/{posteId}/propositions", new { id = propositionId }, HttpStatusCode.Created, "POST");
        var jeton = Jeton("employeur", [_affilie]);

        var json = await Json(await Client(jeton).PostAsJsonAsync($"/api/v1/affilies/{_affilie}/postes/{posteId}/propositions", new
        {
            motif = "Nouvelle machine bruyante",
            valideDu = "2026-11-01",
            lignes = new[] { new { type = "Ajout", risqueCode = "BRUIT", niveauExposition = "Eleve" } },
        }, Ct), HttpStatusCode.Created);

        json.GetProperty("id").GetGuid().ShouldBe(propositionId);
        json.GetProperty("statut").GetString().ShouldBe("Soumise");
        var post = _aval.Requetes.Single(r => r.Methode == "POST");
        post.Authorization.ShouldBe($"Bearer {jeton}");
        var corps = JsonDocument.Parse(post.Corps!).RootElement;
        corps.GetProperty("motif").GetString().ShouldBe("Nouvelle machine bruyante");
        corps.GetProperty("lignes")[0].GetProperty("risqueCode").GetString().ShouldBe("BRUIT");
    }

    [Fact]
    public async Task Une_proposition_refusee_par_le_service_est_relayee_et_n_est_pas_rejouee()
    {
        var posteId = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.PostesRisques, $"/api/v1/postes/{posteId}", Poste(posteId, "Soudeur"));
        _aval.Probleme(ServicesAvalSimules.PostesRisques, $"/api/v1/postes/{posteId}/propositions", HttpStatusCode.ServiceUnavailable, "indisponible", "POST");

        var response = await Client().PostAsJsonAsync($"/api/v1/affilies/{_affilie}/postes/{posteId}/propositions",
            new { motif = "x", valideDu = "2026-11-01", lignes = Array.Empty<object>() }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        _aval.Requetes.Count(r => r.Methode == "POST").ShouldBe(1, "Une écriture n'est jamais rejouée automatiquement.");
    }

    [Fact]
    public async Task Une_proposition_sur_le_poste_d_un_autre_affilie_est_inconnue()
    {
        var posteId = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.PostesRisques, $"/api/v1/postes/{posteId}", Poste(posteId, "Soudeur", _autreAffilieDuJeton));

        var probleme = await Json(await Client().PostAsJsonAsync($"/api/v1/affilies/{_affilie}/postes/{posteId}/propositions",
            new { motif = "x", valideDu = "2026-11-01", lignes = Array.Empty<object>() }, Ct), HttpStatusCode.NotFound);

        probleme.GetProperty("code").GetString().ShouldBe("poste.inconnu");
        _aval.Requetes.ShouldNotContain(r => r.Methode == "POST");
    }

    [Fact]
    public async Task Le_portail_est_autorise_par_CORS()
    {
        using var requete = new HttpRequestMessage(HttpMethod.Options, "/api/v1/affilies");
        requete.Headers.Add("Origin", "http://localhost:4201");
        requete.Headers.Add("Access-Control-Request-Method", "GET");
        requete.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await _factory.CreateClient().SendAsync(requete, Ct);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["http://localhost:4201"]);
    }
}
