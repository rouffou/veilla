using Sepp.BuildingBlocks.Web;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

// ARC-42, ARC-43, NF-03 : validation des origines CORS des BFF de portail.
public class PortailCorsTests
{
    [Fact]
    public void Des_origines_https_exactes_sont_acceptees_et_normalisees()
    {
        var origines = PortailCors.ValiderOrigines(["https://Portail.Example.be", "https://portail.example.be:8443", "https://portail.example.be"], production: true);

        origines.ShouldBe(["https://portail.example.be", "https://portail.example.be:8443"]);
    }

    [Fact]
    public void Une_liste_vide_echoue_en_production()
    {
        Should.Throw<InvalidOperationException>(() => PortailCors.ValiderOrigines([], production: true));
        Should.Throw<InvalidOperationException>(() => PortailCors.ValiderOrigines(null, production: true));
    }

    [Fact]
    public void Une_liste_vide_est_acceptee_hors_production()
    {
        PortailCors.ValiderOrigines([], production: false).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.be")]
    [InlineData("http://portail.example.be")]
    [InlineData("https://portail.example.be/")]
    [InlineData("https://portail.example.be/app")]
    [InlineData("https://portail.example.be?x=1")]
    [InlineData("https://portail.example.be#a")]
    [InlineData("https://user:mdp@portail.example.be")]
    [InlineData("portail.example.be")]
    [InlineData("")]
    [InlineData("  ")]
    public void Une_origine_invalide_echoue_en_production(string origine)
    {
        Should.Throw<InvalidOperationException>(() => PortailCors.ValiderOrigines([origine], production: true));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("http://localhost:4201/app")]
    [InlineData("ftp://localhost")]
    public void Joker_chemin_et_schema_inconnu_sont_refuses_aussi_hors_production(string origine)
    {
        Should.Throw<InvalidOperationException>(() => PortailCors.ValiderOrigines([origine], production: false));
    }

    [Fact]
    public void Http_est_accepte_hors_production()
    {
        PortailCors.ValiderOrigines(["http://localhost:8082"], production: false).ShouldBe(["http://localhost:8082"]);
    }

    [Fact]
    public void Un_joker_parmi_des_origines_valides_fait_echouer_la_liste()
    {
        Should.Throw<InvalidOperationException>(() => PortailCors.ValiderOrigines(["https://portail.example.be", "*"], production: true));
    }
}
