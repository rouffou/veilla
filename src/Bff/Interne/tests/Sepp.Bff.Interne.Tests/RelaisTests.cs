using System.Security.Claims;

using Microsoft.Extensions.Configuration;

using Sepp.Bff.Interne.Relais;
using Sepp.Bff.Interne.Securite;

using Shouldly;

namespace Sepp.Bff.Interne.Tests;

public sealed class RelaisTests
{
    private static Dictionary<string, string?> ToutesLesAdresses() =>
        ServicesRelayes.Tous.ToDictionary(s => $"ServicesAval:{s.Cle}", s => (string?)$"http://{s.Segment}:8080");

    [Fact]
    public void Les_adresses_viennent_de_ServicesAval_puis_des_cles_terraform()
    {
        var valeurs = ToutesLesAdresses();
        valeurs.Remove("ServicesAval:PostesRisques");
        valeurs.Remove("ServicesAval:SurveillanceMedicale");
        valeurs["Sepp:Services:Postes_Risques"] = "https://ca-postes-risques.interne.example";
        valeurs["Sepp:Services:Surveillance_Medicale"] = "https://ca-surveillance-medicale.interne.example";

        var adresses = ServicesRelayes.Resoudre(new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build());

        adresses.Count.ShouldBe(12);
        adresses["referentiels"].ShouldBe(new Uri("http://referentiels:8080/"));
        adresses["postes-risques"].ShouldBe(new Uri("https://ca-postes-risques.interne.example/"));
        adresses["surveillance-medicale"].ShouldBe(new Uri("https://ca-surveillance-medicale.interne.example/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("referentiels:8080")]
    [InlineData("ftp://referentiels")]
    [InlineData("http://referentiels:8080/api")]
    [InlineData("http://user:mdp@referentiels:8080")]
    public void Une_adresse_absente_ou_invalide_fait_echouer_le_demarrage(string? adresse)
    {
        var valeurs = ToutesLesAdresses();
        valeurs["ServicesAval:Referentiels"] = adresse;

        Should.Throw<InvalidOperationException>(() => ServicesRelayes.Resoudre(new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build()))
            .Message.ShouldContain("ServicesAval:Referentiels");
    }

    [Theory]
    [InlineData("/referentiels/api/v1/parametres-legaux", null, false)]
    [InlineData("/referentiels/api/v1/parametres-legaux", "/referentiels/api/v1/parametres-legaux?langue=nl", false)]
    [InlineData("/referentiels/api/v1/../../health/ready", null, true)]
    [InlineData("/referentiels/api/v1/./x", null, true)]
    [InlineData("/referentiels/api/v1/x", "/referentiels/api/v1/%2e%2e/%2E%2E/health", true)]
    [InlineData("/referentiels/api/v1/a%2Fb", null, true)]
    [InlineData("/referentiels/api/v1/a\\..\\b", null, true)]
    [InlineData("/referentiels/api/v1/x", "/referentiels/api/v1/a%5c..", true)]
    public void Un_chemin_qui_tente_de_sortir_de_api_v1_est_detecte(string chemin, string? cible, bool detourne)
    {
        RelaisServices.CheminDetourne(chemin, cible).ShouldBe(detourne);
    }

    [Theory]
    [InlineData(true, "cpmt")]
    [InlineData(true, "admin-fonctionnel", "offline_access")]
    [InlineData(true, "assistant-medical")]
    [InlineData(true, "responsable-centre")]
    [InlineData(false, "employeur")]
    [InlineData(false, "sipp")]
    [InlineData(false, "travailleur")]
    [InlineData(false, "integrations")]
    [InlineData(false, "exploitation")]
    [InlineData(false, "cpmt", "employeur")]
    [InlineData(false)]
    public void Seul_un_profil_interne_sans_role_externe_entre_dans_le_canal(bool attendu, params string[] roles)
    {
        var utilisateur = new ClaimsPrincipal(new ClaimsIdentity(roles.Select(r => new Claim("roles", r)), "test"));

        PerimetreInterne.EstInterne(utilisateur).ShouldBe(attendu);
    }
}
