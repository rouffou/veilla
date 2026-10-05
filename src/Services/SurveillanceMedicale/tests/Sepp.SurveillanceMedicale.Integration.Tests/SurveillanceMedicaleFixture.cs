using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.SurveillanceMedicale.Adapters.Persistence;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23), partagé par les tests de la classe.
/// Clé de chiffrement de la zone médicale générée pour le test (section ZoneMedicale:Encryption) ; journalisation au
/// niveau Debug capturée pour vérifier qu'aucun contenu clinique n'y figure.
/// </summary>
public sealed class SurveillanceMedicaleFixture : IAsyncLifetime
{
    public const string CleTest = "test-medical";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public JournalCapture Journal { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:SurveillanceMedicale", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "01:00:00");
            b.UseSetting("ZoneMedicale:Encryption:CurrentKeyId", CleTest);
            b.UseSetting($"ZoneMedicale:Encryption:Keys:{CleTest}", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            b.UseSetting("Logging:LogLevel:Default", "Debug");
            b.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Debug");
            b.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ILoggerProvider>(Journal);
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
    }

    public HttpClient Client(string role, string user, Guid? affilieId = null, string? motif = null, bool brisDeGlace = false)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, role);
        client.DefaultRequestHeaders.Add(TestAuth.UserHeader, user);
        if (affilieId is { } affilie)
        {
            client.DefaultRequestHeaders.Add(TestAuth.AffilieHeader, affilie.ToString());
        }

        if (motif is not null)
        {
            client.DefaultRequestHeaders.Add("X-Motif-Acces", Uri.EscapeDataString(motif));
        }

        if (brisDeGlace)
        {
            client.DefaultRequestHeaders.Add("X-Bris-De-Glace", "true");
        }

        return client;
    }

    public async Task<int> DispatcherAsync<TEvent>(Guid messageId, TEvent integrationEvent, CancellationToken ct)
        where TEvent : IntegrationEvent
    {
        var dispatcher = Factory.Services.GetRequiredService<IntegrationEventDispatcher<SurveillanceMedicaleDbContext>>();
        return await dispatcher.DispatchAsync(messageId, EventContractAttribute.Of(typeof(TEvent)).FullName,
            JsonSerializer.Serialize(integrationEvent, EventSerialization.Options), ct);
    }

    /// <summary>Traces d'audit (outbox, rubrique audit) portant sur un dossier : (action, type d'objet, motif, bris de glace).</summary>
    public async Task<List<(string Action, string ObjetType, string? Motif, bool BrisDeGlace, string Zone)>> TracesAsync(Guid dossierId, CancellationToken ct)
    {
        await using var connexion = new NpgsqlConnection(ConnectionString);
        await connexion.OpenAsync(ct);
        await using var commande = new NpgsqlCommand(
            """
            SELECT payload->>'action', payload->>'objetType', payload->>'motif', (payload->>'brisDeGlace')::boolean, payload->>'zone'
            FROM outbox_message WHERE event_type = 'audit.acces-donnee-sensible.v1' AND payload->>'objetId' = @id ORDER BY occurred_at, id
            """, connexion);
        commande.Parameters.AddWithValue("id", dossierId.ToString());
        await using var lecteur = await commande.ExecuteReaderAsync(ct);
        var traces = new List<(string, string, string?, bool, string)>();
        while (await lecteur.ReadAsync(ct))
        {
            traces.Add((lecteur.GetString(0), lecteur.GetString(1), lecteur.IsDBNull(2) ? null : lecteur.GetString(2), lecteur.GetBoolean(3), lecteur.GetString(4)));
        }

        return traces;
    }

    /// <summary>Charges utiles des événements métier (hors audit) de l'outbox.</summary>
    public async Task<List<(string Type, string Payload)>> EvenementsAsync(CancellationToken ct)
    {
        await using var connexion = new NpgsqlConnection(ConnectionString);
        await connexion.OpenAsync(ct);
        await using var commande = new NpgsqlCommand("SELECT event_type, payload::text FROM outbox_message WHERE topic <> 'audit' ORDER BY occurred_at", connexion);
        await using var lecteur = await commande.ExecuteReaderAsync(ct);
        var evenements = new List<(string, string)>();
        while (await lecteur.ReadAsync(ct))
        {
            evenements.Add((lecteur.GetString(0), lecteur.GetString(1)));
        }

        return evenements;
    }

    /// <summary>SQL brut : nombre de lignes, toutes tables confondues, dont la représentation texte contient la valeur.</summary>
    public async Task<long> OccurrencesEnBaseAsync(string valeur, CancellationToken ct)
    {
        await using var connexion = new NpgsqlConnection(ConnectionString);
        await connexion.OpenAsync(ct);
        var tables = new List<string>();
        await using (var liste = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", connexion))
        await using (var lecteur = await liste.ExecuteReaderAsync(ct))
        {
            while (await lecteur.ReadAsync(ct))
            {
                tables.Add(lecteur.GetString(0));
            }
        }

        tables.Count.ShouldBeGreaterThan(25);
        long total = 0;
        foreach (var table in tables)
        {
            await using var commande = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" t WHERE t::text ILIKE @motif", connexion);
            commande.Parameters.AddWithValue("motif", $"%{valeur}%");
            total += (long)(await commande.ExecuteScalarAsync(ct))!;
        }

        return total;
    }

    public async Task<object?> ScalaireAsync(string sql, CancellationToken ct, params (string Nom, object Valeur)[] parametres)
    {
        await using var connexion = new NpgsqlConnection(ConnectionString);
        await connexion.OpenAsync(ct);
        await using var commande = new NpgsqlCommand(sql, connexion);
        foreach (var (nom, valeur) in parametres)
        {
            commande.Parameters.AddWithValue(nom, valeur);
        }

        return await commande.ExecuteScalarAsync(ct);
    }

    /// <summary>Authentification de test : rôles, utilisateur et affilié (claim <c>affilie_id</c>) passés dans des en-têtes.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";
        public const string UserHeader = "X-Test-User";
        public const string AffilieHeader = "X-Test-Affilie";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Append(new Claim("sub", Request.Headers.TryGetValue(UserHeader, out var user) ? user.ToString() : "test-user"));
            if (Request.Headers.TryGetValue(AffilieHeader, out var affilie))
            {
                claims = claims.Append(new Claim("affilie_id", affilie.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}

/// <summary>Capture de tous les journaux (messages, valeurs structurées, exceptions).</summary>
public sealed class JournalCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entrees = new();

    public IReadOnlyCollection<string> Entrees => [.. _entrees];

    public ILogger CreateLogger(string categoryName) => new Capteur(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Capteur(JournalCapture journal, string categorie) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            journal._entrees.Enqueue($"{categorie} [scope] {Valeurs(state)}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            journal._entrees.Enqueue($"{categorie} {formatter(state, exception)} {Valeurs(state)} {exception}");

        private static string Valeurs<TState>(TState state) =>
            state is IEnumerable<KeyValuePair<string, object?>> valeurs
                ? string.Join(" ", valeurs.Select(v => $"{v.Key}={v.Value}"))
                : state?.ToString() ?? string.Empty;
    }
}
