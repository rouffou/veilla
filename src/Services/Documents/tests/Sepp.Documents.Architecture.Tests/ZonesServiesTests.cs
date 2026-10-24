using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

using Sepp.Documents.Adapters.Securite;
using Sepp.Documents.Domain.Commun;

using Shouldly;

namespace Sepp.Documents.Architecture.Tests;

/// <summary>Garde de zone (décision #356) : hors développement et test, une instance sert exactement la zone de son environnement.</summary>
public sealed class ZonesServiesTests
{
    private static IConfiguration Configuration(string? zones, string? zoneEnvironnement, bool liste = true)
    {
        var valeurs = new Dictionary<string, string?>();
        if (zones is not null)
        {
            if (liste)
            {
                var i = 0;
                foreach (var z in zones.Split(','))
                {
                    valeurs[$"Documents:Zones:{i++}"] = z;
                }
            }
            else
            {
                valeurs["Documents:Zones"] = zones;
            }
        }

        if (zoneEnvironnement is not null)
        {
            valeurs["Sepp:Zone"] = zoneEnvironnement;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build();
    }

    private static HostingEnvironment Environnement(string nom) => new() { EnvironmentName = nom };

    [Fact]
    public void La_liste_des_zones_est_obligatoire_meme_en_developpement()
    {
        Should.Throw<InvalidOperationException>(() => ZonesServies.Depuis(Configuration(null, null), Environnement(Environments.Development)));
    }

    [Fact]
    public void Une_zone_inconnue_est_refusee()
    {
        Should.Throw<InvalidOperationException>(() => ZonesServies.Depuis(Configuration("inconnue", "standard"), Environnement(Environments.Production)));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Hors_developpement_et_test_une_seule_zone_egale_a_celle_de_l_environnement_est_acceptee(string environnement)
    {
        var zones = ZonesServies.Depuis(Configuration("medicale", "medicale"), Environnement(environnement));

        zones.Zones.ShouldBe([ZoneDocument.Medicale]);
        zones.Sert(ZoneDocument.Medicale).ShouldBeTrue();
        zones.Sert(ZoneDocument.Standard).ShouldBeFalse();
    }

    [Fact]
    public void Une_variable_d_environnement_a_valeur_simple_est_acceptee()
    {
        ZonesServies.Depuis(Configuration("standard", "standard", liste: false), Environnement(Environments.Production)).Zones.ShouldBe([ZoneDocument.Standard]);
    }

    [Fact]
    public void Hors_developpement_et_test_plusieurs_zones_sont_refusees()
    {
        Should.Throw<InvalidOperationException>(() =>
            ZonesServies.Depuis(Configuration("standard,medicale", "standard"), Environnement(Environments.Production)));
    }

    [Fact]
    public void Hors_developpement_et_test_une_zone_differente_de_l_environnement_est_refusee()
    {
        Should.Throw<InvalidOperationException>(() =>
            ZonesServies.Depuis(Configuration("medicale", "standard"), Environnement(Environments.Production)));
    }

    [Fact]
    public void Hors_developpement_et_test_la_zone_de_l_environnement_est_obligatoire()
    {
        Should.Throw<InvalidOperationException>(() =>
            ZonesServies.Depuis(Configuration("standard", null), Environnement(Environments.Production)));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Testing")]
    public void En_developpement_et_en_test_une_instance_peut_servir_les_trois_zones(string environnement)
    {
        var zones = ZonesServies.Depuis(Configuration("standard,medicale,psychosociale", null), Environnement(environnement));

        zones.Zones.ShouldBe([ZoneDocument.Standard, ZoneDocument.Medicale, ZoneDocument.Psychosociale]);
    }
}
