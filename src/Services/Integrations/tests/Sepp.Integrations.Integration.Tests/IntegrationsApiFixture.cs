using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Integrations.Adapters.External.Personnes;

using Testcontainers.PostgreSql;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL éphémère (ARC-23), simulateurs des organismes, et service Personnes
/// et fournisseur d'identité remplacés par un gestionnaire HTTP de test. Clé de chiffrement générée pour le test.
/// </summary>
public sealed class IntegrationsApiFixture : IAsyncLifetime
{
    public const string AdressePersonnes = "http://personnes.test/";
    public const string PointJeton = "http://idp.test/realms/veilla/protocol/openid-connect/token";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public JournalCapture Journal { get; } = new();

    public FauxPersonnes Personnes { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Integrations", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Encryption:CurrentKeyId", "test");
            b.UseSetting("Encryption:Keys:test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            b.UseSetting("Integrations:Adaptateurs:Bce", "Simulateur");
            b.UseSetting("Integrations:Adaptateurs:Dimona", "Simulateur");
            b.UseSetting("Integrations:Adaptateurs:RegistreNational", "Simulateur");
            b.UseSetting("Integrations:Planification:Actif", "false");
            b.UseSetting("Integrations:Conservation:ApresTraitement", "00:00:00");
            b.UseSetting("Integrations:Personnes:BaseAddress", AdressePersonnes);
            b.UseSetting("Integrations:Personnes:TokenEndpoint", PointJeton);
            b.UseSetting("Integrations:Personnes:ClientId", "veilla-integrations");
            b.UseSetting("Integrations:Personnes:ClientSecret", "secret-de-test");

            // Journalisation détaillée : le test vérifie qu'aucun NISS n'apparaît, même au niveau Debug.
            b.UseSetting("Logging:LogLevel:Default", "Debug");
            b.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Debug");
            b.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug");
            b.UseSetting("Logging:LogLevel:System.Net.Http", "Trace");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ILoggerProvider>(Journal);
                s.AddHttpClient(ClientsHttp.Personnes).ConfigurePrimaryHttpMessageHandler(() => Personnes).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
                s.AddHttpClient(ClientsHttp.JetonOidc).ConfigurePrimaryHttpMessageHandler(() => Personnes).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
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

    public HttpClient Client(string role)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, role);
        return client;
    }

    /// <summary>Authentification de test : rôles passés dans un en-tête.</summary>
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

/// <summary>
/// Service Personnes et fournisseur d'identité simulés : point de jeton client credentials, API interne DIMONA
/// idempotente sur la référence (comme le vrai service). Enregistre les requêtes reçues.
/// </summary>
public sealed partial class FauxPersonnes : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, (Guid PersonneId, Guid OccupationId)> _occupations = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Guid> _nissConnus = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _mutations = new(StringComparer.Ordinal);

    public ConcurrentQueue<(HttpMethod Methode, string Chemin, string? Autorisation, string Corps)> Requetes { get; } = new();

    private int _jetonsDelivres;

    public int JetonsDelivres => _jetonsDelivres;

    /// <summary>Déclare une personne connue de Personnes (sans quoi une mutation est signalée « personne inconnue »).</summary>
    public void ConnaitPersonne(string niss) => _nissConnus.TryAdd(niss, Guid.CreateVersion7());

    /// <summary>Si renseigné, toute requête vers Personnes reçoit ce statut (panne simulée).</summary>
    public HttpStatusCode? Panne { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corps = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var chemin = request.RequestUri!.AbsolutePath;
        Requetes.Enqueue((request.Method, chemin, request.Headers.Authorization?.ToString(), corps));

        if (request.RequestUri.Host == "idp.test")
        {
            Interlocked.Increment(ref _jetonsDelivres);
            return corps.Contains("grant_type=client_credentials") && corps.Contains("client_secret=secret-de-test")
                ? Json(HttpStatusCode.OK, """{"access_token":"jeton-test","expires_in":300,"token_type":"Bearer"}""")
                : Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        }

        if (request.Headers.Authorization?.ToString() != "Bearer jeton-test")
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (Panne is { } statut)
        {
            return Json(statut, """{"title":"personnes.interdit","code":"personnes.interdit","detail":"Accès refusé."}""", "application/problem+json");
        }

        if (chemin == "/api/v1/dimona/entrees")
        {
            using var document = JsonDocument.Parse(corps);
            var reference = document.RootElement.GetProperty("referenceDimona").GetString()!;
            _nissConnus.TryAdd(document.RootElement.GetProperty("niss").GetString()!, Guid.CreateVersion7());
            var deja = _occupations.ContainsKey(reference);
            var (personne, occupation) = _occupations.GetOrAdd(reference, _ => (Guid.CreateVersion7(), Guid.CreateVersion7()));
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { personneId = personne, occupationId = occupation, dejaEnregistree = deja }));
        }

        if (chemin == "/api/v1/registre-national/mutations")
        {
            // Comme le vrai service : idempotent sur la référence, personne inconnue tolérée.
            using var document = JsonDocument.Parse(corps);
            var reference = document.RootElement.GetProperty("referenceMutation").GetString()!;
            if (!_nissConnus.TryGetValue(document.RootElement.GetProperty("niss").GetString()!, out var personne))
            {
                return Json(HttpStatusCode.OK, """{"personneId":null,"statut":"PersonneInconnue"}""");
            }

            var nouvelle = _mutations.TryAdd(reference, personne);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { personneId = personne, statut = nouvelle ? "Appliquee" : "DejaAppliquee" }));
        }

        if (Sortie().Match(chemin) is { Success: true } sortie)
        {
            return _occupations.ContainsKey(Uri.UnescapeDataString(sortie.Groups[1].Value))
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : Json(HttpStatusCode.NotFound, """{"title":"occupation.inconnue","code":"occupation.inconnue","detail":"Aucune occupation pour cette référence."}""", "application/problem+json");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public IReadOnlyList<(HttpMethod Methode, string Chemin, string? Autorisation, string Corps)> AppelsPersonnes =>
        Requetes.Where(r => r.Chemin.StartsWith("/api/v1/", StringComparison.Ordinal)).ToList();

    private static HttpResponseMessage Json(HttpStatusCode statut, string json, string type = "application/json") =>
        new(statut) { Content = new StringContent(json, Encoding.UTF8, type) };

    [GeneratedRegex("^/api/v1/dimona/([^/]+)/sortie$")]
    private static partial Regex Sortie();
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
