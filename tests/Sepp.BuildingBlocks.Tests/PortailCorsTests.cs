using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    private static CorsPolicy Politique(Action<WebApplicationBuilder> cors)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration["Cors:OriginesAutorisees:0"] = "http://localhost:8081";
        cors(builder);
        using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
        return options.GetPolicy(options.DefaultPolicyName)!;
    }

    [Fact]
    public void La_surcharge_simple_garde_les_en_tetes_communs_des_portails()
    {
        var politique = Politique(b => b.AddSeppPortailCors("GET", "POST"));

        politique.Methods.ShouldBe(["GET", "POST"]);
        politique.Headers.ShouldBe(["Authorization", "Content-Type", "Accept-Language", "X-Correlation-Id"]);
        politique.ExposedHeaders.ShouldBe(["Content-Disposition", "X-Correlation-Id"]);
        politique.Origins.ShouldBe(["http://localhost:8081"]);
        politique.SupportsCredentials.ShouldBeFalse();
    }

    [Fact]
    public void La_surcharge_avec_en_tetes_supplementaires_les_ajoute_aux_en_tetes_communs()
    {
        var politique = Politique(b => b.AddSeppPortailCors(["GET", "POST", "PUT", "DELETE"], ["X-Motif-Acces", "X-Bris-De-Glace", "authorization"]));

        politique.Methods.ShouldBe(["GET", "POST", "PUT", "DELETE"]);
        politique.Headers.ShouldBe(["Authorization", "Content-Type", "Accept-Language", "X-Correlation-Id", "X-Motif-Acces", "X-Bris-De-Glace"]);
        politique.Origins.ShouldBe(["http://localhost:8081"]);
        politique.SupportsCredentials.ShouldBeFalse();
        politique.AllowAnyHeader.ShouldBeFalse();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    public void Un_en_tete_supplementaire_joker_ou_vide_est_refuse(string entete)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });

        Should.Throw<ArgumentException>(() => builder.AddSeppPortailCors(["GET"], [entete]));
    }
}
