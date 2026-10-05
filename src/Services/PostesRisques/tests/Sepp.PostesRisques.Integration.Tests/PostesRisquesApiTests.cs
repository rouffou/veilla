using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.PostesRisques.Adapters.Persistence;
using Sepp.PostesRisques.Application.Listes;
using Sepp.PostesRisques.Application.Postes;
using Sepp.PostesRisques.Application.Risques;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.PostesRisques.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23) : migrations, persistance,
/// outbox, réception idempotente des événements, API, permissions et périmètre de l'affilié.
/// </summary>
public sealed class PostesRisquesApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _affilie = Guid.CreateVersion7();
    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:PostesRisques", _postgres.GetConnectionString());
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

    private HttpClient Client(string role, string user = "test-user", Guid? affilie = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, role);
        client.DefaultRequestHeaders.Add(TestAuth.UserHeader, user);
        if (affilie is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAuth.AffiliesHeader, id.ToString());
        }

        return client;
    }

    private HttpClient Employeur => Client(Roles.Employeur, "employeur-1", _affilie);

    private HttpClient Cpmt => Client(Roles.Cpmt, "cpmt-1");

    private async Task ImporterJeuExemple()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Sepp.slnx")))
        {
            dir = dir.Parent;
        }

        var csv = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "src", "Services", "PostesRisques", "exemples", "risques-exemple.csv"), Ct);
        var response = await Client(Roles.AdministrateurFonctionnel).PostAsync(
            "/api/v1/risques/import?valideDu=2026-01-01", new StringContent(csv, Encoding.UTF8, "text/csv"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<Guid> Id(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> PosteExpose(string intitule, string codeRisque)
    {
        var posteId = await Id(await Employeur.PostAsJsonAsync("/api/v1/postes",
            new { affilieId = _affilie, intitule, description = "Poste de test", metierTypeCode = "EX.METIER" }, Ct));
        var propositionId = await Id(await Employeur.PostAsJsonAsync($"/api/v1/postes/{posteId}/propositions", new
        {
            motif = "Analyse de risques",
            valideDu = "2026-01-15",
            lignes = new[] { new { type = "Ajout", risqueCode = codeRisque, niveauExposition = "Eleve" } },
            avisCppt = new { date = "2026-01-10", documentId = Guid.CreateVersion7() },
        }, Ct));
        (await Cpmt.PostAsJsonAsync($"/api/v1/propositions-poste-risque/{propositionId}/validation", new { }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return posteId;
    }

    private async Task<int> Dispatcher<TEvent>(Guid messageId, TEvent integrationEvent)
        where TEvent : IntegrationEvent
    {
        var dispatcher = _factory.Services.GetRequiredService<IntegrationEventDispatcher<PostesRisquesDbContext>>();
        return await dispatcher.DispatchAsync(
            messageId,
            EventContractAttribute.Of(typeof(TEvent)).FullName,
            JsonSerializer.Serialize(integrationEvent, EventSerialization.Options),
            Ct);
    }

    [Fact]
    public async Task Les_sondes_de_sante_repondent_sans_authentification()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Une_requete_anonyme_est_refusee() =>
        (await _factory.CreateClient().GetAsync($"/api/v1/postes?affilieId={_affilie}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Le_referentiel_d_exemple_est_importe_une_seule_fois_et_consultable_en_neerlandais()
    {
        await ImporterJeuExemple();
        var second = await Client(Roles.AdministrateurFonctionnel).PostAsync(
            "/api/v1/risques/import", new StringContent(await File.ReadAllTextAsync(RepoCsv(), Ct), Encoding.UTF8, "text/csv"), Ct);
        (await second.Content.ReadFromJsonAsync<ImportRisquesDto>(Json, Ct))!.LignesInchangees.ShouldBe(12);

        var risque = await Cpmt.GetFromJsonAsync<RisqueDto>("/api/v1/risques/ex.phys.bruit?langue=nl&date=2026-06-01", Json, Ct);
        risque!.Libelle.ShouldBe("Voorbeeld — lawaai");
        risque.RegleApplicable!.ActesSupplementaires.ShouldBe(["EX.AUDIOMETRIE"]);

        (await Cpmt.PostAsync("/api/v1/risques/import", new StringContent("x", Encoding.UTF8, "text/csv"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Proposition_de_l_employeur_puis_validation_du_cpmt_publie_le_profil()
    {
        await ImporterJeuExemple();
        var posteId = await Id(await Employeur.PostAsJsonAsync("/api/v1/postes",
            new { affilieId = _affilie, intitule = "Cariste", description = (string?)null, metierTypeCode = "EX.CARISTE" }, Ct));
        var propositionId = await Id(await Employeur.PostAsJsonAsync($"/api/v1/postes/{posteId}/propositions", new
        {
            motif = "Nouveau chariot",
            valideDu = "2026-10-01",
            lignes = new[] { new { type = "Ajout", risqueCode = "EX.SECU.CONDUITE", niveauExposition = "Eleve" } },
        }, Ct));

        // Aucun effet avant la validation ; un non-CPMT ne peut pas valider.
        (await Employeur.GetFromJsonAsync<PosteDto>($"/api/v1/postes/{posteId}", Json, Ct))!.Risques.ShouldBeEmpty();
        var avis = new { dateAvisCppt = "2026-09-20", documentAvisCpptId = Guid.CreateVersion7() };
        (await Employeur.PostAsJsonAsync($"/api/v1/propositions-poste-risque/{propositionId}/validation", avis, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.GestionnaireDossiers).PostAsJsonAsync($"/api/v1/propositions-poste-risque/{propositionId}/validation", avis, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Sans avis du Comité PPT, la validation est refusée.
        var sansAvis = await Cpmt.PostAsJsonAsync($"/api/v1/propositions-poste-risque/{propositionId}/validation", new { }, Ct);
        sansAvis.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        sansAvis.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        (await Cpmt.PostAsJsonAsync($"/api/v1/propositions-poste-risque/{propositionId}/validation", avis, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var poste = await Employeur.GetFromJsonAsync<PosteDto>($"/api/v1/postes/{posteId}", Json, Ct);
        var lien = poste!.Risques.ShouldHaveSingleItem();
        lien.RisqueCode.ShouldBe("EX.SECU.CONDUITE");
        lien.ValideParCpmtId.ShouldBe("cpmt-1");
        lien.DocumentAvisCpptId.ShouldBe(avis.documentAvisCpptId);

        var proposition = await Employeur.GetFromJsonAsync<PropositionPosteRisqueDto>($"/api/v1/propositions-poste-risque/{propositionId}", Json, Ct);
        proposition!.Statut.ShouldBe(StatutProposition.Validee);
        proposition.Origine.ShouldBe(OrigineProposition.PortailEmployeur);

        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "postes-risques.profil-risque-poste-modifie.v1"), Ct);
        var message = publisher.Published.Single(m => m.EventType == "postes-risques.profil-risque-poste-modifie.v1");
        var evt = JsonSerializer.Deserialize<ProfilRisquePosteModifie>(message.Payload, EventSerialization.Options)!;
        evt.PosteId.ShouldBe(posteId);
        evt.CodesRisques.ShouldBe(["EX.SECU.CONDUITE"]);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PostesRisquesDbContext>();
        (await db.PropositionsPosteRisque.Where(p => EF.Property<string>(p, "updated_by") == "cpmt-1").CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Un_employeur_hors_perimetre_est_refuse()
    {
        var posteId = await Id(await Employeur.PostAsJsonAsync("/api/v1/postes",
            new { affilieId = _affilie, intitule = "Soudeur", description = (string?)null, metierTypeCode = (string?)null }, Ct));
        var etranger = Client(Roles.Employeur, "employeur-2", Guid.CreateVersion7());

        (await etranger.GetAsync($"/api/v1/postes/{posteId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await etranger.GetAsync($"/api/v1/postes?affilieId={_affilie}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await etranger.PostAsJsonAsync("/api/v1/postes", new { affilieId = _affilie, intitule = "X" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Travailleur).GetAsync($"/api/v1/postes?affilieId={_affilie}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Seul_le_cpmt_definit_une_surcharge_de_frequence()
    {
        await ImporterJeuExemple();
        var body = new
        {
            affilieId = _affilie,
            cibleType = "Personne",
            cibleId = Guid.CreateVersion7(),
            risqueCode = "EX.PHYS.BRUIT",
            frequenceMois = 12,
            motif = "Suivi rapproché",
            valideDu = "2026-10-01",
            valideJusquAu = "2027-10-01",
        };

        (await Employeur.PostAsJsonAsync("/api/v1/surcharges-frequence", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Infirmier).PostAsJsonAsync("/api/v1/surcharges-frequence", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Id(await Cpmt.PostAsJsonAsync("/api/v1/surcharges-frequence", body, Ct));
        (await Cpmt.PostAsJsonAsync("/api/v1/surcharges-frequence", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var surcharges = await Client(Roles.Infirmier).GetFromJsonAsync<JsonElement>($"/api/v1/surcharges-frequence?affilieId={_affilie}", Ct);
        surcharges.GetArrayLength().ShouldBe(1);
        (await Employeur.GetAsync($"/api/v1/surcharges-frequence?affilieId={_affilie}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Les_projections_sont_idempotentes_et_alimentent_la_liste_nominative()
    {
        await ImporterJeuExemple();
        var posteId = await PosteExpose("Conducteur", "EX.SECU.CONDUITE");
        var travailleur = Guid.CreateVersion7();
        var affectation = new AffectationModifiee(Guid.CreateVersion7(), travailleur, posteId, new DateOnly(2026, 2, 1), null);
        var messageAffectation = Guid.CreateVersion7();
        var examen = new ExamenCloture(Guid.CreateVersion7(), travailleur, _affilie, "EVALUATION_PERIODIQUE", new DateOnly(2026, 3, 4));
        var messageExamen = Guid.CreateVersion7();

        (await Dispatcher(messageAffectation, affectation)).ShouldBe(1);
        (await Dispatcher(messageAffectation, affectation)).ShouldBe(0);
        (await Dispatcher(Guid.CreateVersion7(), affectation)).ShouldBe(1);
        (await Dispatcher(messageExamen, examen)).ShouldBe(1);
        (await Dispatcher(messageExamen, examen)).ShouldBe(0);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PostesRisquesDbContext>();
            (await db.Affectations.CountAsync(Ct)).ShouldBe(1);
            (await db.Examens.CountAsync(Ct)).ShouldBe(1);
        }

        var generee = await Employeur.PostAsJsonAsync("/api/v1/listes-nominatives",
            new { affilieId = _affilie, type = "PosteSecurite", dateReference = "2026-09-28" }, Ct);
        generee.StatusCode.ShouldBe(HttpStatusCode.Created, await generee.Content.ReadAsStringAsync(Ct));
        var resume = (await generee.Content.ReadFromJsonAsync<ListeGenereeDto>(Json, Ct))!;
        resume.Version.ShouldBe(1);

        var liste = await Employeur.GetFromJsonAsync<ListeNominativeDto>($"/api/v1/listes-nominatives/{resume.Id}", Json, Ct);
        var ligne = liste!.Lignes.ShouldHaveSingleItem();
        ligne.PersonneId.ShouldBe(travailleur);
        ligne.PosteId.ShouldBe(posteId);
        ligne.DateDerniereEvaluation.ShouldBe(new DateOnly(2026, 3, 4));
        liste.ConserverJusquAu.Year.ShouldBeGreaterThanOrEqualTo(DateTime.UtcNow.Year + 5);

        // AFF-31 : l'employeur propose un retrait, le CPMT valide ; une nouvelle version est produite.
        var propositionId = await Id(await Employeur.PostAsJsonAsync($"/api/v1/listes-nominatives/{resume.Id}/propositions", new
        {
            motif = "Changement de fonction",
            lignes = new[] { new { type = "Retrait", personneId = travailleur, posteId } },
        }, Ct));
        (await Employeur.PostAsync($"/api/v1/propositions-liste-nominative/{propositionId}/validation", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var validee = await Cpmt.PostAsync($"/api/v1/propositions-liste-nominative/{propositionId}/validation", null, Ct);
        validee.StatusCode.ShouldBe(HttpStatusCode.OK, await validee.Content.ReadAsStringAsync(Ct));

        var historique = await Employeur.GetFromJsonAsync<List<ListeNominativeDto>>($"/api/v1/listes-nominatives?affilieId={_affilie}&type=PosteSecurite", Json, Ct);
        historique!.Select(l => (l.Version, l.NombreLignes)).ShouldBe([(2, 0), (1, 1)]);
        var proposition = await Employeur.GetFromJsonAsync<PropositionListeDto>($"/api/v1/propositions-liste-nominative/{propositionId}", Json, Ct);
        proposition!.Statut.ShouldBe(StatutProposition.Validee);
        proposition.TypeListe.ShouldBe(TypeListeNominative.PosteSecurite);
    }

    [Fact]
    public async Task Un_poste_inconnu_est_un_problem_details()
    {
        var response = await Cpmt.GetAsync($"/api/v1/postes/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    private static string RepoCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Sepp.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir.FullName, "src", "Services", "PostesRisques", "exemples", "risques-exemple.csv");
    }

    private static async Task Eventually(Func<bool> condition, CancellationToken ct)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, ct);
        }

        condition().ShouldBeTrue();
    }

    /// <summary>Authentification de test : rôles, utilisateur et affiliés autorisés passés dans des en-têtes.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";
        public const string UserHeader = "X-Test-User";
        public const string AffiliesHeader = "X-Test-Affilies";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Append(new Claim("sub", Request.Headers.TryGetValue(UserHeader, out var user) ? user.ToString() : "test-user"));
            if (Request.Headers.TryGetValue(AffiliesHeader, out var affilies))
            {
                claims = claims.Append(new Claim("affilie_id", affilies.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
