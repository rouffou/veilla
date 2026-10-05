using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

internal static class Exemples
{
    public static readonly DateOnly Affiliation = new(2024, 1, 1);

    public static DonneesFiche Fiche(string denomination = "Boulangerie Dupont", CategorieTarifaire categorie = CategorieTarifaire.B) =>
        new(denomination, "SRL", "10.711", "118.03", categorie, Language.Fr, RegimeLinguistique.Francais);

    public static Affilie Affilie(string? seppOrigine = null) =>
        Domain.Affilies.Affilie.Creer(new NumeroBce("0202.239.951"), Fiche(), Affiliation, seppOrigine: seppOrigine);

    public static Adresse Adresse() => new("Rue de la Loi", "16", null, "1000", "Bruxelles");
}

public class AffilieTests
{
    [Fact]
    public void Un_affilie_cree_est_actif_en_version_1()
    {
        var affilie = Exemples.Affilie();

        affilie.Statut.ShouldBe(StatutAffilie.Actif);
        affilie.NumeroVersion.ShouldBe(1);
        affilie.NumeroBce.Formate.ShouldBe("0202.239.951");
        affilie.CodeNace.ShouldBe("10.711");
        affilie.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AffilieVersionne>();
        affilie.Operations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "SRL", "10.711", "118.03")]
    [InlineData("Dupont", "", "10.711", "118.03")]
    [InlineData("Dupont", "SRL", "10711", "118.03")]
    [InlineData("Dupont", "SRL", "10.711", "11803")]
    [InlineData("Dupont", "SRL", "A", "200")]
    public void Une_fiche_incomplete_ou_mal_codee_est_refusee(string denomination, string forme, string nace, string cp) =>
        Should.Throw<DomainException>(() => new DonneesFiche(denomination, forme, nace, cp, CategorieTarifaire.A, Language.Nl, RegimeLinguistique.Neerlandais));

    [Fact]
    public void Une_categorie_tarifaire_hors_A_a_D_est_refusee() =>
        Should.Throw<DomainException>(() => new DonneesFiche("Dupont", "SRL", "10.711", "200", (CategorieTarifaire)9, Language.Fr, RegimeLinguistique.Francais));

    [Fact]
    public void Modifier_la_fiche_incremente_la_version()
    {
        var affilie = Exemples.Affilie();
        var groupe = Guid.CreateVersion7();

        affilie.ModifierFiche(Exemples.Fiche("Dupont & Fils", CategorieTarifaire.C), groupe);

        affilie.Denomination.ShouldBe("Dupont & Fils");
        affilie.CategorieTarifaire.ShouldBe(CategorieTarifaire.C);
        affilie.GroupeId.ShouldBe(groupe);
        affilie.NumeroVersion.ShouldBe(2);
    }

    [Fact]
    public void Resilier_fixe_la_date_de_fin_et_le_statut()
    {
        var affilie = Exemples.Affilie();

        affilie.Resilier(new DateOnly(2026, 12, 31));

        affilie.Statut.ShouldBe(StatutAffilie.Resilie);
        affilie.DateFin.ShouldBe(new DateOnly(2026, 12, 31));
        affilie.EstAffilieAu(new DateOnly(2026, 12, 31)).ShouldBeTrue();
        affilie.EstAffilieAu(new DateOnly(2027, 1, 1)).ShouldBeFalse();
        affilie.EstAffilieAu(new DateOnly(2023, 12, 31)).ShouldBeFalse();
    }

    [Fact]
    public void La_fin_ne_peut_pas_preceder_l_affiliation() =>
        Should.Throw<DomainException>(() => Exemples.Affilie().Resilier(new DateOnly(2023, 1, 1)));

    [Fact]
    public void Une_resiliation_peut_etre_annulee()
    {
        var affilie = Exemples.Affilie();
        affilie.Resilier(new DateOnly(2026, 12, 31));

        affilie.AnnulerResiliation();

        affilie.Statut.ShouldBe(StatutAffilie.Actif);
        affilie.DateFin.ShouldBeNull();
        Should.Throw<DomainException>(affilie.AnnulerResiliation);
    }
}
