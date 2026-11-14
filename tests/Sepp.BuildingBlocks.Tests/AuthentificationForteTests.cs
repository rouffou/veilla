using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

// NF-01 : authentification forte (MFA, eID, itsme) pour signer et pour accéder aux données médicales et psychosociales.
public sealed class AuthentificationForteTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Permissions.DossierSanteLire, true)]
    [InlineData(Permissions.DossierSanteEcrire, true)]
    [InlineData(Permissions.DossierSantePurger, true)]
    [InlineData(Permissions.CopieDossierTraiter, true)]
    [InlineData(Permissions.DossierPsyLire, true)]
    [InlineData(Permissions.DossierPsyEcrire, true)]
    [InlineData(Permissions.CopieDossierDemander, false)]
    [InlineData(Permissions.DossierSanteTransmettreDocuments, false)]
    [InlineData(Permissions.DecisionEcrire, false)]
    [InlineData(Permissions.AffilieLire, false)]
    public void Les_permissions_des_dossiers_sante_et_psy_exigent_l_authentification_forte(string permission, bool exigee)
    {
        AuthenticationStrength.IsRequiredFor(permission).ShouldBe(exigee);
    }

    [Fact]
    public void Toute_permission_dossier_sante_ou_psy_hors_exemptions_exige_l_authentification_forte()
    {
        // Une permission ajoutée plus tard dans ces zones est couverte sans modification de la politique.
        var sensibles = typeof(Permissions).GetFields().Select(f => (string)f.GetRawConstantValue()!)
            .Where(p => p.StartsWith("dossier-sante:", StringComparison.Ordinal) || p.StartsWith("dossier-psy:", StringComparison.Ordinal))
            .Except([Permissions.CopieDossierDemander, Permissions.DossierSanteTransmettreDocuments])
            .ToList();

        sensibles.ShouldNotBeEmpty();
        sensibles.ShouldAllBe(p => AuthenticationStrength.IsRequiredFor(p));
    }

    [Theory]
    [InlineData("acr", "mfa", true)]
    [InlineData("acr", "password", false)]
    [InlineData("acr", "1", false)]
    [InlineData("amr", "otp", true)]
    [InlineData("amr", "pwd", false)]
    [InlineData("acrs", "c1", true)]
    [InlineData("acrs", "c2", false)]
    public void Un_jeton_est_fort_s_il_porte_une_valeur_acceptee(string claim, string valeur, bool fort)
    {
        var options = new AuthenticationStrengthOptions { AcceptedAcr = ["mfa"], AcceptedAmr = ["otp", "mfa"], AcceptedAuthenticationContexts = ["c1"] };

        AuthenticationStrength.IsSatisfiedBy(Utilisateur((claim, valeur)), options, Maintenant).ShouldBe(fort);
    }

    [Fact]
    public void Une_seule_methode_amr_acceptee_suffit()
    {
        var options = new AuthenticationStrengthOptions { AcceptedAmr = ["mfa"] };

        AuthenticationStrength.IsSatisfiedBy(Utilisateur(("amr", "pwd"), ("amr", "mfa")), options, Maintenant).ShouldBeTrue();
        AuthenticationStrength.IsSatisfiedBy(Utilisateur(("amr", "pwd")), options, Maintenant).ShouldBeFalse();
        AuthenticationStrength.IsSatisfiedBy(Utilisateur(), options, Maintenant).ShouldBeFalse();
    }

    [Fact]
    public void L_anciennete_maximale_s_appuie_sur_auth_time()
    {
        var options = new AuthenticationStrengthOptions { AcceptedAcr = ["mfa"], MaxAge = TimeSpan.FromMinutes(15) };
        static string Il_y_a(int minutes) => Maintenant.AddMinutes(-minutes).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        AuthenticationStrength.IsSatisfiedBy(Utilisateur(("acr", "mfa"), ("auth_time", Il_y_a(5))), options, Maintenant).ShouldBeTrue();
        AuthenticationStrength.IsSatisfiedBy(Utilisateur(("acr", "mfa"), ("auth_time", Il_y_a(30))), options, Maintenant).ShouldBeFalse();
        AuthenticationStrength.IsSatisfiedBy(Utilisateur(("acr", "mfa")), options, Maintenant).ShouldBeFalse("Sans auth_time, l'ancienneté ne peut être prouvée.");
    }

    [Fact]
    public void Le_defi_suit_la_RFC_9470_et_porte_la_demande_de_contexte_Entra()
    {
        var local = AuthenticationStrength.Challenge(new AuthenticationStrengthOptions { AcceptedAcr = ["mfa"], ChallengeAcrValues = "mfa", MaxAge = TimeSpan.FromMinutes(15) });
        local.ShouldStartWith("Bearer ");
        local.ShouldContain("error=\"insufficient_user_authentication\"");
        local.ShouldContain("acr_values=\"mfa\"");
        local.ShouldContain("max_age=900");
        local.ShouldNotContain("claims=");

        var azure = AuthenticationStrength.Challenge(new AuthenticationStrengthOptions { AcceptedAmr = ["mfa"], AcceptedAuthenticationContexts = ["c1"] });
        azure.ShouldNotContain("acr_values");
        var claims = azure[(azure.IndexOf("claims=\"", StringComparison.Ordinal) + 8)..].TrimEnd('"');
        Encoding.UTF8.GetString(Convert.FromBase64String(claims)).ShouldBe("""{"access_token":{"acrs":{"essential":true,"value":"c1"}}}""");
    }

    [Fact]
    public async Task En_mode_strict_une_route_dossier_sante_sans_MFA_repond_401_avec_le_niveau_requis()
    {
        await using var hote = await Hote.DemarrerAsync("Testing", ("Authentication:Strength:Mode", "Enforce"));

        var reponse = await hote.Client.SendAsync(Requete("/lire", "cpmt", ("acr", "password")), TestContext.Current.CancellationToken);

        reponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var defi = reponse.Headers.WwwAuthenticate.ToString();
        defi.ShouldContain("error=\"insufficient_user_authentication\"");
        defi.ShouldContain("acr_values=\"mfa\"");
        var corps = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        corps.GetProperty("code").GetString().ShouldBe("authentification.niveau-insuffisant");
        corps.GetProperty("acr_values").GetString().ShouldBe("mfa");
    }

    [Theory]
    [InlineData("acr", "mfa")]
    [InlineData("amr", "otp")]
    public async Task En_mode_strict_une_authentification_forte_ouvre_la_route(string claim, string valeur)
    {
        await using var hote = await Hote.DemarrerAsync("Testing", ("Authentication:Strength:Mode", "Enforce"));

        (await hote.Client.SendAsync(Requete("/lire", "cpmt", (claim, valeur)), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await hote.Client.SendAsync(Requete("/signer", "cpmt", (claim, valeur)), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task En_mode_strict_la_politique_explicite_protege_la_signature()
    {
        await using var hote = await Hote.DemarrerAsync("Testing", ("Authentication:Strength:Mode", "Enforce"));

        (await hote.Client.SendAsync(Requete("/signer", "cpmt"), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sans_la_permission_la_reponse_reste_403_meme_sans_MFA()
    {
        await using var hote = await Hote.DemarrerAsync("Testing", ("Authentication:Strength:Mode", "Enforce"));

        var reponse = await hote.Client.SendAsync(Requete("/lire", Roles.GestionnaireDossiers), TestContext.Current.CancellationToken);

        reponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        reponse.Headers.WwwAuthenticate.ShouldBeEmpty();
    }

    [Fact]
    public async Task Les_routes_hors_zones_sensibles_ne_sont_pas_concernees()
    {
        await using var hote = await Hote.DemarrerAsync("Testing", ("Authentication:Strength:Mode", "Enforce"));

        (await hote.Client.SendAsync(Requete("/affilie", Roles.GestionnaireDossiers), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task En_local_le_mode_par_defaut_est_souple_et_journalise()
    {
        await using var hote = await Hote.DemarrerAsync("Development");

        var reponse = await hote.Client.SendAsync(Requete("/lire", "cpmt", ("acr", "password")), TestContext.Current.CancellationToken);

        reponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        hote.Journal.ShouldContain(e => e.Contains("NF-01", StringComparison.Ordinal) && e.Contains("/lire", StringComparison.Ordinal) && e.Contains("acr=password", StringComparison.Ordinal));
        var options = hote.Services.GetRequiredService<IOptions<AuthenticationStrengthOptions>>().Value;
        options.Mode.ShouldBe(AuthenticationStrengthMode.Log);
        options.AcceptedAcr.ShouldBe(["mfa"]);
        options.AcceptedAmr.ShouldBe(["otp", "mfa"]);
        options.ChallengeAcrValues.ShouldBe("mfa");
    }

    [Fact]
    public async Task Hors_developpement_le_mode_par_defaut_est_strict_avec_les_valeurs_Entra()
    {
        await using var hote = await Hote.DemarrerAsync("Production",
            ("Authentication:Strength:AcceptedAmr:0", "mfa"), ("Authentication:Strength:AcceptedAuthenticationContexts:0", "c1"));

        var refus = await hote.Client.SendAsync(Requete("/lire", "cpmt", ("amr", "pwd")), TestContext.Current.CancellationToken);
        refus.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refus.Headers.WwwAuthenticate.ToString().ShouldContain("claims=");
        (await hote.Client.SendAsync(Requete("/lire", "cpmt", ("acrs", "c1")), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await hote.Client.SendAsync(Requete("/lire", "cpmt", ("amr", "pwd"), ("amr", "mfa")), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await hote.Client.SendAsync(Requete("/lire", "cpmt", ("amr", "otp")), TestContext.Current.CancellationToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized, "Les valeurs de démonstration locale ne s'appliquent pas hors développement.");
    }

    [Fact]
    public async Task Hors_developpement_le_demarrage_echoue_sans_valeur_acceptee()
    {
        await Should.ThrowAsync<OptionsValidationException>(async () => await (await Hote.DemarrerAsync("Production")).DisposeAsync());
    }

    [Fact]
    public async Task Un_contexte_d_authentification_invalide_empeche_le_demarrage()
    {
        await Should.ThrowAsync<OptionsValidationException>(async () =>
            await (await Hote.DemarrerAsync("Production", ("Authentication:Strength:AcceptedAuthenticationContexts:0", "mfa"))).DisposeAsync());
    }

    private static ClaimsPrincipal Utilisateur(params (string Type, string Valeur)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Valeur)), "Test"));

    private static HttpRequestMessage Requete(string chemin, string role, params (string Type, string Valeur)[] claims)
    {
        var requete = new HttpRequestMessage(HttpMethod.Get, chemin);
        requete.Headers.Add(AuthentificationEnTetes.Roles, role);
        foreach (var (type, valeur) in claims)
        {
            requete.Headers.Add(AuthentificationEnTetes.Prefixe + type, valeur);
        }

        return requete;
    }

    /// <summary>Hôte minimal avec le gabarit commun des services, une authentification de test et trois routes.</summary>
    private sealed class Hote : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Hote(WebApplication app, JournalTest journal)
        {
            _app = app;
            Journal = journal.Entrees;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IReadOnlyCollection<string> Journal { get; }

        public IServiceProvider Services => _app.Services;

        public static async Task<Hote> DemarrerAsync(string environnement, params (string Cle, string Valeur)[] configuration)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environnement });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(configuration.Select(c => new KeyValuePair<string, string?>(c.Cle, c.Valeur)));
            builder.AddSeppServiceDefaults("test-authentification-forte");
            var journal = new JournalTest();
            builder.Logging.AddProvider(journal);
            builder.Services.AddAuthentication(AuthentificationEnTetes.Schema)
                .AddScheme<AuthenticationSchemeOptions, AuthentificationEnTetes>(AuthentificationEnTetes.Schema, _ => { });
            builder.Services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AuthentificationEnTetes.Schema;
                o.DefaultChallengeScheme = AuthentificationEnTetes.Schema;
            });

            var app = builder.Build();
            app.MapSeppServiceDefaults();
            app.MapGet("/lire", () => Results.Ok()).RequirePermission(Permissions.DossierSanteLire);
            app.MapGet("/signer", () => Results.Ok()).RequirePermission(Permissions.DecisionEcrire).RequireAuthenticationStrength();
            app.MapGet("/affilie", () => Results.Ok()).RequirePermission(Permissions.AffilieLire);
            await app.StartAsync(TestContext.Current.CancellationToken);
            return new Hote(app, journal);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    /// <summary>Rôles dans <c>X-Test-Roles</c>, autres claims dans <c>X-Test-Claim-&lt;type&gt;</c>.</summary>
    private sealed class AuthentificationEnTetes(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Schema = "Test";
        public const string Roles = "X-Test-Roles";
        public const string Prefixe = "X-Test-Claim-";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Roles, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',').Select(r => new Claim("roles", r)).Append(new Claim("sub", "test-user")).ToList();
            foreach (var (nom, valeurs) in Request.Headers.Where(h => h.Key.StartsWith(Prefixe, StringComparison.OrdinalIgnoreCase)))
            {
                claims.AddRange(valeurs.Select(v => new Claim(nom[Prefixe.Length..].ToLowerInvariant(), v!)));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Schema, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Schema)));
        }
    }

    private sealed class JournalTest : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entrees = new();

        public IReadOnlyCollection<string> Entrees => _entrees;

        public ILogger CreateLogger(string categoryName) => new Capteur(_entrees);

        public void Dispose()
        {
        }

        private sealed class Capteur(ConcurrentQueue<string> entrees) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entrees.Enqueue(formatter(state, exception));
        }
    }
}
