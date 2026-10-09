using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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

namespace Sepp.Bff.Travailleur.Integration.Tests;

/// <summary>
/// BFF complet (pipeline ASP.NET Core, validation réelle du jeton JWT, clients typés, résilience) face à des services aval
/// simulés au niveau HTTP : périmètre <c>personne_id</c>, propagation du jeton, écriture seule, traduction des erreurs.
/// </summary>
public sealed class BffTravailleurApiTests : IAsyncLifetime
{
    private const string Emetteur = "https://idp.test/realms/veilla";

    private static readonly SymmetricSecurityKey Cle = new(RandomNumberGenerator.GetBytes(32));

    private readonly Guid _personne = Guid.CreateVersion7();
    private readonly Guid _autrePersonne = Guid.CreateVersion7();
    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly ServicesAvalSimules _aval = new();
    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ServicesAval:Planification", $"http://{ServicesAvalSimules.Planification}");
            b.UseSetting("ServicesAval:SurveillanceMedicale", $"http://{ServicesAvalSimules.SurveillanceMedicale}");
            b.UseSetting("ServicesAval:Obligations", $"http://{ServicesAvalSimules.Obligations}");
            b.UseSetting("ServicesAval:Documents", $"http://{ServicesAvalSimules.Documents}");
            b.UseSetting("Cors:Origines:0", "http://localhost:4202");
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

    private static string Jeton(string role, params string[] personnes) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emetteur,
            Audience = "sepp-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "travailleur-1",
                ["roles"] = new[] { role },
                ["personne_id"] = personnes.Length == 1 ? personnes[0] : personnes,
            },
            SigningCredentials = new SigningCredentials(Cle, SecurityAlgorithms.HmacSha256),
        });

    private HttpClient Client(string? jeton = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton ?? Jeton("travailleur", _personne.ToString()));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode attendu = HttpStatusCode.OK)
    {
        var texte = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(attendu, texte);
        return JsonDocument.Parse(texte).RootElement;
    }

    private object RendezVous(Guid id, Guid personne, string debut, string statut = "Planifie") => new
    {
        id,
        creneauId = Guid.CreateVersion7(),
        personneId = personne,
        affilieId = _affilie,
        ressourceId = Guid.CreateVersion7(),
        lieuId = Guid.CreateVersion7(),
        debut,
        fin = DateTimeOffset.Parse(debut).AddMinutes(30),
        typeActe = "EVALUATION_PERIODIQUE",
        statut,
        motifAnnulation = (string?)null,
        obligationIds = new[] { Guid.CreateVersion7() },
        origine = "ReservationEnLigne",
        urgent = false,
        arriveeA = (string?)null,
        appeleA = (string?)null,
        salleId = Guid.CreateVersion7(),
        reconvocationDeId = (Guid?)null,
    };

    private static object Document(Guid id, Guid destinataire, string statut = "Publie", string code = "SANTE.EVALUATION.TRAVAILLEUR") => new
    {
        id,
        modeleId = Guid.CreateVersion7(),
        codeModele = code,
        versionModele = 1,
        langue = "Fr",
        motifLangue = "choix",
        zone = "Medicale",
        serviceProprietaire = "surveillance-medicale",
        objetType = "decision",
        objetId = Guid.CreateVersion7(),
        typeDestinataire = "Personne",
        destinataireId = destinataire,
        exemplaire = "Travailleur",
        format = "PDF/A-1a",
        taille = 4096,
        empreinte = "sha256:secret",
        date = "2026-09-01T08:00:00Z",
        autoriteHorodatage = "interne",
        statut,
        publieLe = "2026-09-02T08:00:00Z",
        signatures = Array.Empty<object>(),
    };

    // --- POR-10 : authentification et périmètre ---

    [Fact]
    public async Task Sans_jeton_le_BFF_repond_401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/rendez-vous", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_jeton_d_une_autre_audience_est_refuse()
    {
        var jeton = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emetteur,
            Audience = "autre-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["roles"] = new[] { "travailleur" }, ["personne_id"] = _personne.ToString() },
            SigningCredentials = new SigningCredentials(Cle, SecurityAlgorithms.HmacSha256),
        });

        (await Client(jeton).GetAsync("/api/v1/rendez-vous", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("employeur")]
    [InlineData("cpmt")]
    [InlineData("infirmier")]
    public async Task Un_autre_profil_n_utilise_pas_le_BFF_travailleur(string role)
    {
        var response = await Client(Jeton(role, _personne.ToString())).GetAsync("/api/v1/rendez-vous", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _aval.Requetes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("pas-un-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Un_claim_personne_id_invalide_est_refuse_sans_appel_aval(string valeur)
    {
        var probleme = await Json(await Client(Jeton("travailleur", valeur)).GetAsync("/api/v1/rendez-vous", Ct), HttpStatusCode.Forbidden);

        probleme.GetProperty("code").GetString().ShouldBe("perimetre.interdit");
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_jeton_sans_personne_ou_avec_deux_personnes_est_refuse_sans_appel_aval()
    {
        var sans = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emetteur,
            Audience = "sepp-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["roles"] = new[] { "travailleur" } },
            SigningCredentials = new SigningCredentials(Cle, SecurityAlgorithms.HmacSha256),
        });

        (await Client(sans).GetAsync("/api/v1/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Jeton("travailleur", _personne.ToString(), _autrePersonne.ToString())).GetAsync("/api/v1/documents", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _aval.Requetes.ShouldBeEmpty();
    }

    // --- POR-11 : rendez-vous ---

    [Fact]
    public async Task Les_rendez_vous_sont_lus_avec_le_jeton_et_la_correlation_et_ne_montrent_aucune_information_interne()
    {
        var mien = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.Planification, "/api/v1/reservations/rendez-vous", new[]
        {
            RendezVous(Guid.CreateVersion7(), _autrePersonne, "2026-12-03T09:00:00Z"),
            RendezVous(mien, _personne, "2026-12-01T09:00:00Z"),
        });
        var jeton = Jeton("travailleur", _personne.ToString());
        using var client = Client(jeton);
        const string correlation = "0192a5c8-0000-7000-8000-0000000000c4";
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);

        var response = await client.GetAsync("/api/v1/rendez-vous", Ct);
        var texte = await response.Content.ReadAsStringAsync(Ct);
        var json = await Json(response);

        json.GetArrayLength().ShouldBe(1);
        json[0].GetProperty("id").GetGuid().ShouldBe(mien);
        texte.ShouldNotContain("ressourceId");
        texte.ShouldNotContain("salleId");
        texte.ShouldNotContain("obligationIds");
        var requete = _aval.Requetes.Single();
        requete.Authorization.ShouldBe($"Bearer {jeton}");
        requete.Correlation.ShouldBe(correlation);
        requete.Chemin.ShouldBe("/api/v1/reservations/rendez-vous");
    }

    [Fact]
    public async Task Les_creneaux_ouverts_sont_relayes_pour_l_affilie_demande()
    {
        _aval.Repondre(ServicesAvalSimules.Planification, "/api/v1/reservations/creneaux", new[]
        {
            new { id = Guid.CreateVersion7(), lieuId = Guid.CreateVersion7(), debut = "2026-12-01T09:00:00Z", fin = "2026-12-01T09:30:00Z", typeActe = "EVALUATION_PERIODIQUE" },
        });

        var json = await Json(await Client().GetAsync(
            $"/api/v1/rendez-vous/creneaux?affilieId={_affilie}&typeActe=EVALUATION_PERIODIQUE&du=2026-12-01T00:00:00Z&au=2026-12-31T00:00:00Z", Ct));

        json.GetArrayLength().ShouldBe(1);
        var requete = _aval.Requetes.Single();
        requete.Chemin.ShouldContain($"affilieId={_affilie}");
        requete.Chemin.ShouldContain("typeActe=EVALUATION_PERIODIQUE");
    }

    [Fact]
    public async Task La_reservation_est_faite_pour_la_personne_du_jeton_et_non_celle_du_corps()
    {
        var creneau = Guid.CreateVersion7();
        var rendezVous = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.Planification, "/api/v1/reservations", new { id = rendezVous }, HttpStatusCode.Created, "POST");

        var response = await Client().PostAsJsonAsync("/api/v1/rendez-vous",
            new { creneauId = creneau, affilieId = _affilie, personneId = _autrePersonne }, Ct);

        var json = await Json(response, HttpStatusCode.Created);
        json.GetProperty("id").GetGuid().ShouldBe(rendezVous);
        var corps = JsonDocument.Parse(_aval.Requetes.Single().Corps!).RootElement;
        corps.GetProperty("personneId").GetGuid().ShouldBe(_personne);
        corps.GetProperty("creneauId").GetGuid().ShouldBe(creneau);
        corps.GetProperty("affilieId").GetGuid().ShouldBe(_affilie);
    }

    [Fact]
    public async Task Un_creneau_indisponible_est_relaye_en_409_sans_rejeu()
    {
        _aval.Probleme(ServicesAvalSimules.Planification, "/api/v1/reservations", HttpStatusCode.Conflict, "creneau.indisponible", "POST");

        var probleme = await Json(await Client().PostAsJsonAsync("/api/v1/rendez-vous",
            new { creneauId = Guid.CreateVersion7(), affilieId = _affilie }, Ct), HttpStatusCode.Conflict);

        probleme.GetProperty("code").GetString().ShouldBe("creneau.indisponible");
        probleme.GetProperty("service").GetString().ShouldBe("planification");
        _aval.Requetes.Count(r => r.Methode == "POST").ShouldBe(1);
    }

    [Fact]
    public async Task Une_reservation_n_est_jamais_rejouee_quand_Planification_est_indisponible()
    {
        _aval.Probleme(ServicesAvalSimules.Planification, "/api/v1/reservations", HttpStatusCode.ServiceUnavailable, "indisponible", "POST");

        var response = await Client().PostAsJsonAsync("/api/v1/rendez-vous", new { creneauId = Guid.CreateVersion7(), affilieId = _affilie }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        _aval.Requetes.Count(r => r.Methode == "POST").ShouldBe(1, "Une écriture n'est jamais rejouée automatiquement.");
    }

    [Fact]
    public async Task L_annulation_est_relayee_avec_le_jeton_et_renvoie_204()
    {
        var id = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.Planification, $"/api/v1/reservations/rendez-vous/{id}/annulation", new { }, HttpStatusCode.NoContent, "POST");
        var jeton = Jeton("travailleur", _personne.ToString());

        var response = await Client(jeton).PostAsync($"/api/v1/rendez-vous/{id}/annulation", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _aval.Requetes.Single().Authorization.ShouldBe($"Bearer {jeton}");
    }

    [Fact]
    public async Task Une_annulation_tardive_est_refusee_par_le_service_et_le_refus_est_traduit()
    {
        var id = Guid.CreateVersion7();
        _aval.Probleme(ServicesAvalSimules.Planification, $"/api/v1/reservations/rendez-vous/{id}/annulation", HttpStatusCode.Conflict, "rendez-vous.annulation-tardive", "POST");

        var probleme = await Json(await Client().PostAsync($"/api/v1/rendez-vous/{id}/annulation", null, Ct), HttpStatusCode.Conflict);

        probleme.GetProperty("code").GetString().ShouldBe("rendez-vous.annulation-tardive");
    }

    // --- POR-12 : questionnaires et demandes ---

    [Fact]
    public async Task Les_modeles_de_questionnaire_sont_lus_dans_la_langue_demandee()
    {
        _aval.Repondre(ServicesAvalSimules.SurveillanceMedicale, "/api/v1/protocoles/questionnaires", new[]
        {
            new
            {
                id = Guid.CreateVersion7(),
                code = "SANTE-GENERAL",
                version = 2,
                titre = "Gezondheidsvragenlijst",
                questions = new[] { new { code = "Q1", libelle = "Rookt u?", typeReponse = "OuiNon", obligatoire = true, choix = Array.Empty<string>() } },
            },
        });

        var json = await Json(await Client().GetAsync("/api/v1/questionnaires?langue=nl", Ct));

        json[0].GetProperty("code").GetString().ShouldBe("SANTE-GENERAL");
        json[0].GetProperty("questions")[0].GetProperty("typeReponse").GetString().ShouldBe("OuiNon");
        _aval.Requetes.Single().Chemin.ShouldBe("/api/v1/protocoles/questionnaires?langue=nl");
    }

    [Fact]
    public async Task Le_questionnaire_est_ecrit_pour_la_personne_du_jeton_et_la_reponse_ne_contient_aucune_donnee_du_dossier()
    {
        var enregistre = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.SurveillanceMedicale, "/api/v1/questionnaires/pre-remplissage", new { id = enregistre }, HttpStatusCode.Created, "POST");
        var jeton = Jeton("travailleur", _personne.ToString());

        var response = await Client(jeton).PostAsJsonAsync("/api/v1/questionnaires/SANTE-GENERAL/reponses",
            new { reponses = new[] { new { codeQuestion = "Q1", valeur = "Non" } }, personneId = _autrePersonne }, Ct);

        var json = await Json(response, HttpStatusCode.Created);
        json.GetProperty("id").GetGuid().ShouldBe(enregistre);
        json.EnumerateObject().Select(p => p.Name).ShouldBe(["id"]);
        var requete = _aval.Requetes.Single();
        requete.Authorization.ShouldBe($"Bearer {jeton}");
        var corps = JsonDocument.Parse(requete.Corps!).RootElement;
        corps.GetProperty("personneId").GetGuid().ShouldBe(_personne);
        corps.GetProperty("modeleCode").GetString().ShouldBe("SANTE-GENERAL");
        corps.GetProperty("reponses")[0].GetProperty("valeur").GetString().ShouldBe("Non");
    }

    [Fact]
    public async Task Le_BFF_n_appelle_jamais_le_dossier_de_sante_ni_les_reponses_deja_enregistrees()
    {
        _aval.Repondre(ServicesAvalSimules.SurveillanceMedicale, "/api/v1/questionnaires/pre-remplissage", new { id = Guid.CreateVersion7() }, HttpStatusCode.Created, "POST");
        _aval.Repondre(ServicesAvalSimules.SurveillanceMedicale, "/api/v1/protocoles/questionnaires", Array.Empty<object>());
        using var client = Client();

        await client.GetAsync("/api/v1/questionnaires", Ct);
        await client.PostAsJsonAsync("/api/v1/questionnaires/SANTE-GENERAL/reponses", new { reponses = Array.Empty<object>() }, Ct);
        await client.GetAsync("/api/v1/accueil", Ct);

        _aval.Requetes.Where(r => r.Hote == ServicesAvalSimules.SurveillanceMedicale).Select(r => r.Chemin.Split('?')[0])
            .Distinct().Order().ShouldBe(["/api/v1/protocoles/questionnaires", "/api/v1/questionnaires/pre-remplissage"]);
        _aval.Requetes.ShouldNotContain(r => r.Chemin.Contains("/dossiers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_refus_de_validation_du_questionnaire_est_traduit_et_non_rejoue()
    {
        _aval.Probleme(ServicesAvalSimules.SurveillanceMedicale, "/api/v1/questionnaires/pre-remplissage", HttpStatusCode.UnprocessableEntity, "questionnaire.invalide", "POST");

        var probleme = await Json(await Client().PostAsJsonAsync("/api/v1/questionnaires/SANTE-GENERAL/reponses",
            new { reponses = new[] { new { codeQuestion = "Q1", valeur = "" } } }, Ct), HttpStatusCode.UnprocessableEntity);

        probleme.GetProperty("code").GetString().ShouldBe("questionnaire.invalide");
        _aval.Requetes.Count(r => r.Methode == "POST").ShouldBe(1);
    }

    [Theory]
    [InlineData("CONSULTATION_SPONTANEE")]
    [InlineData("visite_pre_reprise")]
    public async Task Une_demande_est_relayee_a_Obligations_pour_la_personne_du_jeton_sans_motif(string type)
    {
        var id = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.Obligations, "/api/v1/demandes-travailleur", new { id }, HttpStatusCode.Created, "POST");

        var json = await Json(await Client().PostAsJsonAsync("/api/v1/demandes",
            new { affilieId = _affilie, type, personneId = _autrePersonne, motif = "mal au dos" }, Ct), HttpStatusCode.Created);

        json.GetProperty("id").GetGuid().ShouldBe(id);
        var corps = JsonDocument.Parse(_aval.Requetes.Single().Corps!).RootElement;
        corps.GetProperty("personneId").GetGuid().ShouldBe(_personne);
        corps.GetProperty("type").GetString().ShouldBe(type.ToUpperInvariant());
        corps.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["affilieId", "personneId", "type"]);
    }

    [Fact]
    public async Task Un_type_de_demande_non_offert_au_travailleur_est_refuse_sans_appel_aval()
    {
        var probleme = await Json(await Client().PostAsJsonAsync("/api/v1/demandes",
            new { affilieId = _affilie, type = "EVALUATION_PERIODIQUE" }, Ct), HttpStatusCode.BadRequest);

        probleme.GetProperty("code").GetString().ShouldBe("demande.type-invalide");
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_refus_de_droits_d_Obligations_est_traduit_en_403()
    {
        // Lacune connue : Obligations exige aujourd'hui obligation:gerer, que le rôle travailleur n'a pas (voir le README).
        _aval.Probleme(ServicesAvalSimules.Obligations, "/api/v1/demandes-travailleur", HttpStatusCode.Forbidden, "obligation.interdit", "POST");

        var probleme = await Json(await Client().PostAsJsonAsync("/api/v1/demandes",
            new { affilieId = _affilie, type = "CONSULTATION_SPONTANEE" }, Ct), HttpStatusCode.Forbidden);

        probleme.GetProperty("code").GetString().ShouldBe("obligation.interdit");
        probleme.GetProperty("service").GetString().ShouldBe("obligations");
    }

    // --- POR-13 : documents ---

    [Fact]
    public async Task Les_documents_sont_ceux_publies_pour_la_personne_du_jeton_sans_zone_ni_empreinte()
    {
        var mien = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.Documents, "/api/v1/documents", new[]
        {
            Document(mien, _personne),
            Document(Guid.CreateVersion7(), _personne, statut: "Archive"),
            Document(Guid.CreateVersion7(), _autrePersonne),
        });

        var response = await Client().GetAsync("/api/v1/documents", Ct);
        var texte = await response.Content.ReadAsStringAsync(Ct);
        var json = await Json(response);

        json.GetArrayLength().ShouldBe(1);
        json[0].GetProperty("id").GetGuid().ShouldBe(mien);
        json[0].GetProperty("categorie").GetString().ShouldBe("evaluation-sante");
        texte.ShouldNotContain("empreinte");
        texte.ShouldNotContain("sha256");
        texte.ShouldNotContain("Medicale");
        _aval.Requetes.Single().Chemin.ShouldBe($"/api/v1/documents?typeDestinataire=Personne&destinataireId={_personne}");
    }

    [Fact]
    public async Task Le_contenu_d_un_document_est_servi_en_PDF_avec_le_nom_du_service()
    {
        var id = Guid.CreateVersion7();
        byte[] pdf = [0x25, 0x50, 0x44, 0x46];
        _aval.Repondre(ServicesAvalSimules.Documents, $"/api/v1/documents/{id}", Document(id, _personne));
        _aval.RepondreFichier(ServicesAvalSimules.Documents, $"/api/v1/documents/{id}/contenu", pdf, "evaluation-sante.pdf");

        var response = await Client().GetAsync($"/api/v1/documents/{id}/contenu", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("evaluation-sante.pdf");
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(pdf);
    }

    [Fact]
    public async Task Le_document_d_une_autre_personne_est_inconnu_et_son_contenu_n_est_pas_lu()
    {
        var id = Guid.CreateVersion7();
        _aval.Repondre(ServicesAvalSimules.Documents, $"/api/v1/documents/{id}", Document(id, _autrePersonne));
        _aval.RepondreFichier(ServicesAvalSimules.Documents, $"/api/v1/documents/{id}/contenu", [1, 2, 3], "autre.pdf");

        var probleme = await Json(await Client().GetAsync($"/api/v1/documents/{id}/contenu", Ct), HttpStatusCode.NotFound);

        probleme.GetProperty("code").GetString().ShouldBe("document.inconnu");
        _aval.Requetes.ShouldNotContain(r => r.Chemin.EndsWith("/contenu", StringComparison.Ordinal));
    }

    // --- Accueil composite, erreurs, CORS ---

    [Fact]
    public async Task L_accueil_compose_les_trois_sections_et_signale_un_service_indisponible_sans_echouer()
    {
        _aval.Repondre(ServicesAvalSimules.Planification, "/api/v1/reservations/rendez-vous", new[]
        {
            RendezVous(Guid.CreateVersion7(), _personne, DateTimeOffset.UtcNow.AddDays(10).ToString("O")),
            RendezVous(Guid.CreateVersion7(), _personne, DateTimeOffset.UtcNow.AddDays(-10).ToString("O"), "Termine"),
        });
        _aval.Repondre(ServicesAvalSimules.Documents, "/api/v1/documents", new[] { Document(Guid.CreateVersion7(), _personne) });
        _aval.Injoignable(ServicesAvalSimules.SurveillanceMedicale);

        var json = await Json(await Client().GetAsync("/api/v1/accueil", Ct));

        json.GetProperty("personneId").GetGuid().ShouldBe(_personne);
        json.GetProperty("prochainsRendezVous").GetArrayLength().ShouldBe(1);
        json.GetProperty("documentsRecents").GetArrayLength().ShouldBe(1);
        json.GetProperty("questionnairesDisponibles").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("indisponibles").EnumerateArray().Select(e => e.GetString()).ShouldBe(["questionnaires"]);
    }

    [Fact]
    public async Task Un_service_aval_injoignable_donne_un_503_explicite()
    {
        _aval.Injoignable(ServicesAvalSimules.Planification);

        var probleme = await Json(await Client().GetAsync("/api/v1/rendez-vous", Ct), HttpStatusCode.ServiceUnavailable);

        probleme.GetProperty("code").GetString().ShouldBe("service-aval.indisponible");
        probleme.GetProperty("service").GetString().ShouldBe("planification");
    }

    [Fact]
    public async Task Une_erreur_interne_du_service_aval_devient_un_502_sans_detail_technique()
    {
        _aval.Repondre(ServicesAvalSimules.Documents, "/api/v1/documents", new { stack = "NullReferenceException at ..." }, HttpStatusCode.InternalServerError);

        var response = await Client().GetAsync("/api/v1/documents", Ct);
        var texte = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        texte.ShouldContain("service-aval.reponse-inattendue");
        texte.ShouldNotContain("NullReferenceException");
    }

    [Fact]
    public async Task Le_portail_est_autorise_par_CORS()
    {
        using var requete = new HttpRequestMessage(HttpMethod.Options, "/api/v1/rendez-vous");
        requete.Headers.Add("Origin", "http://localhost:4202");
        requete.Headers.Add("Access-Control-Request-Method", "GET");
        requete.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await _factory.CreateClient().SendAsync(requete, Ct);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["http://localhost:4202"]);
    }
}
