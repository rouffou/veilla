using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Examens;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Adapters.Persistence;
using Sepp.Obligations.Adapters.Traitement;
using Sepp.Obligations.Application.Reprises;
using Sepp.Obligations.Domain.Reprises;

using Shouldly;

namespace Sepp.Obligations.Integration.Tests;

// ARC-33, POR-04 : API du processus de reprise contre un vrai PostgreSQL (migration, unicité, minuteries SKIP LOCKED).
public sealed partial class ObligationsApiTests
{
    private static readonly DateOnly DateReprise = new(2026, 6, 15);
    private static readonly DateOnly DebutAbsence = new(2026, 5, 1);

    private HttpClient Gestionnaire => Client(Roles.GestionnaireDossiers, "gest-1");

    private static object Annonce(Guid personne, Guid affilie, DateOnly? reprise = null, DateOnly? debut = null) =>
        new { personneId = personne, affilieId = affilie, dateReprise = reprise ?? DateReprise, debutAbsence = debut ?? DebutAbsence };

    private async Task<ResultatEnregistrement> AnnoncerAsync(HttpClient client, Guid personne, HttpStatusCode attendu = HttpStatusCode.Created)
    {
        var reponse = await client.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, _affilie), Json, Ct);
        reponse.StatusCode.ShouldBe(attendu, await reponse.Content.ReadAsStringAsync(Ct));
        return (await reponse.Content.ReadFromJsonAsync<ResultatEnregistrement>(Json, Ct))!;
    }

    /// <summary>Établit l'occupation du travailleur chez l'affilié (préalable de toute annonce de reprise, #298).</summary>
    private Task<int> OccuperAsync(Guid personne, DateOnly? debut = null) =>
        Livrer(Guid.CreateVersion7(), new Sepp.Contracts.Personnes.OccupationDebutee(Guid.CreateVersion7(), personne, _affilie, debut ?? new DateOnly(2025, 1, 1)));

    [Fact]
    public async Task Une_annonce_sans_occupation_active_est_refusee_en_422_puis_acceptee_quand_l_occupation_est_recue()
    {
        var personne = Guid.CreateVersion7();

        var refus = await Gestionnaire.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, _affilie), Json, Ct);

        refus.StatusCode.ShouldBe((HttpStatusCode)422);
        (await refus.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("reprise.occupation-inactive");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObligationsDbContext>();
            (await db.Processus.CountAsync(p => p.PersonneId == personne, Ct)).ShouldBe(0);
        }

        // À valider : événements en désordre ; l'occupation arrive après la première tentative.
        await OccuperAsync(personne);
        (await AnnoncerAsync(Gestionnaire, personne)).Cree.ShouldBeTrue();
    }

    [Fact]
    public async Task Une_annonce_par_evenement_sans_occupation_active_est_ignoree()
    {
        var personne = Guid.CreateVersion7();

        await Livrer(Guid.CreateVersion7(), new Sepp.Contracts.BffEmployeur.RepriseAnnoncee(personne, _affilie, DateReprise, DebutAbsence));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ObligationsDbContext>();
        (await db.Processus.CountAsync(p => p.PersonneId == personne, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Une_reprise_annoncee_est_idempotente_consultable_modifiable_et_annulable()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);

        var premiere = await AnnoncerAsync(Gestionnaire, personne);
        var seconde = await AnnoncerAsync(Gestionnaire, personne, HttpStatusCode.OK);
        seconde.RepriseId.ShouldBe(premiere.RepriseId);

        var dto = (await Cpmt.GetFromJsonAsync<RepriseDto>($"/api/v1/reprises/{premiere.RepriseId}", Json, Ct))!;
        dto.Statut.ShouldBe("ObligationOuverte");
        dto.Origine.ShouldBe("Interne");
        (await Obligations(personne)).ShouldContain(o => o.Type == "EXAMEN_REPRISE");

        var liste = (await Cpmt.GetFromJsonAsync<List<RepriseDto>>($"/api/v1/reprises?statut=ObligationOuverte&affilieId={_affilie}", Json, Ct))!;
        liste.ShouldContain(r => r.Id == premiere.RepriseId);

        var modification = await Gestionnaire.PutAsJsonAsync($"/api/v1/reprises/{premiere.RepriseId}", new { debutAbsence = new DateOnly(2026, 5, 4) }, Json, Ct);
        modification.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Employeur.PostAsJsonAsync($"/api/v1/reprises/{premiere.RepriseId}/annulation", new { motif = "Autre" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Gestionnaire.PostAsJsonAsync($"/api/v1/reprises/{premiere.RepriseId}/annulation", new { motif = "ErreurDeSaisie" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Gestionnaire.PostAsJsonAsync($"/api/v1/reprises/{premiere.RepriseId}/annulation", new { motif = "ErreurDeSaisie" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Cpmt.GetFromJsonAsync<RepriseDto>($"/api/v1/reprises/{premiere.RepriseId}", Json, Ct))!.Statut.ShouldBe("Annulee");
        (await Obligations(personne)).Single(o => o.Type == "EXAMEN_REPRISE").Statut.ShouldBe("Annule");
    }

    [Fact]
    public async Task L_annulation_apres_la_cloture_de_l_examen_est_refusee_en_409()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);
        var reprise = await AnnoncerAsync(Gestionnaire, personne);
        (await Livrer(Guid.CreateVersion7(), new ExamenCloture(Guid.CreateVersion7(), personne, _affilie, TypesExamen.ExamenReprise, DateReprise.AddDays(1)))).ShouldBe(1);

        var reponse = await Gestionnaire.PostAsJsonAsync($"/api/v1/reprises/{reprise.RepriseId}/annulation", new { motif = "Autre" }, Json, Ct);

        reponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Cpmt.GetFromJsonAsync<RepriseDto>($"/api/v1/reprises/{reprise.RepriseId}", Json, Ct))!.Statut.ShouldBe("ExamenRealise");
    }

    [Fact]
    public async Task L_employeur_annonce_dans_son_perimetre_seulement_et_les_roles_sans_droit_sont_refuses()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);

        var reprise = await AnnoncerAsync(Employeur, personne);
        (await Employeur.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, Guid.CreateVersion7()), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Cpmt.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, _affilie), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/api/v1/reprises", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var vue = (await Employeur.GetFromJsonAsync<RepriseDto>($"/api/v1/reprises/{reprise.RepriseId}", Json, Ct))!;
        vue.Origine.ShouldBe("PortailEmployeur");
        vue.ExamenId.ShouldBeNull();
        (await Client(Roles.Employeur, "autre-employeur", Guid.CreateVersion7()).GetAsync($"/api/v1/reprises/{reprise.RepriseId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deux_annonces_simultanees_ne_creent_qu_un_seul_processus()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);

        var reponses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Gestionnaire.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, _affilie), Json, Ct)));

        // 201 pour le gagnant ; 200 pour l'annonce idempotente ; 409 si une écriture concurrente demande de réessayer.
        reponses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        reponses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ObligationsDbContext>();
        (await db.Processus.CountAsync(p => p.PersonneId == personne, Ct)).ShouldBe(1);
        (await db.Obligations.CountAsync(o => o.PersonneId == personne, Ct)).ShouldBe(1);

        // Une annonce répétée après coup renvoie le même processus.
        var identifiant = (await db.Processus.SingleAsync(p => p.PersonneId == personne, Ct)).Id;
        (await AnnoncerAsync(Gestionnaire, personne, HttpStatusCode.OK)).RepriseId.ShouldBe(identifiant);
    }

    [Fact]
    public async Task Deux_passages_simultanes_des_minuteries_ne_traitent_chaque_processus_qu_une_fois()
    {
        // Reprise du 18 mai : la date limite (1er juin) est dépassée à la date simulée (15 juin) : deux minuteries sont échues.
        var personnes = Enumerable.Range(0, 6).Select(_ => Guid.CreateVersion7()).ToList();
        foreach (var personne in personnes)
        {
            await OccuperAsync(personne);
            var reponse = await Gestionnaire.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, _affilie, new DateOnly(2026, 5, 18), new DateOnly(2026, 4, 1)), Json, Ct);
            reponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var service = _factory.Services.GetServices<IHostedService>().OfType<MinuteriesRepriseService>().Single();
        var passages = await Task.WhenAll(service.ExecuterUneFoisAsync(Ct), service.ExecuterUneFoisAsync(Ct));

        passages.Sum(p => p.ProcessusTraites).ShouldBe(personnes.Count);
        passages.Sum(p => p.MinuteriesDeclenchees).ShouldBe(personnes.Count * 2);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ObligationsDbContext>();
        var processus = await db.Processus.Where(p => personnes.Contains(p.PersonneId)).ToListAsync(Ct);
        processus.ShouldAllBe(p => p.HorsDelai && p.MinuteriesDeclenchees.Count == 2 && p.ProchaineEcheance == null);

        // Un nouveau passage n'a plus rien à faire.
        (await service.ExecuterUneFoisAsync(Ct)).ProcessusTraites.ShouldBe(0);
    }

    [Fact]
    public async Task Les_alertes_de_l_affilie_incluent_les_reprises_hors_delai()
    {
        var personne = Guid.CreateVersion7();
        await OccuperAsync(personne);
        await Gestionnaire.PostAsJsonAsync("/api/v1/reprises", Annonce(personne, _affilie, new DateOnly(2026, 5, 18), new DateOnly(2026, 4, 1)), Json, Ct);
        await _factory.Services.GetServices<IHostedService>().OfType<MinuteriesRepriseService>().Single().ExecuterUneFoisAsync(Ct);

        var alertes = (await Employeur.GetFromJsonAsync<List<Sepp.Obligations.Application.Consultation.AlerteDto>>($"/api/v1/affilies/{_affilie}/alertes", Json, Ct))!;

        alertes.ShouldContain(a => a.Type == "RepriseHorsDelai" && a.PersonneId == personne);
    }
}
