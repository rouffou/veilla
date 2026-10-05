using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Testcontainers.PostgreSql;

namespace Sepp.Personnes.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23), partagé par les tests de la classe.
/// Clés de chiffrement et d'index aveugle générées pour le test : jamais de vraie clé.
/// </summary>
public sealed class PersonnesApiFixture : IAsyncLifetime
{
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
            b.UseSetting("ConnectionStrings:Personnes", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Encryption:CurrentKeyId", "test");
            b.UseSetting("Encryption:Keys:test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            b.UseSetting("BlindIndex:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

            // Journalisation détaillée : le test vérifie qu'aucun NISS n'apparaît, même au niveau Debug.
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

    public HttpClient Client(string role, Guid? affilieId = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, role);
        if (affilieId is { } affilie)
        {
            client.DefaultRequestHeaders.Add(TestAuth.AffilieHeader, affilie.ToString());
        }

        return client;
    }

    /// <summary>Authentification de test : rôles et affilié (claim <c>affilie_id</c>) passés dans des en-têtes.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";
        public const string AffilieHeader = "X-Test-Affilie";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Append(new Claim("sub", "test-user"));
            if (Request.Headers.TryGetValue(AffilieHeader, out var affilie))
            {
                claims = claims.Append(new Claim("affilie_id", affilie.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}

/// <summary>Capture de tous les journaux (messages, valeurs structurées, exceptions) pour vérifier l'absence de NISS.</summary>
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
