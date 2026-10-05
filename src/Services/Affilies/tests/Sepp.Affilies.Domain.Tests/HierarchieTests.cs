using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

public class HierarchieTests
{
    private static readonly DateOnly Debut = new(2024, 1, 1);
    private readonly Affilie _affilie = Exemples.Affilie();

    private UniteEtablissement Unite() =>
        _affilie.AjouterUniteEtablissement(new NumeroUniteEtablissement("2.123.456.791"), "Siège", Exemples.Adresse(), Language.Fr, Debut);

    [Fact]
    public void La_hierarchie_unite_site_departements_se_construit()
    {
        var unite = Unite();
        var site = _affilie.AjouterSite(unite.Id, "Atelier", Exemples.Adresse(), 50.846, 4.352, Debut);
        var production = _affilie.AjouterDepartement(site.Id, "Production", null, Debut);
        var four = _affilie.AjouterDepartement(site.Id, "Four", production.Id, Debut);

        _affilie.UnitesEtablissement.ShouldHaveSingleItem().Sites.ShouldHaveSingleItem().Departements.Count.ShouldBe(2);
        four.ParentId.ShouldBe(production.Id);
        site.Latitude.ShouldBe(50.846);
        _affilie.NumeroVersion.ShouldBe(5);
    }

    [Fact]
    public void Une_unite_d_etablissement_n_est_rattachee_qu_une_fois()
    {
        Unite();
        Should.Throw<DomainException>(Unite);
    }

    [Theory]
    [InlineData(50.0, null)]
    [InlineData(91.0, 4.0)]
    [InlineData(50.0, 181.0)]
    public void Des_coordonnees_invalides_sont_refusees(double? latitude, double? longitude)
    {
        var unite = Unite();
        Should.Throw<DomainException>(() => _affilie.AjouterSite(unite.Id, "Atelier", Exemples.Adresse(), latitude, longitude, Debut));
    }

    [Fact]
    public void Un_departement_ne_peut_pas_devenir_son_propre_descendant()
    {
        var site = _affilie.AjouterSite(Unite().Id, "Atelier", Exemples.Adresse(), null, null, Debut);
        var parent = _affilie.AjouterDepartement(site.Id, "Production", null, Debut);
        var enfant = _affilie.AjouterDepartement(site.Id, "Four", parent.Id, Debut);

        Should.Throw<DomainException>(() => _affilie.ModifierDepartement(parent.Id, "Production", enfant.Id)).Message.ShouldContain("sous-départements");
        Should.Throw<DomainException>(() => _affilie.ModifierDepartement(parent.Id, "Production", parent.Id));
    }

    [Fact]
    public void Un_departement_parent_doit_appartenir_au_meme_site()
    {
        var unite = Unite();
        var site1 = _affilie.AjouterSite(unite.Id, "Atelier", Exemples.Adresse(), null, null, Debut);
        var site2 = _affilie.AjouterSite(unite.Id, "Magasin", Exemples.Adresse(), null, null, Debut);
        var departement = _affilie.AjouterDepartement(site1.Id, "Production", null, Debut);

        Should.Throw<DomainException>(() => _affilie.AjouterDepartement(site2.Id, "Vente", departement.Id, Debut));
    }

    [Fact]
    public void Fermer_une_unite_ferme_ses_sites_et_departements()
    {
        var unite = Unite();
        var site = _affilie.AjouterSite(unite.Id, "Atelier", Exemples.Adresse(), null, null, Debut);
        _affilie.AjouterDepartement(site.Id, "Production", null, Debut);

        _affilie.FermerUniteEtablissement(unite.Id, new DateOnly(2026, 1, 1));

        unite.Validite.ValidTo.ShouldBe(new DateOnly(2026, 1, 1));
        site.Validite.IsOpen.ShouldBeFalse();
        site.Departements.ShouldAllBe(d => !d.Validite.IsOpen);
        Should.Throw<DomainException>(() => _affilie.AjouterSite(unite.Id, "Nouveau", Exemples.Adresse(), null, null, new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Fermer_un_departement_ferme_sa_branche()
    {
        var site = _affilie.AjouterSite(Unite().Id, "Atelier", Exemples.Adresse(), null, null, Debut);
        var parent = _affilie.AjouterDepartement(site.Id, "Production", null, Debut);
        var enfant = _affilie.AjouterDepartement(site.Id, "Four", parent.Id, Debut);
        var autre = _affilie.AjouterDepartement(site.Id, "Vente", null, Debut);

        _affilie.FermerDepartement(parent.Id, new DateOnly(2025, 1, 1));

        parent.Validite.IsOpen.ShouldBeFalse();
        enfant.Validite.IsOpen.ShouldBeFalse();
        autre.Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Un_site_ne_debute_pas_avant_son_unite()
    {
        var unite = Unite();
        Should.Throw<DomainException>(() => _affilie.AjouterSite(unite.Id, "Atelier", Exemples.Adresse(), null, null, new DateOnly(2023, 1, 1)));
    }

    [Fact]
    public void Un_element_inconnu_leve_une_exception_d_element_introuvable() =>
        Should.Throw<ElementIntrouvableException>(() => _affilie.AjouterSite(Guid.CreateVersion7(), "Atelier", Exemples.Adresse(), null, null, Debut));
}
