using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// NF-01 en mode strict (comme en Azure) : la signature d'une décision (SAN-32) et les routes <c>dossier-sante:*</c> exigent
/// l'authentification forte ; sans elle, 401 avec le défi de montée en niveau (RFC 9470). En mode souple (défaut local et des
/// autres tests), l'accès reste accordé.
/// </summary>
public sealed partial class AuthentificationForteApiTests(SurveillanceMedicaleFixture fixture) : IClassFixture<SurveillanceMedicaleFixture>
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
        // #401 : la clôture (decision:signer) et la gestion du dossier (dossier-sante:gerer) exigent aussi l'authentification forte.
        DefiDeMonteeEnNiveau(await faible.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct));
        DefiDeMonteeEnNiveau(await faible.PutAsJsonAsync($"/api/v1/dossiers/{dossierId}/gestionnaire", new { cpmtId = "cpmt-mfa" }, _ct));
        DefiDeMonteeEnNiveau(await faible.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/transferts", new { contrepartie = "0123456789", motif = "ChangementEmployeur" }, _ct));
        (await fort.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var decisionId = await Id(await fort.PostAsJsonAsync($"/api/v1/examens/{examenId}/decision",
            new { categorie = "Apte", valideJusquAu = "2027-03-01" }, _ct));

        DefiDeMonteeEnNiveau(await faible.SendAsync(Signature(decisionId), _ct));
        DefiDeMonteeEnNiveau(await faible.GetAsync($"/api/v1/dossiers/{dossierId}", _ct));

        var signee = await fort.SendAsync(Signature(decisionId), _ct);
        signee.StatusCode.ShouldBe(HttpStatusCode.OK, await signee.Content.ReadAsStringAsync(_ct));
    }

    [Theory]
    [InlineData(Roles.Infirmier, Permissions.DecisionEcrire, 5)]
    [InlineData(Roles.Cpmt, Permissions.DecisionEcrire, 5)]
    [InlineData(Roles.Cpmt, Permissions.DecisionSigner, 7)]
    public async Task Les_ecritures_decision_exigent_l_authentification_forte(string role, string permission, int routesAttendues)
    {
        // #494 : toute route decision:ecrire ou decision:signer exige l'authentification forte (préfixe decision:), sans
        // déclaration route par route. Les routes sont énumérées depuis l'application : une route ajoutée plus tard est
        // couverte. L'autorisation précède le chargement et la lecture du corps : des identifiants inconnus suffisent.
        var faible = fixture.ClientStrict(role, $"{role}-pwd", amr: "pwd");
        var routes = RoutesProtegeesPar(permission);
        routes.Count.ShouldBe(routesAttendues, string.Join(", ", routes.Select(r => $"{r.Methode} {r.Gabarit}")));

        foreach (var (methode, gabarit) in routes)
        {
            var chemin = ParametreDeRoute().Replace(gabarit, _ => Guid.CreateVersion7().ToString());
            using var requete = new HttpRequestMessage(new HttpMethod(methode), chemin) { Content = JsonContent.Create(new { }) };
            requete.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            var reponse = await faible.SendAsync(requete, _ct);
            reponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{methode} {gabarit}");
            DefiDeMonteeEnNiveau(reponse);
        }
    }

    /// <summary>Routes de l'application protégées par <paramref name="permission"/> (méthode, gabarit).</summary>
    private List<(string Methode, string Gabarit)> RoutesProtegeesPar(string permission) =>
        [.. fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == PermissionPolicy.Name(permission)))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => (m, "/" + e.RoutePattern.RawText!.TrimStart('/'))))];

    [GeneratedRegex("\\{[^}]+\\}")]
    private static partial Regex ParametreDeRoute();

    [Fact]
    public async Task La_redaction_d_une_decision_par_l_infirmier_est_acceptee_apres_montee_en_niveau()
    {
        var cpmt = fixture.ClientStrict(Roles.Cpmt, "cpmt-gestionnaire", amr: "pwd,otp");
        var dossierId = await Id(await cpmt.PostAsJsonAsync("/api/v1/dossiers", new { personneId = Guid.CreateVersion7() }, _ct));
        var examenId = await Id(await cpmt.PostAsJsonAsync("/api/v1/examens",
            new { dossierId, typeExamen = "EVALUATION_PERIODIQUE", affilieId = Guid.CreateVersion7(), date = "2026-03-02" }, _ct));

        var infirmier = fixture.ClientStrict(Roles.Infirmier, "infirmier-mfa", amr: "pwd,otp", motif: "Préparation de la décision du Dr gestionnaire");
        await Id(await infirmier.PostAsJsonAsync($"/api/v1/examens/{examenId}/decision",
            new { categorie = "Apte", justification = "Justification médicale" }, _ct));
    }

    [Fact]
    public async Task La_lecture_du_resume_d_une_decision_n_exige_pas_l_authentification_forte()
    {
        // decision:lire est exempté : le résumé (catégorie, mesures, validité) est aussi lu par l'employeur et le travailleur.
        var response = await fixture.ClientStrict(Roles.GestionnaireDossiers, "gestionnaire-pwd", amr: "pwd").GetAsync($"/api/v1/decisions/{Guid.CreateVersion7()}", _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.WwwAuthenticate.ShouldBeEmpty();
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

    /// <summary>Signature de la version 1 (décision tout juste rédigée), en-tête If-Match (#401).</summary>
    private static HttpRequestMessage Signature(Guid decisionId)
    {
        var requete = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/decisions/{decisionId}/signature");
        requete.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        return requete;
    }
}
