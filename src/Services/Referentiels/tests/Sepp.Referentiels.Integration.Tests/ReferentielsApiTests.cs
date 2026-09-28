using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

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
using Sepp.Referentiels.Adapters.Persistence;
using Sepp.Referentiels.Application.Calendrier;
using Sepp.Referentiels.Application.Parametres;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Referentiels.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23) :
/// migrations, persistance, outbox, API et autorisations.
/// </summary>
public sealed class ReferentielsApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Referentiels", _postgres.GetConnectionString());
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

    private HttpClient Client(params string[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, string.Join(',', roles));
        return client;
    }

    [Fact]
    public async Task Les_sondes_de_sante_repondent_sans_authentification()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Une_requete_anonyme_est_refusee() =>
        (await _factory.CreateClient().GetAsync("/api/v1/parametres-legaux", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Les_parametres_legaux_initiaux_sont_disponibles_en_neerlandais()
    {
        var parametre = await Client(Roles.Cpmt).GetFromJsonAsync<ParametreDto>(
            "/api/v1/parametres-legaux/SANTE.REPRISE.DELAI?date=2026-09-28&langue=nl", TestContext.Current.CancellationToken);

        parametre!.ValeurCourante.ShouldBe(10);
        parametre.Libelle.ShouldStartWith("Termijn");
    }

    [Fact]
    public async Task Modifier_un_parametre_exige_l_administrateur_et_passe_par_l_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new { valeur = 12, valideDu = "2027-01-01" };

        (await Client(Roles.Cpmt).PostAsJsonAsync("/api/v1/parametres-legaux/SANTE.REPRISE.DELAI/valeurs", body, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await Client(Roles.AdministrateurFonctionnel).PostAsJsonAsync("/api/v1/parametres-legaux/SANTE.REPRISE.DELAI/valeurs", body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var parametre = await Client(Roles.Cpmt).GetFromJsonAsync<ParametreDto>("/api/v1/parametres-legaux/SANTE.REPRISE.DELAI?date=2027-02-01", ct);
        parametre!.ValeurCourante.ShouldBe(12);
        parametre.Historique.Count.ShouldBe(2);

        // L'outbox publie l'événement (publication en mémoire, aucun bus configuré).
        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "referentiels.parametre-legal-modifie.v1"), ct);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReferentielsDbContext>();
        var auditees = await db.ParametresLegaux
            .Where(p => EF.Property<string>(p, "updated_by") == "test-user")
            .CountAsync(ct);
        auditees.ShouldBe(1);
    }

    [Fact]
    public async Task L_echeance_en_jours_ouvrables_est_calculee()
    {
        var echeance = await Client(Roles.Planificateur).GetFromJsonAsync<EcheanceDto>(
            "/api/v1/calendrier/echeance?depart=2026-05-01&joursOuvrables=10", TestContext.Current.CancellationToken);

        echeance!.Echeance.ShouldBe(new DateOnly(2026, 5, 18));
    }

    [Fact]
    public async Task Une_erreur_fonctionnelle_est_un_problem_details()
    {
        var response = await Client(Roles.Cpmt).GetAsync("/api/v1/parametres-legaux/INCONNU", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    private static async Task Eventually(Func<bool> condition, CancellationToken ct)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, ct);
        }

        condition().ShouldBeTrue();
    }

    /// <summary>Authentification de test : les rôles sont passés dans un en-tête.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Append(new Claim("sub", "test-user"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
