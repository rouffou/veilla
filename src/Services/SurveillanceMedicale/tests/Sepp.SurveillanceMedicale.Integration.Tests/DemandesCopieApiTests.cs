using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Documents;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// SAN-43, SAN-50, POR-13 de bout en bout contre un vrai PostgreSQL : demande de copie déposée par le travailleur (claim
/// personne_id), validée par le CPMT, contenu transmis au seul compte technique de Documents, publication enregistrée ;
/// 403 pour tout autre profil, trace à chaque étape, aucun contenu clinique dans les événements, les journaux ni la table
/// des demandes.
/// </summary>
public sealed class DemandesCopieApiTests(SurveillanceMedicaleFixture fixture) : IClassFixture<SurveillanceMedicaleFixture>
{
    private const string Anamnese = "Bronchite-Zorglub-tenace";
    private const string Remarque = "Réaction-Bidulon locale";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private HttpClient Cpmt => fixture.Client(Roles.Cpmt, "cpmt-copie");

    private HttpClient Documents => fixture.Client(Roles.Documents, "service-account-veilla-documents");

    private HttpClient Travailleur(Guid personne) => fixture.Client(Roles.Travailleur, "travailleur-1", personneId: personne);

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode attendu)
    {
        response.StatusCode.ShouldBe(attendu, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(Guid Personne, Guid Dossier)> DossierAsync()
    {
        var personne = Guid.CreateVersion7();
        var dossier = (await Json(await Cpmt.PostAsJsonAsync("/api/v1/dossiers", new { personneId = personne }, _ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var examen = (await Json(await Cpmt.PostAsJsonAsync("/api/v1/examens",
            new { dossierId = dossier, typeExamen = "EVALUATION_PERIODIQUE", affilieId = Guid.CreateVersion7(), date = "2026-03-02" }, _ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await Cpmt.PutAsJsonAsync($"/api/v1/examens/{examen}/observation", new { anamnese = Anamnese }, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return (personne, dossier);
    }

    [Fact]
    public async Task Parcours_complet_de_la_demande_de_copie_avec_trace_a_chaque_etape()
    {
        var (personne, dossier) = await DossierAsync();

        var deposee = await Json(await Travailleur(personne).PostAsync("/api/v1/demandes-copie", null, _ct), HttpStatusCode.Created);
        var demande = deposee.GetProperty("id").GetGuid();
        deposee.GetProperty("statut").GetString().ShouldBe("Deposee");
        deposee.GetProperty("personneId").GetGuid().ShouldBe(personne);
        (await Travailleur(personne).PostAsync("/api/v1/demandes-copie", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var file = await Json(await Cpmt.GetAsync("/api/v1/demandes-copie", _ct), HttpStatusCode.OK);
        file.EnumerateArray().ShouldContain(d => d.GetProperty("id").GetGuid() == demande);

        (await Documents.GetAsync($"/api/v1/demandes-copie/{demande}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(await Cpmt.PostAsync($"/api/v1/demandes-copie/{demande}/validation", null, _ct), HttpStatusCode.OK))
            .GetProperty("statut").GetString().ShouldBe("Validee");

        var contenu = await Json(await Documents.GetAsync($"/api/v1/demandes-copie/{demande}/contenu", _ct), HttpStatusCode.OK);
        contenu.GetRawText().ShouldContain(Anamnese);

        var documentId = Guid.CreateVersion7();
        (await fixture.DispatcherAsync(Guid.CreateVersion7(),
            new DocumentPublie(documentId, "medicale", "personne", personne, "SANTE.COPIE_DOSSIER", "demande-copie-dossier", demande), _ct)).ShouldBeGreaterThanOrEqualTo(0);

        var suivi = await Json(await Travailleur(personne).GetAsync("/api/v1/demandes-copie/miennes", _ct), HttpStatusCode.OK);
        var ligne = suivi.EnumerateArray().ShouldHaveSingleItem();
        ligne.GetProperty("statut").GetString().ShouldBe("Publiee");
        ligne.GetProperty("documentId").GetGuid().ShouldBe(documentId);
        ligne.GetRawText().ShouldNotContain(Anamnese);
        ligne.GetRawText().ShouldNotContain("cpmt-copie");

        var traces = (await fixture.TracesAsync(dossier, _ct)).Where(t => t.ObjetType == "dossier-sante.copie").ToList();
        traces.ShouldAllBe(t => t.Zone == "medicale");
        traces.Select(t => t.Action).ShouldBe(["creation", "modification", "export", "modification"]);

        (await fixture.EvenementsAsync(_ct)).ShouldContain(e => e.Type == "surveillance-medicale.copie-dossier-validee.v1" && e.Payload.Contains(demande.ToString()));
        (await fixture.EvenementsAsync(_ct)).ShouldAllBe(e => !e.Payload.Contains(Anamnese));
        fixture.Journal.Entrees.ShouldAllBe(e => !e.Contains(Anamnese));
        ((long)(await fixture.ScalaireAsync("SELECT count(*) FROM demande_copie_dossier d WHERE d::text ILIKE @v", _ct, ("v", $"%{Anamnese}%")))!).ShouldBe(0);
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.Infirmier)]
    [InlineData(Roles.AssistantMedical)]
    public async Task Un_profil_non_autorise_recoit_403_a_chaque_etape(string role)
    {
        var (personne, _) = await DossierAsync();
        var demande = (await Json(await Travailleur(personne).PostAsync("/api/v1/demandes-copie", null, _ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var intrus = fixture.Client(role, "intrus", Guid.CreateVersion7(), personneId: personne);

        (await intrus.PostAsync("/api/v1/demandes-copie", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await intrus.GetAsync("/api/v1/demandes-copie/miennes", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await intrus.GetAsync("/api/v1/demandes-copie", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await intrus.PostAsync($"/api/v1/demandes-copie/{demande}/validation", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await intrus.PostAsJsonAsync($"/api/v1/demandes-copie/{demande}/refus", new { motif = "AucunDossier" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await intrus.GetAsync($"/api/v1/demandes-copie/{demande}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await intrus.GetAsync($"/api/v1/vaccinations/{Guid.CreateVersion7()}/carnet", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Le_travailleur_ne_lit_ni_son_dossier_ni_le_contenu_de_sa_copie()
    {
        var (personne, dossier) = await DossierAsync();
        var client = Travailleur(personne);
        var demande = (await Json(await client.PostAsync("/api/v1/demandes-copie", null, _ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await Cpmt.PostAsync($"/api/v1/demandes-copie/{demande}/validation", null, _ct), HttpStatusCode.OK);

        (await client.GetAsync($"/api/v1/dossiers/{dossier}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/dossiers/{dossier}/export", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/demandes-copie/{demande}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Documents.GetAsync($"/api/v1/dossiers/{dossier}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Le_carnet_de_vaccination_n_est_transmis_qu_a_documents_sans_remarque_et_trace()
    {
        var (_, dossier) = await DossierAsync();
        var vaccination = (await Json(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossier}/vaccinations",
            new { vaccinCode = "HEPATITE_B", dose = 1, remarque = Remarque }, _ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        (await Cpmt.GetAsync($"/api/v1/vaccinations/{vaccination}/carnet", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var carnet = await Json(await Documents.GetAsync($"/api/v1/vaccinations/{vaccination}/carnet", _ct), HttpStatusCode.OK);
        (await Documents.GetAsync($"/api/v1/vaccinations/{Guid.CreateVersion7()}/carnet", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        carnet.GetProperty("doses").EnumerateArray().ShouldHaveSingleItem().GetProperty("vaccinCode").GetString().ShouldBe("HEPATITE_B");
        carnet.GetRawText().ShouldNotContain(Remarque);
        (await fixture.TracesAsync(dossier, _ct)).ShouldContain(t => t.Action == "export" && t.ObjetType == "dossier-sante.vaccinations");
    }
}
