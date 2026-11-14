using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// NF-01 en mode strict (comme en Azure) : la signature d'une décision (SAN-32) et les routes <c>dossier-sante:*</c> exigent
/// l'authentification forte ; sans elle, 401 avec le défi de montée en niveau (RFC 9470). En mode souple (défaut local et des
/// autres tests), l'accès reste accordé.
/// </summary>
public sealed class AuthentificationForteApiTests(SurveillanceMedicaleFixture fixture) : IClassFixture<SurveillanceMedicaleFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static async Task<Guid> Id(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static void DefiDeMonteeEnNiveau(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var defi = response.Headers.WwwAuthenticate.ToString();
        defi.ShouldContain("error=\"insufficient_user_authentication\"");
        defi.ShouldContain("acr_values=\"mfa\"");
    }

    [Fact]
    public async Task La_signature_exige_l_authentification_forte_et_l_accepte_apres_montee_en_niveau()
    {
        var fort = fixture.ClientStrict(Roles.Cpmt, "cpmt-mfa", amr: "pwd,otp");
        var faible = fixture.ClientStrict(Roles.Cpmt, "cpmt-mfa", amr: "pwd");
        var affilie = Guid.CreateVersion7();

        var dossierId = await Id(await fort.PostAsJsonAsync("/api/v1/dossiers", new { personneId = Guid.CreateVersion7() }, _ct));
        var examenId = await Id(await fort.PostAsJsonAsync("/api/v1/examens",
            new { dossierId, typeExamen = "EVALUATION_PERIODIQUE", affilieId = affilie, date = "2026-03-02" }, _ct));
        (await fort.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var decisionId = await Id(await fort.PostAsJsonAsync($"/api/v1/examens/{examenId}/decision",
            new { categorie = "Apte", valideJusquAu = "2027-03-01" }, _ct));

        DefiDeMonteeEnNiveau(await faible.PostAsync($"/api/v1/decisions/{decisionId}/signature", null, _ct));
        DefiDeMonteeEnNiveau(await faible.GetAsync($"/api/v1/dossiers/{dossierId}", _ct));

        var signee = await fort.PostAsync($"/api/v1/decisions/{decisionId}/signature", null, _ct);
        signee.StatusCode.ShouldBe(HttpStatusCode.OK, await signee.Content.ReadAsStringAsync(_ct));
    }

    [Fact]
    public async Task Un_profil_sans_permission_recoit_403_et_non_un_defi()
    {
        var response = await fixture.ClientStrict(Roles.GestionnaireDossiers, "gestionnaire-1").GetAsync($"/api/v1/dossiers/{Guid.CreateVersion7()}", _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_depot_d_une_demande_de_copie_par_le_travailleur_n_exige_pas_de_MFA_interne()
    {
        // dossier-sante:copie-demander n'ouvre aucun contenu médical ; le travailleur s'authentifie par CSAM en Azure.
        var response = await fixture.ClientStrict(Roles.Travailleur, "travailleur-1").GetAsync("/api/v1/demandes-copie/miennes", _ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task En_mode_souple_l_acces_reste_accorde_et_l_insuffisance_est_journalisee()
    {
        var response = await fixture.Client(Roles.Cpmt, "cpmt-souple").GetAsync($"/api/v1/dossiers/{Guid.CreateVersion7()}", _ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        fixture.Journal.Entrees.ShouldContain(e => e.Contains("NF-01", StringComparison.Ordinal) && e.Contains("accès accordé en mode Log", StringComparison.Ordinal));
    }
}
