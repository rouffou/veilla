using Sepp.BuildingBlocks.Domain;
using Sepp.Referentiels.Domain.Nomenclatures;
using Shouldly;

namespace Sepp.Referentiels.Domain.Tests;

public class NomenclatureTests
{
    private static readonly DateOnly Debut = new(2026, 1, 1);

    private static Nomenclature Nace()
    {
        var nace = Nomenclature.Creer("nace", new LocalizedLabel("Codes NACE", "NACE-codes", "NACE-Codes", "NACE codes"));
        nace.AjouterEntree("A", new LocalizedLabel("Agriculture", "Landbouw", "Landwirtschaft"), Debut);
        return nace;
    }

    [Fact]
    public void Le_code_est_normalise_et_la_version_incrementee()
    {
        var nace = Nace();

        nace.Code.ShouldBe("NACE");
        nace.NumeroVersion.ShouldBe(2);
    }

    [Fact]
    public void Le_libelle_est_rendu_dans_la_langue_demandee_avec_repli_en_francais()
    {
        var entree = Nace().EntreesAu(Debut).Single();

        entree.Libelle.In(Language.Nl).ShouldBe("Landbouw");
        entree.Libelle.In(Language.En).ShouldBe("Agriculture");
    }

    [Fact]
    public void Modifier_un_libelle_cree_une_nouvelle_periode()
    {
        var nace = Nace();
        nace.ModifierLibelle("A", new LocalizedLabel("Agriculture, sylviculture", "Landbouw, bosbouw", "Land- und Forstwirtschaft"), new DateOnly(2027, 1, 1));

        nace.EntreesAu(new DateOnly(2026, 6, 1)).Single().Libelle.Fr.ShouldBe("Agriculture");
        nace.EntreesAu(new DateOnly(2027, 6, 1)).Single().Libelle.Fr.ShouldBe("Agriculture, sylviculture");
        nace.Entrees.Count.ShouldBe(2);
    }

    [Fact]
    public void Un_code_retire_n_est_plus_en_vigueur_mais_reste_historise()
    {
        var nace = Nace();
        nace.RetirerEntree("A", new DateOnly(2027, 1, 1));

        nace.EntreesAu(new DateOnly(2027, 1, 1)).ShouldBeEmpty();
        nace.Entrees.Count.ShouldBe(1);
    }

    [Fact]
    public void Un_code_en_double_sur_la_meme_periode_est_refuse() =>
        Should.Throw<DomainException>(() => Nace().AjouterEntree("a", new LocalizedLabel("x", "y", "z"), new DateOnly(2026, 5, 1)));

    [Fact]
    public void Un_parent_inconnu_est_refuse() =>
        Should.Throw<DomainException>(() => Nace().AjouterEntree("01", new LocalizedLabel("x", "y", "z"), Debut, "B"));

    [Fact]
    public void Les_libelles_fr_nl_de_sont_obligatoires() =>
        Should.Throw<DomainException>(() => new LocalizedLabel("Agriculture", "", "Landwirtschaft"));
}
