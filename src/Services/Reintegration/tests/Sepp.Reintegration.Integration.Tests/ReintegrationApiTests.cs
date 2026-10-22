using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Reintegration;
using Sepp.Reintegration.Adapters.Traitement;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Application.TableauDeBord;
using Sepp.Reintegration.Application.Trajets;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Trajets;

using Shouldly;

namespace Sepp.Reintegration.Integration.Tests;

/// <summary>
/// Secret médical de bout en bout contre un vrai PostgreSQL (modèle de Surveillance médicale) : matrice des droits, relation de
/// soin, motif et bris de glace, journalisation de chaque accès, chiffrement du contenu médical au repos (SQL brut), événements
/// et journaux sans contenu médical, consommation idempotente, suivi employeur limité à son affilié, minuteries en base
/// (<c>FOR UPDATE SKIP LOCKED</c>) et envoi eHealth par le simulateur.
/// </summary>
public sealed class ReintegrationApiTests(ReintegrationFixture fixture) : IClassFixture<ReintegrationFixture>
{
    private const string Commentaire = "Lombalgie-Zorglub chronique";
    private const string Note = "Hernie-Bidulon L4-L5 opérée";
    private const string Contenu = "Traitement-Machinchose en cours";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly Guid _personne = Guid.CreateVersion7();

    private HttpClient Cpmt => fixture.Client(Roles.Cpmt, "cpmt-1");

    private static async Task<Guid> Id(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task Ok(HttpResponseMessage response, HttpStatusCode attendu = HttpStatusCode.NoContent) =>
        response.StatusCode.ShouldBe(attendu, await response.Content.ReadAsStringAsync());

    private async Task<Guid> SaisirAsync() =>
        await Id(await Cpmt.PostAsJsonAsync("/api/v1/incapacites", new { personneId = _personne, affilieId = _affilie, dateDebut = "2026-03-02" }, _ct));

    private async Task<(Guid Incapacite, Guid Trajet)> TrajetAsync()
    {
        var incapacite = await SaisirAsync();
        var trajet = await Id(await Cpmt.PostAsJsonAsync("/api/v1/trajets", new { incapaciteId = incapacite, initiateur = "Travailleur", dateDemande = "2026-03-20" }, _ct));
        return (incapacite, trajet);
    }

    [Fact]
    public async Task Les_sondes_repondent_et_une_requete_anonyme_est_refusee()
    {
        (await fixture.Factory.CreateClient().GetAsync("/health/ready", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.Factory.CreateClient().GetAsync($"/api/v1/incapacites/{Guid.CreateVersion7()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.AssistantMedical)]
    public async Task Un_profil_non_medical_recoit_403_sur_tout_le_dossier_et_ne_laisse_aucune_trace(string role)
    {
        var (incapacite, trajet) = await TrajetAsync();
        var avant = (await fixture.TracesAsync(incapacite, _ct)).Count;
        var client = fixture.Client(role, "intrus", _affilie, motif: "curiosité");

        (await client.GetAsync($"/api/v1/incapacites/{incapacite}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/incapacites?personneId={_personne}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/trajets/{trajet}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/incapacites/{incapacite}/echanges-ehealth", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/tableau-de-bord/delais", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/v1/incapacites/{incapacite}/estimations", new { date = "2026-04-28", resultat = "TrajetIndique", methode = "M" }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/incapacites", new { personneId = _personne, affilieId = _affilie, dateDebut = "2026-03-02" }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await fixture.TracesAsync(incapacite, _ct)).Count.ShouldBe(avant);
    }

    [Fact]
    public async Task Chaque_acces_est_trace_et_hors_relation_de_soin_le_motif_est_exige()
    {
        var incapacite = await SaisirAsync();
        await Ok(await Cpmt.GetAsync($"/api/v1/incapacites/{incapacite}", _ct), HttpStatusCode.OK);

        var autre = fixture.Client(Roles.Cpmt, "cpmt-2");
        var refus = await autre.GetAsync($"/api/v1/incapacites/{incapacite}", _ct);
        refus.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refus.Content.ReadAsStringAsync(_ct)).ShouldContain("reintegration.motif-obligatoire");
        (await fixture.Client(Roles.Cpmt, "cpmt-2", brisDeGlace: true).GetAsync($"/api/v1/incapacites/{incapacite}", _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await Ok(await fixture.Client(Roles.Cpmt, "cpmt-2", motif: "Avis demandé par le Dr Dupré").GetAsync($"/api/v1/incapacites/{incapacite}", _ct), HttpStatusCode.OK);
        await Ok(await fixture.Client(Roles.Infirmier, "inf-9", motif: "Remplacement urgent", brisDeGlace: true).GetAsync($"/api/v1/incapacites/{incapacite}", _ct), HttpStatusCode.OK);

        var traces = await fixture.TracesAsync(incapacite, _ct);
        traces.ShouldAllBe(t => t.Zone == "medicale");
        traces.Select(t => (t.Action, t.ObjetType, t.Motif, t.BrisDeGlace)).ShouldBe(
        [
            ("creation", "reintegration.incapacite", null, false),
            ("lecture", "reintegration.incapacite", null, false),
            ("lecture", "reintegration.incapacite", "Avis demandé par le Dr Dupré", false),
            ("lecture", "reintegration.incapacite", "Remplacement urgent", true),
        ]);
    }

    [Fact]
    public async Task Le_contenu_medical_n_apparait_jamais_en_clair_ni_en_base_ni_dans_les_evenements_ni_dans_les_journaux()
    {
        var (incapacite, trajet) = await TrajetAsync();
        (await Cpmt.PostAsJsonAsync($"/api/v1/incapacites/{incapacite}/estimations",
            new { date = "2026-04-28", resultat = "TrajetIndique", methode = "METHODE_STANDARD", commentaire = Commentaire }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/invitation", new { date = "2026-03-25", recommandee = true }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/evaluation", new { date = "2026-03-27", note = Note }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/formulaire", new { date = "2026-03-27", conclusion = "TravailAdapteOuAutrePossible" }, _ct));
        (await Cpmt.PostAsJsonAsync("/api/v1/echanges-ehealth", new
        {
            incapaciteId = incapacite,
            trajetId = trajet,
            destinataire = "MedecinTraitant",
            objet = "DemandeInformation",
            contenu = Contenu,
            referenceDestinataire = "INAMI-SIM-1",
            accordTravailleur = true,
        }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await fixture.Factory.Services.GetRequiredService<TraitementReintegrationService>().ExecuterUneFoisAsync(_ct);

        // Lecture déchiffrée par le personnel autorisé.
        var dossier = await Cpmt.GetFromJsonAsync<IncapaciteDto>($"/api/v1/incapacites/{incapacite}", Json, _ct);
        dossier!.Estimations.ShouldHaveSingleItem().Commentaire.ShouldBe(Commentaire);
        (await Cpmt.GetFromJsonAsync<TrajetDto>($"/api/v1/trajets/{trajet}", Json, _ct))!.NoteEvaluation.ShouldBe(Note);

        // SQL brut : colonnes chiffrées avec la clé de la zone médicale, aucune valeur médicale en clair, nulle part.
        ((string)(await fixture.ScalaireAsync("SELECT note_evaluation_chiffre FROM trajet WHERE id = @id", _ct, ("id", trajet)))!)
            .ShouldStartWith($"v1:{ReintegrationFixture.CleTest}:");
        foreach (var medical in new[] { "Zorglub", "Bidulon", "Machinchose" })
        {
            (await fixture.OccurrencesEnBaseAsync(medical, _ct)).ShouldBe(0, $"« {medical} » trouvé en clair en base");
            fixture.Journal.Entrees.Where(e => e.Contains(medical, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty($"« {medical} » trouvé dans les journaux");
        }

        // Événements : identifiants, dates et codes seulement (ARC-06).
        var evenements = await fixture.EvenementsAsync(_ct);
        evenements.ShouldContain(e => e.Type == "reintegration.trajet-demarre.v1" && e.Payload.Contains(trajet.ToString()) && e.Payload.Contains("TRAVAILLEUR"));
        foreach (var (_, payload) in evenements)
        {
            payload.ShouldNotContain("Zorglub");
            payload.ShouldNotContain("Bidulon");
            payload.ShouldNotContain("Machinchose");
        }

        // L'échange a été remis au canal eHealth simulé, sa réponse fictive reçue et rattachée.
        var echanges = await Cpmt.GetFromJsonAsync<JsonElement>($"/api/v1/incapacites/{incapacite}/echanges-ehealth", _ct);
        echanges.EnumerateArray().Select(e => e.GetProperty("statut").GetString()).ShouldBe(["Envoye", "Recu"], ignoreOrder: true);
    }

    [Fact]
    public async Task La_notification_eHealth_est_consommee_une_fois_et_les_absences_informent_le_medecin_conseil()
    {
        var incapaciteId = Guid.CreateVersion7();
        var notification = new IncapaciteNotifiee(incapaciteId, _personne, _affilie, new DateOnly(2026, 3, 2), CodesReintegration.SourcesIncapacite.Employeur, new DateOnly(2026, 6, 30));
        var message = Guid.CreateVersion7();
        (await fixture.DispatcherAsync(message, notification, _ct)).ShouldBe(1);
        (await fixture.DispatcherAsync(message, notification, _ct)).ShouldBe(0);

        var cpmt = fixture.Client(Roles.Cpmt, "cpmt-3", motif: "Prise en charge");
        await Ok(await cpmt.PostAsync($"/api/v1/incapacites/{incapaciteId}/prise-en-charge", null, _ct));
        var trajet = await Id(await fixture.Client(Roles.Cpmt, "cpmt-3").PostAsJsonAsync("/api/v1/trajets",
            new { incapaciteId, initiateur = "MedecinConseil", dateDemande = "2026-03-23" }, _ct));

        var obligation = Guid.CreateVersion7();
        await fixture.DispatcherAsync(Guid.CreateVersion7(),
            new ObligationCreee(obligation, _personne, _affilie, TypesExamen.EvaluationReintegration, new DateOnly(2026, 3, 23), new DateOnly(2026, 5, 11)), _ct);
        foreach (var jour in new[] { 24, 26 })
        {
            await fixture.DispatcherAsync(Guid.CreateVersion7(), new AbsenceRendezVousConstatee(
                Guid.CreateVersion7(), _personne, _affilie, new DateTimeOffset(2026, 3, jour, 8, 0, 0, TimeSpan.Zero), [obligation]), _ct);
        }

        var detail = await fixture.Client(Roles.Cpmt, "cpmt-3").GetFromJsonAsync<TrajetDto>($"/api/v1/trajets/{trajet}", Json, _ct);
        detail!.Absences.Count.ShouldBe(2);
        detail.MedecinConseilInformeLe.ShouldNotBeNull();
        (await fixture.ScalaireAsync("SELECT count(*) FROM echange_ehealth WHERE trajet_id = @t AND destinataire = 'MedecinConseil' AND objet = 'NotificationAbsences'", _ct,
            ("t", trajet)))!.ShouldBe(1L);
    }

    [Fact]
    public async Task L_employeur_suit_les_trajets_de_son_seul_affilie_sans_donnee_medicale_et_depose_le_plan()
    {
        var (_, trajet) = await TrajetAsync();
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/invitation", new { date = "2026-03-25", recommandee = true }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/evaluation", new { date = "2026-03-27", note = Note }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/formulaire", new { date = "2026-03-27", conclusion = "TravailAdapteOuAutrePossible" }, _ct));

        var employeur = fixture.Client(Roles.Employeur, "employeur-1", _affilie);
        var suivi = await employeur.GetStringAsync($"/api/v1/affilies/{_affilie}/trajets", _ct);
        suivi.ShouldContain(trajet.ToString());
        suivi.ShouldContain("TravailAdapteOuAutrePossible");
        suivi.ShouldNotContain("Bidulon");
        (await fixture.Client(Roles.Employeur, "employeur-2", Guid.CreateVersion7()).GetAsync($"/api/v1/affilies/{_affilie}/trajets", _ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await employeur.GetAsync($"/api/v1/trajets/{trajet}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await Ok(await employeur.PostAsJsonAsync($"/api/v1/trajets/{trajet}/reponse-employeur", new { reponse = "PlanReintegration", date = "2026-03-30" }, _ct));
        var travailleur = fixture.Client(Roles.Travailleur, "travailleur-1", personneId: _personne);
        await Ok(await travailleur.PostAsJsonAsync($"/api/v1/trajets/{trajet}/reponse-travailleur", new { reponse = "Refus", date = "2026-03-31" }, _ct));
        (await fixture.Client(Roles.Travailleur, "travailleur-2", personneId: Guid.CreateVersion7())
            .PostAsJsonAsync($"/api/v1/trajets/{trajet}/reponse-travailleur", new { reponse = "Acceptation", date = "2026-03-31" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/trajets/{trajet}/fin", new { date = "2026-03-31", issue = "PlanRefuse" }, _ct));

        var evenements = await fixture.EvenementsAsync(_ct);
        evenements.ShouldContain(e => e.Type == "reintegration.trajet-termine.v1" && e.Payload.Contains(trajet.ToString()) && e.Payload.Contains("PLAN_REFUSE"));
    }

    [Fact]
    public async Task Le_tableau_de_bord_signale_les_depassements_et_deux_passages_simultanes_ne_traitent_pas_deux_fois()
    {
        var incapacite = await SaisirAsync();

        var tableau = await Cpmt.GetFromJsonAsync<TableauDeBordDto>($"/api/v1/tableau-de-bord/delais?affilieId={_affilie}", Json, _ct);
        var information = tableau!.Lignes.Single(l => l.IncapaciteId == incapacite && l.Type == TypeEcheance.InformationTravailleur);
        information.Statut.ShouldBe(StatutEcheance.Depassee);
        information.Date.ShouldBe(new DateOnly(2026, 3, 30));
        (await fixture.TracesAsync(incapacite, _ct)).ShouldContain(t => t.ObjetType == "reintegration.tableau-de-bord");

        var service = fixture.Factory.Services.GetRequiredService<TraitementReintegrationService>();
        var passages = await Task.WhenAll(service.ExecuterUneFoisAsync(_ct), service.ExecuterUneFoisAsync(_ct));
        var declenchees = (string[])(await fixture.ScalaireAsync("SELECT minuteries_declenchees FROM incapacite WHERE id = @id", _ct, ("id", incapacite)))!;
        declenchees.Count(m => m.StartsWith("InformationTravailleur.Depassee", StringComparison.Ordinal)).ShouldBe(1);
        declenchees.Count(m => m.StartsWith("InformationTravailleur.Proche", StringComparison.Ordinal)).ShouldBe(1);
        passages.Sum(p => p.DossiersTraites).ShouldBeGreaterThanOrEqualTo(1);

        var apres = await Cpmt.GetFromJsonAsync<TableauDeBordDto>($"/api/v1/tableau-de-bord/delais?affilieId={_affilie}", Json, _ct);
        apres!.Lignes.Single(l => l.IncapaciteId == incapacite && l.Type == TypeEcheance.InformationTravailleur).AlerteEmise.ShouldBeTrue();
    }
}
