using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Sepp.Audit.Adapters.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts.Audit;

using Testcontainers.PostgreSql;

namespace Sepp.Audit.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23) : migrations et déclencheurs,
/// consommateur idempotent, API et autorisations. Un conteneur par test : certains tests altèrent la base.
/// </summary>
public abstract class AuditServiceTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Audit", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Audit:Conservation:PurgeActive", "false");
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
        _ = Factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected HttpClient Client(params string[] roles)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, string.Join(',', roles));
        return client;
    }

    protected static AccesDonneeSensible Acces(
        string zone = "medicale",
        string? motif = null,
        bool brisDeGlace = false,
        string utilisateur = "cpmt-42",
        Guid? objetId = null,
        DateTimeOffset? horodatage = null) =>
        new(zone == "psychosociale" ? "psychosocial" : "surveillance-medicale", zone, utilisateur, "cpmt", "lecture",
            zone == "psychosociale" ? "dossier-psychosocial" : "dossier-sante", objetId ?? Guid.CreateVersion7(), motif, brisDeGlace)
        {
            OccurredAt = horodatage ?? DateTimeOffset.UtcNow,
        };

    /// <summary>Remet un événement au consommateur comme le ferait le bus : même sérialisation que l'outbox émettrice.</summary>
    protected async Task<int> Recevoir(AccesDonneeSensible acces)
    {
        var message = OutboxMessage.From(acces);
        var dispatcher = Factory.Services.GetRequiredService<IntegrationEventDispatcher<AuditDbContext>>();
        return await dispatcher.DispatchAsync(message.Id, message.EventType, message.Payload, Ct);
    }

    protected async Task RecevoirTous(IEnumerable<AccesDonneeSensible> acces)
    {
        foreach (var a in acces)
        {
            await Recevoir(a);
        }
    }

    /// <summary>
    /// SQL brut sur la base du service, comme un administrateur de bases de données. Avec
    /// <paramref name="contournerDeclencheurs"/>, les déclencheurs sont désactivés pour la session (superutilisateur).
    /// </summary>
    protected async Task<int> Sql(string sql, bool contournerDeclencheurs = false)
    {
        await using var connexion = new NpgsqlConnection(_postgres.GetConnectionString());
        await connexion.OpenAsync(Ct);
        if (contournerDeclencheurs)
        {
            await using var bypass = new NpgsqlCommand("SET session_replication_role = replica", connexion);
            await bypass.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand(sql, connexion);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    protected async Task<long> Scalaire(string sql)
    {
        await using var connexion = new NpgsqlConnection(_postgres.GetConnectionString());
        await connexion.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connexion);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, Ct);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition non atteinte dans le délai imparti.");
        }
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
