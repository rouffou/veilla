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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.BffEmployeur;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Planification;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Adapters.Persistence;
using Sepp.Obligations.Application.Consultation;
using Sepp.Obligations.Application.Gestion;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Obligations.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL dans un conteneur éphémère (ARC-23) : migrations, persistance,
/// outbox, réception idempotente et désordonnée des événements, API, permissions et périmètre de l'affilié.
/// </summary>
public sealed partial class ObligationsApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateTimeOffset Maintenant = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

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
            b.UseSetting("ConnectionStrings:Obligations", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Obligations:Traitement:Actif", "false");
            b.UseSetting("Obligations:Reprise:Actif", "false");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(new HorlogeFixe(Maintenant));
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

    private async Task<int> Livrer<TEvent>(Guid messageId, TEvent integrationEvent)
        where TEvent : IntegrationEvent
    {
        var dispatcher = _factory.Services.GetRequiredService<IntegrationEventDispatcher<ObligationsDbContext>>();
        return await dispatcher.DispatchAsync(
            messageId,
            EventContractAttribute.Of(typeof(TEvent)).FullName,
            JsonSerializer.Serialize(integrationEvent, EventSerialization.Options),
            Ct);
    }

    /// <summary>Un travailleur exposé au risque R1 d'un poste, et les événements qui décrivent sa situation.</summary>
    private sealed record Travailleur(Guid Id, Guid Poste, List<Func<Guid, Task<int>>> Evenements);

    private Travailleur NouveauTravailleur(string codeRisque = "R1", DateOnly? debutAffectation = null)
    {
        var personne = Guid.CreateVersion7();
        var poste = Guid.CreateVersion7();
        var occupation = Guid.CreateVersion7();
        var t0 = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var examen1 = Guid.CreateVersion7();
        var examen2 = Guid.CreateVersion7();
        var evenements = new List<Func<Guid, Task<int>>>
        {
            Evt(new RegleSurveillanceModifiee(Guid.CreateVersion7(), codeRisque, "Physique", 1, "EvaluationSantePeriodique", 12, false, new DateOnly(2026, 1, 1))),
            Evt(new ProfilRisquePosteModifie(poste, _affilie, [codeRisque], new DateOnly(2026, 1, 1)) { OccurredAt = t0 }),
            Evt(new AffectationModifiee(Guid.CreateVersion7(), personne, poste, debutAffectation ?? new DateOnly(2026, 2, 1), null) { OccurredAt = t0 }),
            Evt(new OccupationDebutee(occupation, personne, _affilie, new DateOnly(2025, 1, 1))),
            Evt(new ExamenCloture(examen1, personne, _affilie, "EVALUATION_PREALABLE", new DateOnly(2026, 1, 20))),
            Evt(new ExamenCloture(examen2, personne, _affilie, "EVALUATION_PERIODIQUE", new DateOnly(2027, 1, 10))),
        };
        return new Travailleur(personne, poste, evenements);
    }

    private Func<Guid, Task<int>> Evt<TEvent>(TEvent e)
        where TEvent : IntegrationEvent
    {
        return message => Livrer(message, e);
    }

    /// <summary>Livre les premiers événements du travailleur (règle, profil, affectation, occupation), chacun sous un nouvel identifiant.</summary>
    private static async Task Appliquer(Travailleur travailleur, int nombre = 4)
    {
        foreach (var livrer in travailleur.Evenements.Take(nombre))
        {
            await livrer(Guid.CreateVersion7());
        }
    }

    private async Task<List<ObligationDto>> Obligations(Guid personne, HttpClient? client = null) =>
        (await (client ?? Cpmt).GetFromJsonAsync<List<ObligationDto>>($"/api/v1/personnes/{personne}/obligations", Json, Ct))!;

    /// <summary>Résultat comparable : l'obligation sans ses identifiants propres au travailleur.</summary>
    private static List<string> Empreinte(IEnumerable<ObligationDto> obligations) =>
        obligations
            .Where(o => o.Statut != "SortiEntreprise" && !(o.Statut == "Annule" && o.MotifAnnulation == "Recalcul"))
            .Select(o => $"{o.Type}|{o.Origine}|{string.Join(',', o.CodesRisques)}|{o.DateDue}|{o.DateLimite}|{o.Statut}|{o.DateRealisation}")
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public async Task Les_sondes_de_sante_repondent_sans_authentification()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Une_requete_anonyme_est_refusee() =>
        (await _factory.CreateClient().GetAsync($"/api/v1/affilies/{_affilie}/obligations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Des_evenements_rejoues_en_double_et_dans_le_desordre_donnent_un_resultat_identique()
    {
        var nominal = NouveauTravailleur();
        var desordonne = NouveauTravailleur();

        await Appliquer(nominal, nominal.Evenements.Count);

        // Ordre inverse, chaque événement livré deux fois sous le même identifiant (l'inbox écarte le doublon)...
        foreach (var livrer in Enumerable.Reverse(desordonne.Evenements))
        {
            var message = Guid.CreateVersion7();
            (await livrer(message)).ShouldBe(1);
            (await livrer(message)).ShouldBe(0);
        }

        // ... puis republié sous de nouveaux identifiants (doublon applicatif) : sans effet sur le résultat.
        await Appliquer(desordonne, desordonne.Evenements.Count);

        var attendu = Empreinte(await Obligations(nominal.Id));
        attendu.Count.ShouldBeGreaterThanOrEqualTo(3);
        attendu.ShouldContain(e => e.StartsWith("EVALUATION_PERIODIQUE|", StringComparison.Ordinal));
        Empreinte(await Obligations(desordonne.Id)).ShouldBe(attendu);

        // Aucun doublon : une obligation par clé de déclencheur (index unique).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ObligationsDbContext>();
        var cles = await db.Obligations.Where(o => o.PersonneId == desordonne.Id).Select(o => o.Cle).ToListAsync(Ct);
        cles.Distinct().Count().ShouldBe(cles.Count);
    }

    [Fact]
    public async Task L_exposition_cree_l_evaluation_prealable_publiee_en_ObligationCreee_et_expliquee_par_la_trace()
    {
        var t = NouveauTravailleur();
        await Appliquer(t);

        var obligation = (await Obligations(t.Id)).ShouldHaveSingleItem();
        obligation.Type.ShouldBe("EVALUATION_PREALABLE");
        obligation.DateDue.ShouldBe(new DateOnly(2026, 2, 1));
        obligation.Statut.ShouldBe("APlanifier");
        obligation.Categorie.ShouldBe("EnRetard");

        var trace = await Cpmt.GetFromJsonAsync<TraceObligationDto>($"/api/v1/obligations/{obligation.Id}/trace", Json, Ct);
        var ligne = trace!.Traces.ShouldHaveSingleItem();
        ligne.Regle.ShouldBe("regle-surveillance:R1");
        ligne.Entrees.ShouldContain(e => e.Cle == "exposition_debut" && e.Valeur == "2026-02-01");

        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "obligations.obligation-creee.v1"), Ct);
        var evenement = JsonSerializer.Deserialize<ObligationCreee>(
            publisher.Published.Single(m => m.EventType == "obligations.obligation-creee.v1").Payload, EventSerialization.Options)!;
        evenement.ObligationId.ShouldBe(obligation.Id);
        evenement.TypeExamen.ShouldBe("EVALUATION_PREALABLE");
    }

    [Fact]
    public async Task Le_perimetre_de_l_affilie_et_la_confidentialite_des_types_sont_appliques_aux_profils_externes()
    {
        var t = NouveauTravailleur();
        await Appliquer(t);

        await Livrer(Guid.CreateVersion7(), new EtatParticulierDeclare(Guid.CreateVersion7(), t.Id, "PROTECTION_MATERNITE", new DateOnly(2026, 6, 1), null));

        var interne = await Obligations(t.Id);
        interne.Select(o => o.Type).ShouldBe(["EVALUATION_PREALABLE", "PROTECTION_MATERNITE"], ignoreOrder: true);

        var employeur = await Obligations(t.Id, Employeur);
        employeur.ShouldHaveSingleItem().Type.ShouldBe("EVALUATION_PREALABLE");

        var etranger = Client(Roles.Employeur, "employeur-2", Guid.CreateVersion7());
        (await Obligations(t.Id, etranger)).ShouldBeEmpty();
        (await etranger.GetAsync($"/api/v1/affilies/{_affilie}/obligations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await etranger.GetAsync($"/api/v1/affilies/{_affilie}/alertes", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Travailleur).GetAsync($"/api/v1/personnes/{t.Id}/obligations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await etranger.GetAsync($"/api/v1/obligations/{interne.Single(o => o.Type == "PROTECTION_MATERNITE").Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Employeur.GetAsync($"/api/v1/obligations/{interne.Single(o => o.Type == "PROTECTION_MATERNITE").Id}/trace", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact] public async Task Le_tableau_de_bord_de_l_employeur_distingue_dues_en_retard_et_planifiees() { var enRetard = NouveauTravailleur("R1"); var due = NouveauTravailleur("R2", new DateOnly(2026, 7, 10)); var planifiee = NouveauTravailleur("R3", new DateOnly(2026, 7, 20)); var aVenir = NouveauTravailleur("R4", new DateOnly(2027, 3, 1)); foreach (var t in new[] { enRetard, due, planifiee, aVenir }) { await Appliquer(t); } var obligationPlanifiee = (await Obligations(planifiee.Id)).Single(); await Livrer(Guid.CreateVersion7(), new RendezVousPlanifie(Guid.CreateVersion7(), planifiee.Id, _affilie, new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), [obligationPlanifiee.Id])); var synthese = await Employeur.GetFromJsonAsync<SyntheseObligationsDto>($"/api/v1/affilies/{_affilie}/obligations/synthese", Json, Ct); (synthese!.EnRetard, synthese.Dues, synthese.Planifiees, synthese.AVenir).ShouldBe((1, 1, 1, 1)); async Task<List<Guid>> Personnes(string categorie) => (await Employeur.GetFromJsonAsync<List<ObligationDto>>($"/api/v1/affilies/{_affilie}/obligations?categorie={categorie}", Json, Ct))!.Select(o => o.PersonneId).ToList(); (await Personnes("EnRetard")).ShouldBe([enRetard.Id]); (await Personnes("Due")).ShouldBe([due.Id]); (await Personnes("Planifiee")).ShouldBe([planifiee.Id]); (await Personnes("AVenir")).ShouldBe([aVenir.Id]); (await Employeur.GetAsync($"/api/v1/affilies/{_affilie}/obligations?categorie=Inconnue", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest); (await Employeur.GetAsync($"/api/v1/affilies/{_affilie}/obligations?type=INCONNU", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest); }
    [Fact]
    public async Task Les_statuts_suivent_la_machine_a_etats_et_les_rendez_vous_de_la_planification()
    {
        var t = NouveauTravailleur();
        await Appliquer(t);

        var obligation = (await Obligations(t.Id)).Single();
        var rendezVous = Guid.CreateVersion7();
        await Livrer(Guid.CreateVersion7(), new RendezVousPlanifie(rendezVous, t.Id, _affilie, new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), [obligation.Id]));
        (await Obligations(t.Id)).Single().Statut.ShouldBe("Planifie");

        (await Employeur.PostAsync($"/api/v1/obligations/{obligation.Id}/convocation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Cpmt.PostAsync($"/api/v1/obligations/{obligation.Id}/convocation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var conflit = await Cpmt.PostAsync($"/api/v1/obligations/{obligation.Id}/convocation", null, Ct);
        conflit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        conflit.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        (await Cpmt.PostAsync($"/api/v1/obligations/{obligation.Id}/absence", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Cpmt.PostAsJsonAsync($"/api/v1/obligations/{obligation.Id}/report", new { date = "2026-06-15" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Cpmt.PostAsJsonAsync($"/api/v1/obligations/{obligation.Id}/report", new { date = "2026-09-01" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Obligations(t.Id)).Single().Statut.ShouldBe("Reporte");

        // Le rendez-vous est annulé : le report est conservé (rien n'est planifié).
        await Livrer(Guid.CreateVersion7(), new RendezVousAnnule(rendezVous, t.Id, "ANNULE_PAR_TRAVAILLEUR") { OccurredAt = Maintenant.AddMinutes(1) });
        (await Obligations(t.Id)).Single().Statut.ShouldBe("Reporte");

        (await Cpmt.PostAsJsonAsync($"/api/v1/obligations/{obligation.Id}/annulation", new { motif = "Recalcul" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Cpmt.PostAsJsonAsync($"/api/v1/obligations/{obligation.Id}/annulation", new { motif = "DecisionCpmt" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var annulee = (await Obligations(t.Id)).Single();
        annulee.Statut.ShouldBe("Annule");
        annulee.MotifAnnulation.ShouldBe("DecisionCpmt");
        (await Cpmt.PostAsync($"/api/v1/obligations/{Guid.CreateVersion7()}/excuse", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Une_demande_du_travailleur_cree_une_obligation_a_dix_jours_ouvrables_sans_conserver_de_motif()
    {
        var personne = Guid.CreateVersion7();
        var corps = new { personneId = personne, affilieId = _affilie, type = "VISITE_PRE_REPRISE", dateDemande = "2026-06-01" };

        (await Employeur.PostAsJsonAsync("/api/v1/demandes-travailleur", corps, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Cpmt.PostAsJsonAsync("/api/v1/demandes-travailleur", new { personneId = personne, affilieId = _affilie, type = "EXAMEN_REPRISE" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Cpmt.PostAsJsonAsync("/api/v1/demandes-travailleur", new { personneId = personne, affilieId = _affilie, type = "bidon" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Cpmt.PostAsJsonAsync("/api/v1/demandes-travailleur", corps, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var obligation = (await Obligations(personne)).ShouldHaveSingleItem();
        obligation.Type.ShouldBe("VISITE_PRE_REPRISE");
        obligation.DateDue.ShouldBe(new DateOnly(2026, 6, 1));
        obligation.DateLimite.ShouldBe(new DateOnly(2026, 6, 15));

        // Type confidentiel : invisible pour l'employeur.
        (await Obligations(personne, Employeur)).ShouldBeEmpty();
    }

    [Fact]
    public async Task La_reprise_annoncee_et_les_parametres_de_referentiels_donnent_l_examen_de_reprise_et_ses_alertes()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);
        await Livrer(Guid.CreateVersion7(), new RepriseAnnoncee(personne, _affilie, new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 23)));
        (await Obligations(personne)).Single().DateLimite.ShouldBe(new DateOnly(2026, 5, 12));

        await Livrer(Guid.CreateVersion7(), new JoursFeriesModifies(2026, [new DateOnly(2026, 5, 4)]));
        (await Obligations(personne)).Single().DateLimite.ShouldBe(new DateOnly(2026, 5, 13));

        await Livrer(Guid.CreateVersion7(), new ParametreLegalModifie("SANTE.REPRISE.DELAI", 5, "JoursOuvrables", new DateOnly(2026, 1, 1), null));
        var obligation = (await Obligations(personne)).Single();
        obligation.DateLimite.ShouldBe(new DateOnly(2026, 5, 6));

        var trace = await Cpmt.GetFromJsonAsync<TraceObligationDto>($"/api/v1/obligations/{obligation.Id}/trace", Json, Ct);
        trace!.Traces.Count.ShouldBe(3);
        trace.Traces[0].Entrees.ShouldContain(e => e.Cle == "delai" && e.Valeur.Contains("Référentiels", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Le_traitement_periodique_publie_ObligationEchue_une_seule_fois()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);
        await Livrer(Guid.CreateVersion7(), new RepriseAnnoncee(personne, _affilie, new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 23)));
        (await Employeur.PostAsync("/api/v1/traitement-echeances", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var premier = await Cpmt.PostAsync("/api/v1/traitement-echeances", null, Ct);
        premier.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await premier.Content.ReadFromJsonAsync<ResultatTraitement>(Json, Ct))!.ObligationsEchues.ShouldBe(1);
        (await (await Cpmt.PostAsync("/api/v1/traitement-echeances", null, Ct)).Content.ReadFromJsonAsync<ResultatTraitement>(Json, Ct))!.ObligationsEchues.ShouldBe(0);

        var publisher = (InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "obligations.obligation-echue.v1"), Ct);
        publisher.Published.Count(m => m.EventType == "obligations.obligation-echue.v1").ShouldBe(1);
    }

    [Fact]
    public async Task Les_regroupables_et_les_alertes_sont_exposes_par_affilie()
    {
        var t = NouveauTravailleur();
        await Appliquer(t);

        await Livrer(Guid.CreateVersion7(), new ListeNominativeGeneree(Guid.CreateVersion7(), _affilie, "EXPOSES", 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 5)));

        var regroupables = await Cpmt.GetFromJsonAsync<List<PropositionRendezVousDto>>($"/api/v1/affilies/{_affilie}/obligations/regroupables?fenetreJours=45", Json, Ct);
        regroupables!.ShouldHaveSingleItem().PersonneId.ShouldBe(t.Id);
        (await Cpmt.GetAsync($"/api/v1/affilies/{_affilie}/obligations/regroupables?fenetreJours=999", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var alertes = await Employeur.GetFromJsonAsync<List<AlerteDto>>($"/api/v1/affilies/{_affilie}/alertes", Json, Ct);
        alertes!.Select(a => a.Type).ShouldBe(["TravailleurExposeSansSurveillancePlanifiee", "ListeNominativeNonRevue"], ignoreOrder: true);
        alertes!.Single(a => a.Type == "TravailleurExposeSansSurveillancePlanifiee").PersonneId.ShouldBe(t.Id);
    }

    [Fact]
    public async Task Le_recalcul_a_la_demande_et_la_validation_des_corps_de_requete()
    {
        var t = NouveauTravailleur();
        await Appliquer(t);

        (await Cpmt.PostAsJsonAsync("/api/v1/recalcul", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var reponse = await Cpmt.PostAsJsonAsync("/api/v1/recalcul", new { affilieId = _affilie }, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("personnesRecalculees").GetInt32().ShouldBe(1);
        (await Obligations(t.Id)).ShouldHaveSingleItem();
    }

    private static async Task Eventually(Func<bool> condition, CancellationToken ct)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, ct);
        }

        condition().ShouldBeTrue();
    }

    private sealed class HorlogeFixe(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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
