using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Adapters.Securite;
using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Historique;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Affilies.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23) :
/// migrations, persistance de l'agrégat, historique, outbox, API et autorisations (§3.3).
/// </summary>
public sealed class AffiliesApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Affilies", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.ConfigureTestServices(s =>
            {
                s.AddAuthentication(TestAuth.Name).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Name, _ => { });
                s.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuth.Name;
                    o.DefaultChallengeScheme = TestAuth.Name;
                });
            });
        });
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private HttpClient Client(string role, params Guid[] affilies)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, role);
        if (affilies.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuth.AffiliesHeader, string.Join(',', affilies));
        }

        return client;
    }

    private HttpClient Gestionnaire => Client(Roles.GestionnaireDossiers);

    private static object Fiche(string denomination = "Boulangerie Dupont", string categorie = "B") => new
    {
        denomination,
        formeJuridique = "SRL",
        codeNace = "10.711",
        commissionParitaire = "118.03",
        categorieTarifaire = categorie,
        langue = "Fr",
        regimeLinguistique = "Francais",
    };

    private static readonly object Adresse = new { rue = "Rue de la Loi", numero = "16", codePostal = "1000", localite = "Bruxelles" };

    private async Task<Guid> CreerAsync(string bce = "0202.239.951", string dateAffiliation = "2024-01-01")
    {
        var response = await Gestionnaire.PostAsJsonAsync("/api/v1/affilies", new { numeroBce = bce, fiche = Fiche(), dateAffiliation }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.Location!.ToString().ShouldStartWith("/api/v1/affilies/");
        return await IdAsync(response);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

    private async Task<AffilieDto> ObtenirAsync(Guid id) =>
        (await Gestionnaire.GetFromJsonAsync<AffilieDto>($"/api/v1/affilies/{id}", Json, Ct))!;

    [Fact]
    public async Task Les_sondes_de_sante_repondent_sans_authentification()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Une_requete_anonyme_est_refusee() =>
        (await _factory.CreateClient().GetAsync("/api/v1/affilies", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Un_travailleur_n_accede_pas_aux_affilies() =>
        (await Client(Roles.Travailleur).GetAsync("/api/v1/affilies", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task La_fiche_est_creee_retrouvee_par_BCE_et_recherchee()
    {
        var id = await CreerAsync();

        var fiche = await ObtenirAsync(id);
        fiche.Fiche.NumeroBce.ShouldBe("0202.239.951");
        fiche.Fiche.CategorieTarifaire.ShouldBe(CategorieTarifaire.B);
        fiche.Fiche.Statut.ShouldBe(StatutAffilie.Actif);
        fiche.Version.ShouldBe(1);

        (await Gestionnaire.GetFromJsonAsync<AffilieDto>("/api/v1/affilies/bce/BE0202239951", Json, Ct))!.Id.ShouldBe(id);
        var page = await Gestionnaire.GetFromJsonAsync<PageDto<AffilieResumeDto>>("/api/v1/affilies?denomination=boulANGERIE&taille=5", Json, Ct);
        page!.Total.ShouldBe(1);
        page.Elements.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await Gestionnaire.GetFromJsonAsync<PageDto<AffilieResumeDto>>("/api/v1/affilies?denomination=50%25", Json, Ct))!.Total.ShouldBe(0);

        // L'outbox publie AffilieCree (publication en mémoire, aucun bus configuré).
        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "affilies.affilie-cree.v1"), Ct);
    }

    [Fact]
    public async Task Un_BCE_invalide_ou_deja_affilie_est_refuse_en_problem_details()
    {
        await CreerAsync();

        var doublon = await Gestionnaire.PostAsJsonAsync("/api/v1/affilies", new { numeroBce = "0202239951", fiche = Fiche(), dateAffiliation = "2024-01-01" }, Ct);
        var invalide = await Gestionnaire.PostAsJsonAsync("/api/v1/affilies", new { numeroBce = "0202.239.952", fiche = Fiche(), dateAffiliation = "2024-01-01" }, Ct);

        doublon.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalide.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalide.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await Gestionnaire.GetAsync("/api/v1/affilies/bce/0403.170.701", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task La_hierarchie_complete_est_persistee()
    {
        var id = await CreerAsync();
        var unite = await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{id}/unites-etablissement",
            new { numero = "2.123.456.791", nom = "Siège", adresse = Adresse, langue = "Fr", depuis = "2024-01-01" }, Ct);
        unite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var uniteId = await IdAsync(unite);
        var siteId = await IdAsync(await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{id}/unites-etablissement/{uniteId}/sites",
            new { nom = "Atelier", adresse = Adresse, latitude = 50.8466, longitude = 4.3528, depuis = "2024-01-01" }, Ct));
        var parentId = await IdAsync(await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{id}/sites/{siteId}/departements",
            new { nom = "Production", depuis = "2024-01-01" }, Ct));
        var enfant = await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{id}/sites/{siteId}/departements",
            new { nom = "Four", parentId, depuis = "2024-01-01" }, Ct);
        enfant.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await Gestionnaire.PutAsJsonAsync($"/api/v1/affilies/{id}/departements/{parentId}", new { nom = "Production", parentId = await IdAsync(enfant) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{id}/unites-etablissement/{uniteId}/fermeture", new { fin = "2026-01-01" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fiche = await ObtenirAsync(id);
        var ue = fiche.UnitesEtablissement.ShouldHaveSingleItem();
        ue.Numero.ShouldBe("2123.456.791");
        ue.ValideJusquAu.ShouldBe(new DateOnly(2026, 1, 1));
        var site = ue.Sites.ShouldHaveSingleItem();
        site.Latitude.ShouldBe(50.8466);
        site.ValideJusquAu.ShouldBe(new DateOnly(2026, 1, 1));
        site.Departements.Count.ShouldBe(2);
        site.Departements.Single(d => d.Nom == "Four").ParentId.ShouldBe(parentId);

        // Une unité d'établissement n'appartient qu'à un seul affilié.
        var autre = await CreerAsync("0403.170.701");
        (await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{autre}/unites-etablissement",
            new { numero = "2123456791", nom = "Siège", adresse = Adresse, langue = "Fr", depuis = "2024-01-01" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task L_employeur_gere_les_contacts_et_la_concertation_de_son_seul_affilie()
    {
        var id = await CreerAsync();
        var autre = await CreerAsync("0403.170.701");
        var employeur = Client(Roles.Employeur, id);
        var contact = new { nom = "Marie Dupont", fonction = "RH", role = "PersonneDeConfiance", email = "marie@dupont.be", valideDu = "2025-01-01" };

        var ajout = await employeur.PostAsJsonAsync($"/api/v1/affilies/{id}/contacts", contact, Ct);
        ajout.StatusCode.ShouldBe(HttpStatusCode.Created);
        var contactId = await IdAsync(ajout);
        var modification = await employeur.PutAsJsonAsync($"/api/v1/affilies/{id}/contacts/{contactId}", contact with { valideDu = "2025-06-01" }, Ct);
        modification.StatusCode.ShouldBe(HttpStatusCode.OK);

        var organeId = await IdAsync(await employeur.PostAsJsonAsync($"/api/v1/affilies/{id}/organes-concertation", new { type = "ComitePpt", depuis = "2024-01-01" }, Ct));
        var reunion = await employeur.PostAsJsonAsync($"/api/v1/affilies/{id}/organes-concertation/{organeId}/reunions",
            new { dateReunion = "2025-03-12", ordreDuJourDocumentId = Guid.CreateVersion7(), participationSepp = true }, Ct);
        reunion.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Hors périmètre ou hors écriture partielle.
        (await employeur.PostAsJsonAsync($"/api/v1/affilies/{autre}/contacts", contact, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await employeur.GetAsync($"/api/v1/affilies/{autre}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await employeur.PutAsJsonAsync($"/api/v1/affilies/{id}", new { fiche = Fiche("Autre nom") }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await employeur.PostAsJsonAsync($"/api/v1/affilies/{id}/resiliation", new { dateFin = "2026-12-31" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Cpmt).PostAsJsonAsync($"/api/v1/affilies/{id}/contacts", contact, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var recherche = await employeur.GetFromJsonAsync<PageDto<AffilieResumeDto>>("/api/v1/affilies", Json, Ct);
        recherche!.Elements.ShouldHaveSingleItem().Id.ShouldBe(id);

        var fiche = (await employeur.GetFromJsonAsync<AffilieDto>($"/api/v1/affilies/{id}", Json, Ct))!;
        fiche.Contacts.Count.ShouldBe(2);
        fiche.Contacts.Count(c => c.ValideJusquAu is null).ShouldBe(1);
        fiche.OrganesConcertation.ShouldHaveSingleItem().Reunions.ShouldHaveSingleItem().ParticipationSepp.ShouldBeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AffiliesDbContext>();
        (await db.Affilies.Where(a => a.Id == id).Select(a => EF.Property<string>(a, "updated_by")).SingleAsync(Ct)).ShouldBe("test-user");
    }

    [Fact]
    public async Task L_historique_trace_qui_quand_avant_et_apres()
    {
        var id = await CreerAsync();
        (await Gestionnaire.PutAsJsonAsync($"/api/v1/affilies/{id}", new { fiche = Fiche("Dupont & Fils", "C") }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{id}/resiliation", new { dateFin = "2026-12-31" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Gestionnaire.DeleteAsync($"/api/v1/affilies/{id}/resiliation", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var historique = (await Client(Roles.Employeur, id).GetFromJsonAsync<List<ModificationDto>>($"/api/v1/affilies/{id}/historique", Json, Ct))!;

        historique.Select(h => h.Version).ShouldBe([1, 2, 3, 4]);
        historique.Select(h => h.Action).ShouldBe(["affilie.cree", "fiche.modifiee", "affiliation.resiliee", "resiliation.annulee"]);
        historique.ShouldAllBe(h => h.Auteur == "test-user");
        historique[1].Avant!.Value.GetProperty("fiche").GetProperty("denomination").GetString().ShouldBe("Boulangerie Dupont");
        historique[1].Apres!.Value.GetProperty("fiche").GetProperty("denomination").GetString().ShouldBe("Dupont & Fils");
        historique[1].Apres!.Value.GetProperty("fiche").TryGetProperty("codeNace", out _).ShouldBeFalse();
        (await ObtenirAsync(id)).Version.ShouldBe(4);

        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Count(m => m.EventType == "affilies.affilie-modifie.v1") == 3, Ct);
    }

    [Fact]
    public async Task Une_fusion_realisee_fige_l_affilie_absorbe_et_publie_l_operation()
    {
        var absorbe = await CreerAsync(dateAffiliation: "2020-01-01");
        var absorbant = await CreerAsync("0403.170.701");

        var projection = await Gestionnaire.PostAsJsonAsync($"/api/v1/affilies/{absorbe}/operations",
            new { type = "Fusion", dateEffet = "2021-01-01", affilieAbsorbantId = absorbant }, Ct);
        projection.StatusCode.ShouldBe(HttpStatusCode.Created);
        var operationId = await IdAsync(projection);
        (await Gestionnaire.PostAsync($"/api/v1/affilies/{absorbe}/operations/{operationId}/realisation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fiche = await ObtenirAsync(absorbe);
        fiche.Fiche.Statut.ShouldBe(StatutAffilie.Absorbe);
        fiche.Fiche.DateFin.ShouldBe(new DateOnly(2020, 12, 31));
        fiche.Operations.ShouldHaveSingleItem().Statut.ShouldBe(StatutOperation.Realisee);
        (await Gestionnaire.PutAsJsonAsync($"/api/v1/affilies/{absorbe}", new { fiche = Fiche() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Client(Roles.Employeur, absorbe).PostAsJsonAsync($"/api/v1/affilies/{absorbe}/operations",
            new { type = "TransfertSortant", dateEffet = "2027-01-01", seppDestination = "X" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Count(m => m.EventType == "affilies.operation-affilie-modifiee.v1") == 2, Ct);
    }

    [Fact]
    public async Task Les_groupes_regroupent_des_affilies()
    {
        var groupe = await Gestionnaire.PostAsJsonAsync("/api/v1/groupes", new { nom = "Groupe Dupont" }, Ct);
        groupe.StatusCode.ShouldBe(HttpStatusCode.Created);
        var groupeId = await IdAsync(groupe);
        var id = await CreerAsync();

        (await Gestionnaire.PutAsJsonAsync($"/api/v1/affilies/{id}", new { fiche = Fiche(), groupeId }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Gestionnaire.PutAsJsonAsync($"/api/v1/groupes/{groupeId}", new { nom = "Dupont Holding" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ObtenirAsync(id)).Fiche.GroupeId.ShouldBe(groupeId);
        var groupes = await Gestionnaire.GetFromJsonAsync<List<JsonElement>>("/api/v1/groupes", Ct);
        groupes!.ShouldHaveSingleItem().GetProperty("nom").GetString().ShouldBe("Dupont Holding");
        (await Client(Roles.Sipp, id).PostAsJsonAsync("/api/v1/groupes", new { nom = "X" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task Eventually(Func<bool> condition, CancellationToken ct)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, ct);
        }

        condition().ShouldBeTrue();
    }

    /// <summary>Authentification de test : rôles et affiliés représentés (claim <c>affilie_id</c>) passés dans des en-têtes.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";
        public const string AffiliesHeader = "X-Test-Affilies";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Concat(Request.Headers[AffiliesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => new Claim(PerimetreJeton.ClaimAffilie, a)))
                .Append(new Claim("sub", "test-user"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
