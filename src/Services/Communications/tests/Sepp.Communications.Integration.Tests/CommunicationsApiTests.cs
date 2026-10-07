using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Communications.Adapters.Annuaire;
using Sepp.Communications.Adapters.Envois;
using Sepp.Communications.Adapters.Persistence;
using Sepp.Communications.Domain.Messages;
using Sepp.Contracts;
using Sepp.Contracts.Audit;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Planification;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Communications.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL éphémère, avec simulateurs de canaux ou, pour l'e-mail, un vrai serveur SMTP
/// (MailHog) : migrations, consommation des événements, journal, preuves, reprises, droits (ARC-23).
/// </summary>
public sealed class CommunicationsApiTests : IAsyncLifetime
{
    private static readonly Guid Personne = Guid.CreateVersion7();
    private static readonly Guid Dirigeant = Guid.Parse("0198a000-0000-7000-8000-00000000c001");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly List<IAsyncDisposable> _ressources = [];
    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync() => await _postgres.StartAsync(_ct);

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        foreach (var ressource in _ressources)
        {
            await ressource.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private void Demarrer(Dictionary<string, string>? supplementaires = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Communications", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Communications:Expedition:Actif", "false");
            foreach (var canal in new[] { "Annuaire", "Portail", "Email", "Sms", "Courrier", "RecommandeElectronique", "EBoxEntreprise", "EBoxCitoyen" })
            {
                b.UseSetting($"Communications:Adaptateurs:{canal}", "Simulateur");
            }

            b.UseSetting("Communications:Annuaire:Dirigeants:medicale:0:Id", Dirigeant.ToString());
            b.UseSetting("Communications:Annuaire:Dirigeants:medicale:0:Email", "cpmt-dirigeant@exemple.test");
            b.UseSetting("Communications:Liens:PortailTravailleur", "https://travailleur.exemple.test/messages");
            foreach (var (cle, valeur) in supplementaires ?? [])
            {
                b.UseSetting(cle, valeur);
            }

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

    private HttpClient Client(params string[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, string.Join(',', roles));
        return client;
    }

    private async Task<int> Dispatcher<TEvent>(TEvent evenement, Guid? messageId = null)
        where TEvent : IntegrationEvent
    {
        var dispatcher = _factory.Services.GetRequiredService<IntegrationEventDispatcher<CommunicationsDbContext>>();
        return await dispatcher.DispatchAsync(messageId ?? Guid.CreateVersion7(), EventContractAttribute.Of(typeof(TEvent)).FullName,
            JsonSerializer.Serialize(evenement, EventSerialization.Options), _ct);
    }

    private async Task Expedier() =>
        (await Client(Roles.GestionnaireDossiers).PostAsync("/api/v1/messages/expedition", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

    private async Task<JsonElement> Messages(string requete = "") =>
        await Client(Roles.GestionnaireDossiers).GetFromJsonAsync<JsonElement>($"/api/v1/messages?{requete}", Json, _ct);

    [Fact]
    public async Task Les_sondes_repondent_et_une_requete_anonyme_est_refusee()
    {
        Demarrer();

        (await _factory.CreateClient().GetAsync("/health/ready", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _factory.CreateClient().GetAsync("/api/v1/messages", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client(Roles.Employeur).GetAsync("/api/v1/messages", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Travailleur).GetAsync("/api/v1/messages", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_document_publie_donne_un_message_envoye_avec_preuve_de_facon_idempotente()
    {
        Demarrer();
        var evenement = new DocumentPublie(Guid.CreateVersion7(), "medicale", "personne", Personne, "SANTE.EVALUATION.TRAVAILLEUR");
        var message = Guid.CreateVersion7();

        (await Dispatcher(evenement, message)).ShouldBe(1);
        (await Dispatcher(evenement, message)).ShouldBe(0);
        // Même fait métier annoncé sous un autre identifiant de message : une seule notification.
        await Dispatcher(evenement);
        await Expedier();
        await Expedier();

        var page = await Messages($"destinataireId={Personne}");
        page.GetProperty("total").GetInt32().ShouldBe(1);
        var resume = page.GetProperty("messages")[0];
        resume.GetProperty("statut").GetString().ShouldBe("Envoye");
        resume.GetProperty("canal").GetString().ShouldBe("Email");

        var detail = await Client(Roles.GestionnaireDossiers).GetFromJsonAsync<JsonElement>($"/api/v1/messages/{resume.GetProperty("id").GetGuid()}", Json, _ct);
        detail.GetProperty("preuves").GetArrayLength().ShouldBe(1);
        detail.GetProperty("preuves")[0].GetProperty("empreinte").GetString()!.Length.ShouldBe(64);

        var boite = _factory.Services.GetRequiredService<BoiteEnvoiSimulee>();
        var email = boite.Envoyes.Single(e => e.DestinataireId == Personne);
        $"{email.Sujet}{email.Corps}".ShouldNotContain("SANTE", Case.Insensitive);
        $"{email.Sujet}{email.Corps}".ShouldNotContain("medicale", Case.Insensitive);
    }

    [Fact]
    public async Task Un_echec_temporaire_est_repris_puis_un_abandon_peut_etre_relance()
    {
        Demarrer();
        var boite = _factory.Services.GetRequiredService<BoiteEnvoiSimulee>();
        boite.ProgrammerEchec(Canal.Email, "smtp-indisponible", definitif: false);
        await Dispatcher(new DocumentPublie(Guid.CreateVersion7(), "standard", "personne", Personne, "COURRIER"));

        await Expedier();

        var message = (await Messages($"destinataireId={Personne}")).GetProperty("messages")[0];
        message.GetProperty("statut").GetString().ShouldBe("EnEchec");
        message.GetProperty("derniereErreur").GetString().ShouldBe("smtp-indisponible");
        message.GetProperty("prochaineTentative").ValueKind.ShouldBe(JsonValueKind.String);

        // Erreur définitive : abandon, puis relance par le gestionnaire.
        var id = message.GetProperty("id").GetGuid();
        (await Client(Roles.GestionnaireDossiers).PostAsync($"/api/v1/messages/{id}/relance", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            var charge = await db.Messages.FindAsync([id], _ct);
            charge!.EnregistrerEchec(DateTimeOffset.UtcNow, "adresse-email-invalide", definitif: true, PolitiqueReprise.Defaut);
            await db.SaveChangesAsync(_ct);
        }

        (await Client(Roles.Cpmt).PostAsync($"/api/v1/messages/{id}/relance", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.GestionnaireDossiers).PostAsync($"/api/v1/messages/{id}/relance", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Expedier();
        (await Messages($"destinataireId={Personne}")).GetProperty("messages")[0].GetProperty("statut").GetString().ShouldBe("Envoye");
    }

    [Fact]
    public async Task Une_convocation_recommandee_part_par_deux_canaux_et_l_annulation_previent_le_travailleur()
    {
        Demarrer();
        var rendezVous = Guid.CreateVersion7();
        var debut = new DateTimeOffset(2026, 10, 12, 7, 30, 0, TimeSpan.Zero);
        await Dispatcher(new ConvocationEmise(Guid.CreateVersion7(), rendezVous, Personne, Guid.CreateVersion7(), Guid.CreateVersion7(), "VISITE", debut, "Sms", true, "Convocation", null));
        await Dispatcher(new RendezVousAnnule(Guid.CreateVersion7(), Personne, "ABSENCE"));
        await Expedier();

        var messages = (await Messages($"destinataireId={Personne}")).GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("canal").GetString()).Order().ShouldBe(["Email", "RecommandeElectronique", "Sms"]);
        messages.ShouldAllBe(m => m.GetProperty("statut").GetString() == "Envoye");

        var recommande = _factory.Services.GetRequiredService<BoiteEnvoiSimulee>().Envoyes.Single(e => e.Canal == Canal.RecommandeElectronique);
        recommande.Recommande.ShouldBeTrue();
        recommande.Corps.ShouldContain("lundi 12 octobre 2026 à 09:30");
    }

    [Fact]
    public async Task Un_bris_de_glace_alerte_le_dirigeant_configure()
    {
        Demarrer();

        await Dispatcher(new BrisDeGlaceSignale(Guid.CreateVersion7(), "medicale", "surveillance-medicale", "u1", "dossier-sante", Guid.CreateVersion7()));
        await Expedier();

        var page = await Messages($"destinataireId={Dirigeant}");
        page.GetProperty("total").GetInt32().ShouldBe(1);
        page.GetProperty("messages")[0].GetProperty("typeDestinataire").GetString().ShouldBe("Interne");
        page.GetProperty("messages")[0].GetProperty("statut").GetString().ShouldBe("Envoye");
    }

    [Fact]
    public async Task L_envoi_manuel_est_reserve_et_ne_transmet_aucun_texte_libre_par_email()
    {
        Demarrer();
        var corps = new { typeDestinataire = "Personne", destinataireId = Personne, canal = "Email", recommande = false, sujet = "Diagnostic", corps = "Vous avez une maladie." };

        (await Client(Roles.Direction).PostAsJsonAsync("/api/v1/messages", corps, Json, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reponse = await Client(Roles.Planificateur).PostAsJsonAsync("/api/v1/messages", corps, Json, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        await Expedier();

        var email = _factory.Services.GetRequiredService<BoiteEnvoiSimulee>().Envoyes.Single(e => e.DestinataireId == Personne);
        $"{email.Sujet}{email.Corps}".ShouldNotContain("maladie", Case.Insensitive);
        $"{email.Sujet}{email.Corps}".ShouldNotContain("Diagnostic", Case.Insensitive);
    }

    [Fact]
    public async Task Un_destinataire_inconnu_est_visible_dans_le_journal_comme_abandonne()
    {
        Demarrer();
        var inconnu = Guid.CreateVersion7();
        _factory.Services.GetRequiredService<AnnuaireSimule>().Supprimer(TypeDestinataire.Personne, inconnu);

        await Dispatcher(new DocumentPublie(Guid.CreateVersion7(), "standard", "personne", inconnu, "COURRIER"));

        var message = (await Messages($"destinataireId={inconnu}")).GetProperty("messages")[0];
        message.GetProperty("statut").GetString().ShouldBe("Abandonne");
        message.GetProperty("derniereErreur").GetString().ShouldBe("destinataire-inconnu");
    }

    [Fact]
    public async Task L_email_reel_est_depose_par_SMTP_sans_donnee_sensible()
    {
        var mailhog = new ContainerBuilder("mailhog/mailhog:v1.0.1")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1025).UntilInternalTcpPortIsAvailable(8025))
            .Build();
        _ressources.Add(mailhog);
        await mailhog.StartAsync(_ct);
        Demarrer(new Dictionary<string, string>
        {
            ["Communications:Adaptateurs:Email"] = "Reel",
            ["Communications:Smtp:Hote"] = mailhog.Hostname,
            ["Communications:Smtp:Port"] = mailhog.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Communications:Smtp:Securite"] = "None",
            ["Communications:Smtp:Expediteur"] = "no-reply@sepp.test",
        });

        await Dispatcher(new DocumentPublie(Guid.CreateVersion7(), "medicale", "personne", Personne, "SANTE.EVALUATION.TRAVAILLEUR"));
        await Expedier();

        var message = (await Messages($"destinataireId={Personne}")).GetProperty("messages")[0];
        message.GetProperty("statut").GetString().ShouldBe("Envoye");
        using var http = new HttpClient();
        var recus = await http.GetFromJsonAsync<JsonElement>(
            new Uri($"http://{mailhog.Hostname}:{mailhog.GetMappedPublicPort(8025)}/api/v2/messages"), _ct);
        recus.GetProperty("total").GetInt32().ShouldBe(1);
        var recu = recus.GetProperty("items")[0];
        recu.GetProperty("Raw").GetProperty("To")[0].GetString()!.ShouldContain("@exemple.test");
        var brut = recu.GetProperty("Raw").GetProperty("Data").GetString()!;
        brut.ShouldContain("https://travailleur.exemple.test/messages/");
        brut.ShouldNotContain("SANTE", Case.Insensitive);
        brut.ShouldNotContain("medicale", Case.Insensitive);
        brut.ShouldContain("Auto-Submitted: auto-generated");

        var detail = await Client(Roles.GestionnaireDossiers).GetFromJsonAsync<JsonElement>($"/api/v1/messages/{message.GetProperty("id").GetGuid()}", Json, _ct);
        detail.GetProperty("preuves")[0].GetProperty("type").GetString().ShouldBe("accuse-depot-smtp");
    }

    [Fact]
    public async Task Un_serveur_SMTP_injoignable_est_une_reprise()
    {
        Demarrer(new Dictionary<string, string>
        {
            ["Communications:Adaptateurs:Email"] = "Reel",
            ["Communications:Smtp:Hote"] = "127.0.0.1",
            ["Communications:Smtp:Port"] = "1",
            ["Communications:Smtp:Securite"] = "None",
            ["Communications:Smtp:Expediteur"] = "no-reply@sepp.test",
        });

        await Dispatcher(new DocumentPublie(Guid.CreateVersion7(), "standard", "personne", Personne, "COURRIER"));
        await Expedier();

        var message = (await Messages($"destinataireId={Personne}")).GetProperty("messages")[0];
        message.GetProperty("statut").GetString().ShouldBe("EnEchec");
        message.GetProperty("derniereErreur").GetString()!.ShouldStartWith("smtp-indisponible");
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
