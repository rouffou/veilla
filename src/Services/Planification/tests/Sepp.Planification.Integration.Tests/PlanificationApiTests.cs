using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts.Obligations;
using Sepp.Planification.Adapters.External;
using Sepp.Planification.Adapters.Persistence;
using Sepp.Planification.Application.Projections;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Planification.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL éphémère (ARC-23) : migrations (contrainte d'exclusion), persistance, outbox,
/// API, autorisations, périmètre des externes, absence de double réservation sous concurrence. L'horloge du service est
/// décalée au vendredi 18 décembre 2026 : Noël et le nouvel an comptent dans le délai légal des urgences.
/// </summary>
public sealed class PlanificationApiTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Maintenant = new(2026, 12, 18, 8, 0, 0, TimeSpan.FromHours(1));

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Planification", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Planification:Adaptateurs:OutilRh", "Simulateur");
            b.UseSetting("Planification:Adaptateurs:AgendaExterne", "Simulateur");
            b.UseSetting("Planification:Taches:Actif", "false");
            b.UseSetting("Planification:Simulateurs:Conges:0:ReferenceExterne", "RH-2027-01");
            b.UseSetting("Planification:Simulateurs:Conges:0:ReferenceRessource", "kc-en-conge");
            b.UseSetting("Planification:Simulateurs:Conges:0:Debut", "2027-02-01T00:00:00+01:00");
            b.UseSetting("Planification:Simulateurs:Conges:0:Fin", "2027-02-03T00:00:00+01:00");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(new HorlogeDecalee(Maintenant - DateTimeOffset.UtcNow));
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

    private HttpClient Client(string role, string? sub = null, Guid[]? affilies = null, Guid? personne = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, role);
        if (sub is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuth.SubHeader, sub);
        }

        if (affilies is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuth.AffiliesHeader, string.Join(',', affilies));
        }

        if (personne is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuth.PersonneHeader, personne.ToString());
        }

        return client;
    }

    private HttpClient Responsable => Client(Roles.ResponsableCentre);

    private HttpClient Planificateur => Client(Roles.Planificateur);

    private static async Task<Guid> Creer(HttpClient client, string url, object body)
    {
        var reponse = await client.PostAsJsonAsync(url, body, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Ct));
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> NouveauLieu(string nom = "Centre de Namur", Guid? affilie = null) =>
        await Creer(Responsable, "/api/v1/lieux", new { type = affilie is null ? "CentreFixe" : "CabinetEntreprise", nom, codePostal = "5000", affilieId = affilie });

    private async Task<Guid> NouvelleRessource(string libelle, string? reference = null, params string[] competences) =>
        await Creer(Responsable, "/api/v1/ressources", new { type = "Conseiller", libelle, referenceId = reference, competences });

    /// <summary>Modèle d'un jour par semaine ; génère les créneaux sur la période et renvoie l'identifiant du modèle.</summary>
    private async Task<Guid> ModeleEtCreneaux(Guid ressource, Guid lieu, string jour, string typeActe, string du, string au, bool enLigne = false, bool urgence = false)
    {
        var modele = await Creer(Planificateur, "/api/v1/modeles-agenda", new
        {
            ressourceId = ressource,
            lieuId = lieu,
            valideDu = "2027-01-01",
            plages = new[] { new { jour, debut = "09:00", fin = "10:00", typeActe, dureeMinutes = 30, reserveUrgence = urgence, ouvertEnLigne = enLigne } },
        });
        var generation = await Planificateur.PostAsJsonAsync($"/api/v1/modeles-agenda/{modele}/creneaux", new { du, au }, Ct);
        generation.StatusCode.ShouldBe(HttpStatusCode.OK, await generation.Content.ReadAsStringAsync(Ct));
        return modele;
    }

    private async Task<JsonElement> Creneaux(Guid ressource, string du, string au, string? statut = null)
    {
        var url = $"/api/v1/creneaux?du={du}T00:00:00Z&au={au}T00:00:00Z&ressourceId={ressource}" + (statut is null ? string.Empty : $"&statut={statut}");
        return await Planificateur.GetFromJsonAsync<JsonElement>(url, Ct);
    }

    private List<OutboxMessage> Publies(string eventType) =>
        ((InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>()).Published.Where(m => m.EventType == eventType).ToList();

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++)
        {
            await Task.Delay(100, Ct);
        }

        condition().ShouldBeTrue();
    }

    [Fact]
    public async Task Les_sondes_repondent_sans_authentification_et_l_api_exige_un_jeton()
    {
        var anonyme = _factory.CreateClient();

        (await anonyme.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonyme.GetAsync("/api/v1/lieux", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Les_permissions_de_la_matrice_sont_appliquees()
    {
        (await Client(Roles.Travailleur).GetAsync("/api/v1/lieux", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Planificateur.PostAsJsonAsync("/api/v1/lieux", new { type = "CentreFixe", nom = "X" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.AssistantMedical).PostAsJsonAsync("/api/v1/rendez-vous", new { creneauId = Guid.NewGuid(), personneId = Guid.NewGuid(), affilieId = Guid.NewGuid() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Employeur).GetAsync("/api/v1/creneaux?du=2027-01-01T00:00:00Z&au=2027-01-08T00:00:00Z", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Planificateur).GetAsync("/api/v1/lieux", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Parcours_du_planificateur_du_modele_d_agenda_au_rendez_vous_convoque()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr Parcours", "kc-parcours", "EVALUATION_PERIODIQUE");
        await ModeleEtCreneaux(conseiller, lieu, "monday", "evaluation-periodique", "2027-01-11", "2027-01-18");

        var libres = await Creneaux(conseiller, "2027-01-11", "2027-01-19", "Libre");
        libres.GetArrayLength().ShouldBe(4);
        var creneau = libres[0].GetProperty("id").GetGuid();
        var personne = Guid.CreateVersion7();
        var affilie = Guid.CreateVersion7();

        var rdv = await Creer(Planificateur, "/api/v1/rendez-vous", new { creneauId = creneau, personneId = personne, affilieId = affilie, recommande = true });
        var second = await Planificateur.PostAsJsonAsync("/api/v1/rendez-vous", new { creneauId = creneau, personneId = Guid.CreateVersion7(), affilieId = affilie }, Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var detail = await Planificateur.GetFromJsonAsync<JsonElement>($"/api/v1/rendez-vous/{rdv}", Ct);
        detail.GetProperty("statut").GetString().ShouldBe("Planifie");
        detail.GetProperty("typeActe").GetString().ShouldBe("EVALUATION_PERIODIQUE");
        var convocations = await Planificateur.GetFromJsonAsync<JsonElement>($"/api/v1/convocations?rendezVousId={rdv}", Ct);
        convocations.GetArrayLength().ShouldBe(1);
        convocations[0].GetProperty("recommande").GetBoolean().ShouldBeTrue();
        convocations[0].GetProperty("canal").GetString().ShouldBe("Courrier");
        (await Creneaux(conseiller, "2027-01-11", "2027-01-19", "Libre")).GetArrayLength().ShouldBe(3);

        await Eventually(() => Publies("planification.rendez-vous-planifie.v1").Any(m => m.Payload.Contains(rdv.ToString())));
        await Eventually(() => Publies("planification.convocation-emise.v1").Any(m => m.Payload.Contains(rdv.ToString())));

        var annulation = await Planificateur.PostAsJsonAsync($"/api/v1/rendez-vous/{rdv}/annulation", new { motif = "DemandeEmployeur" }, Ct);
        annulation.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Creneaux(conseiller, "2027-01-11", "2027-01-19", "Libre")).GetArrayLength().ShouldBe(4);
        await Eventually(() => Publies("planification.rendez-vous-annule.v1").Any(m => m.Payload.Contains(rdv.ToString())));
    }

    [Fact]
    public async Task Huit_reservations_simultanees_du_meme_creneau_donnent_un_seul_rendez_vous()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr Concurrence", "kc-concurrence", "EVALUATION_PERIODIQUE");
        await ModeleEtCreneaux(conseiller, lieu, "tuesday", "EVALUATION_PERIODIQUE", "2027-01-12", "2027-01-12");
        var creneau = (await Creneaux(conseiller, "2027-01-12", "2027-01-13"))[0].GetProperty("id").GetGuid();
        var affilie = Guid.CreateVersion7();
        var client = Planificateur;

        var reponses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync("/api/v1/rendez-vous", new { creneauId = creneau, personneId = Guid.CreateVersion7(), affilieId = affilie }, Ct)));

        reponses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        reponses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificationDbContext>();
        (await db.RendezVous.CountAsync(r => r.CreneauId == creneau, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Six_tournees_simultanees_du_meme_conseiller_donnent_une_seule_session_et_pas_d_erreur_serveur()
    {
        var lieu = await NouveauLieu("Cabinet X");
        var conseiller = await NouvelleRessource("Dr Tournée", "kc-tournee", "EVALUATION_PERIODIQUE");
        var client = Planificateur;

        var reponses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync("/api/v1/sessions", new
        {
            lieuId = lieu,
            date = "2027-01-13",
            conseillerId = conseiller,
            capaciteJournaliere = 4,
            typeActe = "EVALUATION_PERIODIQUE",
            heureDebut = "08:00",
            dureeMinutes = 30,
        }, Ct)));

        reponses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        reponses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificationDbContext>();
        (await db.Creneaux.CountAsync(c => c.RessourceId == conseiller, Ct)).ShouldBe(4);
        (await db.Sessions.CountAsync(s => s.ConseillerId == conseiller, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task La_base_refuse_deux_creneaux_qui_se_chevauchent_pour_une_meme_ressource()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr Exclusion", "kc-exclusion");
        var salle = await Creer(Responsable, "/api/v1/ressources", new { type = "Salle", libelle = "Salle exclusion" });
        var debut = HeureBelge.VersUtc(new DateOnly(2027, 1, 14), new TimeOnly(9, 0));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificationDbContext>();

        // Créneaux contigus : [9 h, 9 h 30[ et [9 h 30, 10 h[ ne se chevauchent pas.
        db.Creneaux.Add(Creneau.Creer(conseiller, lieu, debut, debut.AddMinutes(30), "EVALUATION_PERIODIQUE", false, false, null));
        db.Creneaux.Add(Creneau.Creer(conseiller, lieu, debut.AddMinutes(30), debut.AddMinutes(60), "EVALUATION_PERIODIQUE", false, false, null));
        await db.SaveChangesAsync(Ct);

        // Un autre conseiller dans la même salle au même moment : la salle est déjà occupée.
        var autre = await NouvelleRessource("Dr Exclusion 2", "kc-exclusion-2");
        db.Creneaux.Add(Creneau.Creer(conseiller, lieu, debut.AddMinutes(60), debut.AddMinutes(90), "EVALUATION_PERIODIQUE", false, false, [salle]));
        await db.SaveChangesAsync(Ct);
        db.Creneaux.Add(Creneau.Creer(autre, lieu, debut.AddMinutes(75), debut.AddMinutes(105), "EVALUATION_PERIODIQUE", false, false, [salle]));
        var salleOccupee = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        ((PostgresException)salleOccupee.InnerException!).SqlState.ShouldBe("23P01");

        // Chevauchement du même conseiller.
        await using var scope2 = _factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PlanificationDbContext>();
        db2.Creneaux.Add(Creneau.Creer(conseiller, lieu, debut.AddMinutes(15), debut.AddMinutes(45), "EVALUATION_PERIODIQUE", false, false, null));
        var chevauchement = await Should.ThrowAsync<DbUpdateException>(() => db2.SaveChangesAsync(Ct));
        ((PostgresException)chevauchement.InnerException!).SqlState.ShouldBe("23P01");
    }

    [Fact]
    public async Task Un_creneau_retire_libere_la_ressource_pour_un_autre_creneau()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr Retrait", "kc-retrait");
        var debut = HeureBelge.VersUtc(new DateOnly(2027, 1, 15), new TimeOnly(9, 0));
        Guid creneau;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificationDbContext>();
            var nouveau = Creneau.Creer(conseiller, lieu, debut, debut.AddMinutes(30), "EVALUATION_PERIODIQUE", false, false, null);
            db.Creneaux.Add(nouveau);
            await db.SaveChangesAsync(Ct);
            creneau = nouveau.Id;
        }

        (await Planificateur.DeleteAsync($"/api/v1/creneaux/{creneau}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope2 = _factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PlanificationDbContext>();
        db2.Creneaux.Add(Creneau.Creer(conseiller, lieu, debut, debut.AddMinutes(30), "EVALUATION_PERIODIQUE", false, false, null));
        await db2.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Une_urgence_trouve_son_creneau_dans_le_delai_legal_jours_feries_compris()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr Urgences", "kc-urgences", "EXAMEN_REPRISE");
        // Mardis 5 et 12 janvier 2027 : le 5 est le dixième jour ouvrable après le 18 décembre (Noël et nouvel an exclus).
        await ModeleEtCreneaux(conseiller, lieu, "tuesday", "EXAMEN_REPRISE", "2027-01-05", "2027-01-12");
        var affilie = Guid.CreateVersion7();
        var personnes = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();

        foreach (var personne in personnes)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ObligationCreeeHandler>().HandleAsync(
                new ObligationCreee(Guid.CreateVersion7(), personne, affilie, "EXAMEN_REPRISE", new DateOnly(2026, 12, 18), null), Ct);
        }

        var rendezVous = await Planificateur.GetFromJsonAsync<JsonElement>($"/api/v1/rendez-vous?affilieId={affilie}", Ct);
        rendezVous.GetArrayLength().ShouldBe(2);
        foreach (var rdv in rendezVous.EnumerateArray())
        {
            rdv.GetProperty("urgent").GetBoolean().ShouldBeTrue();
            rdv.GetProperty("origine").GetString().ShouldBe("Urgence");
            rdv.GetProperty("debut").GetDateTimeOffset().ShouldBeInRange(
                new DateTimeOffset(2027, 1, 5, 7, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 5, 9, 30, 0, TimeSpan.Zero));
        }

        // La troisième personne n'a plus de créneau avant l'échéance : alerte au planificateur.
        await Eventually(() => Publies("planification.urgence-non-couverte.v1").Any(m => m.Payload.Contains(personnes[2].ToString())));
    }

    [Fact]
    public async Task Un_employeur_reserve_dans_son_perimetre_et_pas_ailleurs()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr En ligne", "kc-en-ligne", "EVALUATION_PERIODIQUE");
        await ModeleEtCreneaux(conseiller, lieu, "wednesday", "EVALUATION_PERIODIQUE", "2027-01-20", "2027-01-20", enLigne: true);
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ObligationCreeeHandler>().HandleAsync(
                new ObligationCreee(Guid.CreateVersion7(), personne, affilie, "EVALUATION_PERIODIQUE", new DateOnly(2027, 1, 20), null), Ct);
        }

        var employeur = Client(Roles.Employeur, affilies: [affilie, Guid.CreateVersion7()]);
        var intrus = Client(Roles.Employeur, affilies: [Guid.CreateVersion7()]);
        var url = $"/api/v1/reservations/creneaux?affilieId={affilie}&typeActe=EVALUATION_PERIODIQUE&du=2027-01-20T00:00:00Z&au=2027-01-21T00:00:00Z";

        (await intrus.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var ouverts = await employeur.GetFromJsonAsync<JsonElement>(url, Ct);
        ouverts.GetArrayLength().ShouldBe(2);
        ouverts[0].TryGetProperty("ressourceId", out _).ShouldBeFalse();
        var creneau = ouverts[0].GetProperty("id").GetGuid();

        (await intrus.PostAsJsonAsync("/api/v1/reservations", new { creneauId = creneau, personneId = personne, affilieId = affilie }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reservation = await employeur.PostAsJsonAsync("/api/v1/reservations", new { creneauId = creneau, personneId = personne, affilieId = affilie }, Ct);
        reservation.StatusCode.ShouldBe(HttpStatusCode.Created, await reservation.Content.ReadAsStringAsync(Ct));

        // Le travailleur voit son rendez-vous (claim personne_id), pas celui d'un autre.
        var travailleur = Client(Roles.Travailleur, personne: personne);
        var autreTravailleur = Client(Roles.Travailleur, personne: Guid.CreateVersion7());
        (await travailleur.GetFromJsonAsync<JsonElement>("/api/v1/reservations/rendez-vous", Ct)).GetArrayLength().ShouldBe(1);
        (await autreTravailleur.GetFromJsonAsync<JsonElement>("/api/v1/reservations/rendez-vous", Ct)).GetArrayLength().ShouldBe(0);
        // Sans claim personne_id, un travailleur n'a aucun périmètre : il doit préciser un affilié et n'obtient rien.
        (await Client(Roles.Travailleur).GetAsync("/api/v1/reservations/rendez-vous", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Les_rappels_a_sept_jours_sont_publies_une_seule_fois()
    {
        var lieu = await NouveauLieu();
        var conseiller = await NouvelleRessource("Dr Rappels", "kc-rappels", "EVALUATION_PERIODIQUE");
        await ModeleEtCreneaux(conseiller, lieu, "thursday", "EVALUATION_PERIODIQUE", "2027-01-21", "2027-01-21");
        var creneau = (await Creneaux(conseiller, "2027-01-21", "2027-01-22"))[0].GetProperty("id").GetGuid();
        var rdv = await Creer(Planificateur, "/api/v1/rendez-vous", new { creneauId = creneau, personneId = Guid.CreateVersion7(), affilieId = Guid.CreateVersion7() });

        var premiere = await Planificateur.PostAsJsonAsync("/api/v1/convocations/rappels", new { date = "2027-01-14" }, Ct);
        var seconde = await Planificateur.PostAsJsonAsync("/api/v1/convocations/rappels", new { date = "2027-01-14" }, Ct);

        (await premiere.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("premiersRappels").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        (await seconde.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("premiersRappels").GetInt32().ShouldBe(0);
        await Eventually(() => Publies("planification.rappel-rendez-vous-du.v1").Count(m => m.Payload.Contains(rdv.ToString())) == 1);
    }

    [Fact]
    public async Task L_absence_d_un_conseiller_replanifie_ses_rendez_vous_et_notifie_les_personnes()
    {
        var lieu = await NouveauLieu();
        var absent = await NouvelleRessource("Dr Absent", "kc-absent", "EVALUATION_PERIODIQUE");
        var remplacant = await NouvelleRessource("Dr Remplaçant", "kc-remplacant", "EVALUATION_PERIODIQUE");
        await ModeleEtCreneaux(absent, lieu, "monday", "EVALUATION_PERIODIQUE", "2027-01-25", "2027-01-25");
        await ModeleEtCreneaux(remplacant, lieu, "tuesday", "EVALUATION_PERIODIQUE", "2027-01-26", "2027-01-26");
        var creneau = (await Creneaux(absent, "2027-01-25", "2027-01-26"))[0].GetProperty("id").GetGuid();
        var rdv = await Creer(Planificateur, "/api/v1/rendez-vous", new { creneauId = creneau, personneId = Guid.CreateVersion7(), affilieId = Guid.CreateVersion7() });

        var reponse = await Planificateur.PostAsJsonAsync($"/api/v1/replanifications?ressourceId={absent}",
            new { debut = "2027-01-25T00:00:00+01:00", fin = "2027-01-26T00:00:00+01:00" }, Ct);

        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(Ct));
        (await reponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("rendezVousDeplaces").GetInt32().ShouldBe(1);
        var detail = await Planificateur.GetFromJsonAsync<JsonElement>($"/api/v1/rendez-vous/{rdv}", Ct);
        detail.GetProperty("ressourceId").GetGuid().ShouldBe(remplacant);
        await Eventually(() => Publies("planification.rendez-vous-replanifie.v1").Any(m => m.Payload.Contains(rdv.ToString())));
        await Eventually(() => Publies("planification.convocation-emise.v1").Any(m => m.Payload.Contains(rdv.ToString()) && m.Payload.Contains("Replanification")));
    }

    [Fact]
    public async Task L_import_des_conges_de_l_outil_rh_simule_bloque_les_creneaux_et_est_idempotent()
    {
        var lieu = await NouveauLieu();
        var enConge = await NouvelleRessource("Dr En congé", "kc-en-conge", "EVALUATION_PERIODIQUE");
        await ModeleEtCreneaux(enConge, lieu, "tuesday", "EVALUATION_PERIODIQUE", "2027-02-02", "2027-02-09");

        var import = async () => await (await Responsable.PostAsJsonAsync("/api/v1/conges/imports", new { du = "2027-01-25", au = "2027-02-28" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var premier = await import();
        var second = await import();

        premier.GetProperty("crees").GetInt32().ShouldBe(1);
        premier.GetProperty("creneauxBloques").GetInt32().ShouldBe(2);
        second.GetProperty("crees").GetInt32().ShouldBe(0);
        (await Creneaux(enConge, "2027-02-02", "2027-02-10", "Bloque")).GetArrayLength().ShouldBe(2);
        (await Creneaux(enConge, "2027-02-02", "2027-02-10", "Libre")).GetArrayLength().ShouldBe(2);
        (await Planificateur.PostAsJsonAsync("/api/v1/conges/imports", new { du = "2027-01-25", au = "2027-02-28" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_synchronisation_des_agendas_externes_n_ecrit_aucune_donnee_medicale()
    {
        var lieu = await NouveauLieu("Centre agenda");
        var conseiller = await NouvelleRessource("Dr Agenda", "kc-agenda", "EXAMEN_REPRISE");
        (await Responsable.PutAsJsonAsync($"/api/v1/ressources/{conseiller}/agenda-externe", new { fournisseur = "Google", compte = "dr.agenda@sepp.test" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ModeleEtCreneaux(conseiller, lieu, "friday", "EXAMEN_REPRISE", "2027-01-22", "2027-01-22");
        var creneau = (await Creneaux(conseiller, "2027-01-22", "2027-01-23"))[0].GetProperty("id").GetGuid();
        var personne = Guid.CreateVersion7();
        await Creer(Planificateur, "/api/v1/rendez-vous", new { creneauId = creneau, personneId = personne, affilieId = Guid.CreateVersion7() });

        var reponse = await Responsable.PostAsJsonAsync("/api/v1/synchronisations-agenda", new { du = "2027-01-18", au = "2027-01-31" }, Ct);

        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(Ct));
        var simulateur = _factory.Services.GetRequiredService<SimulateurAgendaExterne>();
        var evenement = simulateur.Evenements(FournisseurAgenda.Google, "dr.agenda@sepp.test").Values.ShouldHaveSingleItem();
        evenement.Titre.ShouldBe("Rendez-vous SEPP");
        $"{evenement.Titre} {evenement.Description} {evenement.Lieu}".ShouldNotContain("REPRISE", Case.Insensitive);
        $"{evenement.Titre} {evenement.Description} {evenement.Lieu}".ShouldNotContain(personne.ToString());
    }

    /// <summary>Authentification de test : rôles et revendications du périmètre passés dans des en-têtes.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";
        public const string SubHeader = "X-Test-Sub";
        public const string AffiliesHeader = "X-Test-Affilies";
        public const string PersonneHeader = "X-Test-Personne";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Append(new Claim("sub", Request.Headers.TryGetValue(SubHeader, out var sub) ? sub.ToString() : "test-user"))
                .ToList();
            if (Request.Headers.TryGetValue(AffiliesHeader, out var affilies))
            {
                claims.AddRange(affilies.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => new Claim("affilie_id", a)));
            }

            if (Request.Headers.TryGetValue(PersonneHeader, out var personne))
            {
                claims.Add(new Claim("personne_id", personne.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }

    /// <summary>Horloge décalée : l'heure avance normalement (les minuteries de l'outbox fonctionnent) mais « maintenant » est en décembre 2026.</summary>
    private sealed class HorlogeDecalee(TimeSpan decalage) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + decalage;
    }
}
