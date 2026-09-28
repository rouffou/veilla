using Sepp.BuildingBlocks.Domain;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Domain.Tests;

public class NissTests
{
    /// <summary>Construit un NISS valide (chiffre de contrôle modulo 97, préfixe « 2 » pour les naissances dès 2000).</summary>
    public static string Generer(string aammjj, int ordre, bool apres2000 = false)
    {
        var corps = long.Parse($"{aammjj}{ordre:D3}");
        var controle = 97 - ((apres2000 ? 2_000_000_000L + corps : corps) % 97);
        return $"{aammjj}{ordre:D3}{controle:D2}";
    }

    [Fact]
    public void Un_niss_ne_avant_2000_est_valide_et_normalise()
    {
        var niss = Niss.Parse("85.07.30-033.28");

        niss.Valeur.ShouldBe("85073003328");
        niss.NeApres2000.ShouldBeFalse();
        niss.DateNaissance().ShouldBe(new DateOnly(1985, 7, 30));
    }

    [Fact]
    public void Un_niss_ne_a_partir_de_2000_utilise_le_prefixe_2()
    {
        var valeur = Generer("050315", 123, apres2000: true);

        var niss = Niss.Parse(valeur);

        niss.NeApres2000.ShouldBeTrue();
        niss.DateNaissance().ShouldBe(new DateOnly(2005, 3, 15));
    }

    [Fact]
    public void Le_controle_calcule_sans_prefixe_est_refuse_pour_une_naissance_apres_2000()
    {
        // Même corps, mais chiffre de contrôle du siècle précédent : valide pour 1905, pas pour 2005.
        var niss = Niss.Parse(Generer("050315", 123, apres2000: false));

        niss.NeApres2000.ShouldBeFalse();
        niss.DateNaissance().ShouldBe(new DateOnly(1905, 3, 15));
    }

    [Theory]
    [InlineData("85073003329")]
    [InlineData("8507300332")]
    [InlineData("850730033281")]
    [InlineData("85O73003328")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_niss_invalide_est_refuse_sans_reveler_la_valeur(string? saisie)
    {
        var ex = Should.Throw<DomainException>(() => Niss.Parse(saisie));

        if (!string.IsNullOrEmpty(saisie))
        {
            ex.Message.ShouldNotContain(saisie);
        }
    }

    [Fact]
    public void Un_numero_bis_donne_la_date_de_naissance_sans_la_majoration_du_mois()
    {
        var niss = Niss.Parse(Generer("852730", 45));

        niss.DateNaissance().ShouldBe(new DateOnly(1985, 7, 30));
    }

    [Fact]
    public void Une_date_de_naissance_inconnue_n_est_pas_deduite()
    {
        var niss = Niss.Parse(Generer("850000", 45));

        niss.DateNaissance().ShouldBeNull();
    }

    [Fact]
    public void La_representation_textuelle_ne_contient_jamais_le_niss()
    {
        var niss = Niss.Parse("85073003328");

        niss.ToString().ShouldNotContain("85073003328");
        $"{niss}".ShouldNotContain("0730");
        niss.Masque().ShouldBe("XX.XX.XX-XXX.28");
    }
}
