using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Sepp.Bff.Interne.Relais;

using Shouldly;

using Yarp.ReverseProxy.Forwarder;

namespace Sepp.Bff.Interne.Integration.Tests;

/// <summary>
/// BFF interne complet (pipeline ASP.NET Core, validation réelle du jeton JWT, relais YARP, clients typés) face à des services
/// simulés au niveau HTTP : authentification, réservation aux profils internes, liste fermée, en-têtes transmis, chemins
/// refusés, absence de reprise, écrans composites et CORS (ADR 0013).
/// </summary>
public sealed class BffInterneApiTests : IAsyncLifetime
{
    private const string Emetteur = "https://idp.test/realms/veilla";
    private const string OrigineApplication = "http://localhost:8081";

    private static readonly SymmetricSecurityKey Cle = new(RandomNumberGenerator.GetBytes(32));

    private readonly ServicesAvalSimules _aval = new();
    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            foreach (var service in ServicesRelayes.Tous)
            {
                b.UseSetting($"ServicesAval:{service.Cle}", $"http://{ServicesAvalSimules.Hote(service.Segment)}");
            }

            b.UseSetting("Cors:OriginesAutorisees:0", OrigineApplication);
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
                s.AddSingleton<IForwarderHttpClientFactory>(new FabriqueSimulee(_aval));
                s.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = _aval));
            });
        });
        _ = _factory.Server;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static string Jeton(string[] roles, string audience = "sepp-api", string emetteur = Emetteur) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emetteur,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "utilisateur-1", ["roles"] = roles },
            SigningCredentials = new SigningCredentials(Cle, SecurityAlgorithms.HmacSha256),
        });

    private HttpClient Client(string? jeton)
    {
        var client = _factory.CreateClient();
        if (jeton is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }

        return client;
    }

    private HttpClient Interne() => Client(Jeton(["cpmt"]));

    private void ParametresLegaux() =>
        _aval.Repondre("referentiels", "/api/v1/parametres-legaux", new[] { new { code = "delai-reprise", valeur = "10" } });

    private static async Task<JsonElement> Probleme(HttpResponseMessage response, HttpStatusCode attendu)
    {
        var texte = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(attendu, texte);
        return JsonDocument.Parse(texte).RootElement;
    }

    [Theory]
    [InlineData("/referentiels/api/v1/parametres-legaux")]
    [InlineData("/api/v1/ecrans/tableau-de-bord")]
    [InlineData("/inconnu/api/v1/x")]
    public async Task Un_appel_non_authentifie_recoit_401_sans_appel_aval(string chemin)
    {
        ParametresLegaux();

        (await Client(null).GetAsync(chemin, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        _aval.Requetes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("employeur")]
    [InlineData("sipp")]
    [InlineData("travailleur")]
    [InlineData("integrations")]
    public async Task Un_jeton_sans_role_interne_recoit_403_sans_appel_aval(string role)
    {
        ParametresLegaux();
        var client = Client(Jeton([role]));

        (await client.GetAsync("/referentiels/api/v1/parametres-legaux", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsync("/personnes/api/v1/personnes", JsonContent(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/ecrans/recherche?q=martin", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_jeton_qui_cumule_un_role_interne_et_un_role_externe_recoit_403()
    {
        (await Client(Jeton(["cpmt", "employeur"])).GetAsync("/referentiels/api/v1/parametres-legaux", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task L_audience_et_l_emetteur_sont_valides()
    {
        ParametresLegaux();

        (await Client(Jeton(["cpmt"], audience: "autre-api")).GetAsync("/referentiels/api/v1/parametres-legaux", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client(Jeton(["cpmt"], emetteur: "https://autre-idp.test/realms/veilla")).GetAsync("/referentiels/api/v1/parametres-legaux", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        _aval.Requetes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cpmt", "referentiels")]
    [InlineData("assistant-medical", "planification")]
    [InlineData("responsable-centre", "planification")]
    [InlineData("admin-fonctionnel", "postes-risques")]
    [InlineData("dpo", "audit")]
    public async Task Un_profil_interne_est_relaye_vers_api_v1_du_service_avec_la_requete(string role, string service)
    {
        _aval.Repondre(service, "/api/v1/ressource", new { ok = true });

        var response = await Client(Jeton([role])).GetAsync($"/{service}/api/v1/ressource?langue=nl&page=2", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("""{"ok":true}""");
        var requete = _aval.Requetes.ShouldHaveSingleItem();
        requete.Hote.ShouldBe(ServicesAvalSimules.Hote(service));
        requete.Chemin.ShouldBe("/api/v1/ressource?langue=nl&page=2");
    }

    [Fact]
    public async Task Chacun_des_douze_services_de_la_liste_fermee_est_joignable()
    {
        var client = Interne();
        foreach (var service in ServicesRelayes.Tous)
        {
            _aval.Repondre(service.Segment, "/api/v1/sonde", new { service = service.Segment });
            (await client.GetAsync($"/{service.Segment}/api/v1/sonde", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, service.Segment);
        }

        _aval.Requetes.Select(r => r.Hote).ShouldBe(ServicesRelayes.Tous.Select(s => ServicesAvalSimules.Hote(s.Segment)));
    }

    [Fact]
    public async Task Seuls_les_en_tetes_de_la_liste_blanche_sont_transmis_et_aucun_n_est_ajoute()
    {
        ParametresLegaux();
        var jeton = Jeton(["cpmt"]);
        var correlation = Guid.CreateVersion7();
        using var requete = new HttpRequestMessage(HttpMethod.Get, "/referentiels/api/v1/parametres-legaux");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        requete.Headers.TryAddWithoutValidation("Accept-Language", "nl-BE");
        requete.Headers.TryAddWithoutValidation("Accept", "application/json");
        requete.Headers.TryAddWithoutValidation("X-Motif-Acces", "consultation-planifiee");
        requete.Headers.TryAddWithoutValidation("X-Bris-De-Glace", "false");
        requete.Headers.TryAddWithoutValidation("X-Correlation-Id", correlation.ToString());
        requete.Headers.TryAddWithoutValidation("Cookie", "session=volee");
        requete.Headers.TryAddWithoutValidation("X-Forwarded-For", "10.0.0.66");
        requete.Headers.TryAddWithoutValidation("X-Forwarded-Host", "attaquant.example");
        requete.Headers.TryAddWithoutValidation("X-Test-Roles", "cpmt-dirigeant");
        requete.Headers.TryAddWithoutValidation("X-User", "admin");
        requete.Headers.TryAddWithoutValidation("Forwarded", "for=10.0.0.66");

        var response = await Client(null).SendAsync(requete, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse("Un cookie posé par le service ne traverse pas le BFF.");
        var recue = _aval.Requetes.ShouldHaveSingleItem();
        recue.EnTetes["Authorization"].ShouldBe($"Bearer {jeton}");
        recue.EnTetes["Accept-Language"].ShouldBe("nl-BE");
        recue.EnTetes["Accept"].ShouldBe("application/json");
        recue.EnTetes["X-Motif-Acces"].ShouldBe("consultation-planifiee");
        recue.EnTetes["X-Bris-De-Glace"].ShouldBe("false");
        recue.EnTetes["X-Correlation-Id"].ShouldBe(correlation.ToString());
        recue.EnTeteHost.ShouldNotBe("localhost");
        recue.EnTetes.Keys.Order(StringComparer.OrdinalIgnoreCase)
            .ShouldBe(new[] { "Accept", "Accept-Language", "Authorization", "X-Bris-De-Glace", "X-Correlation-Id", "X-Motif-Acces" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Une_correlation_invalide_n_est_pas_relayee_telle_quelle()
    {
        ParametresLegaux();
        var client = Interne();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", "abc\r\nInjection: 1");

        await client.GetAsync("/referentiels/api/v1/parametres-legaux", Ct);

        var correlation = _aval.Requetes.ShouldHaveSingleItem().EnTetes["X-Correlation-Id"];
        correlation.ShouldNotContain("abc");
        (Guid.TryParse(correlation, out _) || correlation.Length == 32).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/inconnu/api/v1/parametres-legaux")]
    [InlineData("/identite/api/v1/utilisateurs")]
    [InlineData("/documents-medicale/api/v1/documents")]
    [InlineData("/referentiels/health/ready")]
    [InlineData("/referentiels/openapi/v1.json")]
    [InlineData("/referentiels/scalar")]
    [InlineData("/referentiels/api/v2/parametres-legaux")]
    [InlineData("/referentiels/admin/republications")]
    [InlineData("/referentiels")]
    [InlineData("/api/v1/parametres-legaux")]
    public async Task Un_service_hors_liste_ou_un_chemin_hors_api_v1_recoit_404_sans_appel_aval(string chemin)
    {
        (await Interne().GetAsync(chemin, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        _aval.Requetes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/referentiels/api/v1/%2e%2e/%2e%2e/health/ready")]
    [InlineData("/referentiels/api/v1/..%2F..%2Fhealth/ready")]
    [InlineData("/referentiels/api/v1/..%5C..%5Chealth/ready")]
    [InlineData("/referentiels/api/v1/../../health/ready")]
    public async Task Un_chemin_qui_tente_de_sortir_de_api_v1_est_refuse_sans_appel_aval(string chemin)
    {
        var response = await Interne().GetAsync(new Uri(chemin, UriKind.Relative), Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_ecriture_est_relayee_avec_son_corps_et_n_est_jamais_rejouee()
    {
        _aval.Repondre("personnes", "/api/v1/personnes", new { title = "indisponible", status = 503 }, HttpStatusCode.ServiceUnavailable, "POST");

        var response = await Interne().PostAsync("/personnes/api/v1/personnes", JsonContent(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var requete = _aval.Requetes.ShouldHaveSingleItem("Une écriture n'est jamais rejouée.");
        requete.Methode.ShouldBe("POST");
        requete.Corps.ShouldBe("""{"nom":"Martin"}""");
        requete.EnTetes["Content-Type"].ShouldStartWith("application/json");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Les_methodes_put_et_delete_sont_relayees(string methode)
    {
        _aval.Repondre("planification", "/api/v1/lieux/1", new { }, HttpStatusCode.NoContent, methode);

        var response = await Interne().SendAsync(new HttpRequestMessage(new HttpMethod(methode), "/planification/api/v1/lieux/1")
        {
            Content = methode == "PUT" ? JsonContent() : null,
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _aval.Requetes.ShouldHaveSingleItem().Methode.ShouldBe(methode);
    }

    [Fact]
    public async Task Le_refus_d_un_service_est_relaye_tel_quel()
    {
        _aval.Repondre("surveillance-medicale", "/api/v1/dossiers/1", new { title = "motif-acces.requis", status = 403, code = "motif-acces.requis" }, HttpStatusCode.Forbidden);

        var probleme = await Probleme(await Client(Jeton(["gestionnaire"])).GetAsync("/surveillance-medicale/api/v1/dossiers/1", Ct), HttpStatusCode.Forbidden);

        probleme.GetProperty("code").GetString().ShouldBe("motif-acces.requis");
    }

    [Fact]
    public async Task Un_service_injoignable_donne_503_explicite()
    {
        _aval.Injoignable("audit");

        var probleme = await Probleme(await Client(Jeton(["dpo"])).GetAsync("/audit/api/v1/audit/entrees", Ct), HttpStatusCode.ServiceUnavailable);

        probleme.GetProperty("code").GetString().ShouldBe("service-aval.indisponible");
        probleme.GetProperty("service").GetString().ShouldBe("audit");
        _aval.Requetes.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("/api/v1/ecrans/recherche?q=martin", "Recherche")]
    [InlineData("/api/v1/ecrans/travailleurs/0192a5c8-0000-7000-8000-0000000000a1", "DossierTravailleur")]
    [InlineData("/api/v1/ecrans/affilies/0192a5c8-0000-7000-8000-000000000001", "Affilie")]
    [InlineData("/api/v1/ecrans/agenda", "Agenda")]
    [InlineData("/api/v1/ecrans/consultation/0192a5c8-0000-7000-8000-0000000000a1", "Consultation")]
    [InlineData("/api/v1/ecrans/tableau-de-bord", "TableauDeBord")]
    public async Task Les_ecrans_composites_non_realises_repondent_501(string chemin, string ecran)
    {
        var probleme = await Probleme(await Interne().GetAsync(chemin, Ct), HttpStatusCode.NotImplemented);

        probleme.GetProperty("code").GetString().ShouldBe("ecran.non-implemente");
        probleme.GetProperty("ecran").GetString().ShouldBe(ecran);
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_cors_autorise_l_application_interne_avec_ses_methodes_et_les_en_tetes_de_motif()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/surveillance-medicale/api/v1/dossiers/1");
        preflight.Headers.TryAddWithoutValidation("Origin", OrigineApplication);
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "PUT");
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization,content-type,x-motif-acces,x-bris-de-glace");

        var response = await Client(null).SendAsync(preflight, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([OrigineApplication]);
        string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")).ShouldContain("PUT");
        var entetes = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        entetes.ShouldContain("X-Motif-Acces", Case.Insensitive);
        entetes.ShouldContain("X-Bris-De-Glace", Case.Insensitive);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
        _aval.Requetes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_cors_refuse_une_autre_origine()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/referentiels/api/v1/parametres-legaux");
        preflight.Headers.TryAddWithoutValidation("Origin", "http://localhost:8082");
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        var response = await Client(null).SendAsync(preflight, Ct);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        _aval.Requetes.ShouldBeEmpty();
    }

    private static StringContent JsonContent() => new("""{"nom":"Martin"}""", Encoding.UTF8, "application/json");

    /// <summary>Client du relais branché sur les services simulés (remplace le SocketsHttpHandler de YARP).</summary>
    private sealed class FabriqueSimulee(HttpMessageHandler services) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(services, disposeHandler: false);
    }
}
