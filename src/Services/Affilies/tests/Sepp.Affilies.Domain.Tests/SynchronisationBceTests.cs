using System.Globalization;

using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

/// <summary>AFF-01, AFF-02, INT-04 — Application des données BCE à l'affilié, historisée par <c>Validity</c> (DAT-04).</summary>
public class SynchronisationBceTests
{
    private static readonly DateOnly Extraction = new(2026, 10, 9);
    private static readonly IReadOnlySet<string> AucuneAutre = new HashSet<string>();
    private readonly Affilie _affilie = Exemples.Affilie();

    /// <summary>Numéro d'unité valide (modulo 97) construit à partir de huit chiffres.</summary>
    private static string NumeroUnite(string huitChiffres) =>
        huitChiffres + (97 - (long.Parse(huitChiffres, CultureInfo.InvariantCulture) % 97)).ToString("00", CultureInfo.InvariantCulture);

    private static UniteBce Unite(string numero, string nom = "Siège BCE", string rue = "Rue de la Loi", string codePostal = "1000", DateOnly? debut = null) =>
        new(numero, nom, rue, "16", null, codePostal, "Bruxelles", "BE", debut ?? new DateOnly(2010, 1, 1));

    private static DonneesBce Donnees(string denomination = "Boulangerie Dupont SRL", string forme = "SRL", string nace = "10.711", params UniteBce[] unites) =>
        new(denomination, forme, nace, unites);

    [Fact]
    public void La_fiche_prend_la_denomination_la_forme_et_le_NACE_de_la_BCE_sans_toucher_au_reste()
    {
        var versionAvant = _affilie.NumeroVersion;

        var ecarts = _affilie.AppliquerDonneesBce(Donnees("Dupont & Fils SA", "SA", "47.110"), Extraction, AucuneAutre);

        ecarts.ShouldBeEmpty();
        _affilie.Denomination.ShouldBe("Dupont & Fils SA");
        _affilie.FormeJuridique.ShouldBe("SA");
        _affilie.CodeNace.ShouldBe("47.110");
        _affilie.CommissionParitaire.ShouldBe("118.03");
        _affilie.CategorieTarifaire.ShouldBe(CategorieTarifaire.B);
        _affilie.Statut.ShouldBe(StatutAffilie.Actif);
        _affilie.NumeroVersion.ShouldBe(versionAvant + 1);
    }

    [Fact]
    public void Des_donnees_identiques_ne_modifient_rien()
    {
        var unite = Unite(NumeroUnite("21234567"));
        _affilie.AppliquerDonneesBce(Donnees("Dupont SRL", "SRL", "10.711", unite), Extraction, AucuneAutre);
        var version = _affilie.NumeroVersion;

        var ecarts = _affilie.AppliquerDonneesBce(Donnees("Dupont SRL", "SRL", "10.711", unite), Extraction.AddDays(1), AucuneAutre);

        ecarts.ShouldBeEmpty();
        _affilie.NumeroVersion.ShouldBe(version);
        _affilie.UnitesEtablissement.ShouldHaveSingleItem();
    }

    [Fact]
    public void Une_unite_nouvelle_est_ouverte_a_sa_date_de_debut_BCE_ou_a_defaut_a_la_date_d_extraction()
    {
        var avecDebut = Unite(NumeroUnite("21234567"), debut: new DateOnly(2012, 3, 1));
        var sansDebut = avecDebut with { Numero = NumeroUnite("21234568"), DateDebut = null };

        _affilie.AppliquerDonneesBce(Donnees(unites: [avecDebut, sansDebut]), Extraction, AucuneAutre).ShouldBeEmpty();

        _affilie.UnitesEtablissement.Count.ShouldBe(2);
        _affilie.UnitesEtablissement.Single(u => u.Numero.Value == avecDebut.Numero).Validite.ValidFrom.ShouldBe(new DateOnly(2012, 3, 1));
        _affilie.UnitesEtablissement.Single(u => u.Numero.Value == sansDebut.Numero).Validite.ValidFrom.ShouldBe(Extraction);
        _affilie.UnitesEtablissement.ShouldAllBe(u => u.Validite.IsOpen && u.Langue == Language.Fr);
    }

    [Fact]
    public void Le_nom_et_l_adresse_d_une_unite_existante_sont_mis_a_jour_sans_la_fermer()
    {
        var numero = NumeroUnite("21234567");
        _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(numero)]), Extraction, AucuneAutre);
        var idUnite = _affilie.UnitesEtablissement.Single().Id;

        var ecarts = _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(numero, "Nouveau siège", "Avenue Louise", "1050")]), Extraction.AddDays(1), AucuneAutre);

        ecarts.ShouldBeEmpty();
        var unite = _affilie.UnitesEtablissement.ShouldHaveSingleItem();
        unite.Id.ShouldBe(idUnite);
        unite.Nom.ShouldBe("Nouveau siège");
        unite.Adresse.Rue.ShouldBe("Avenue Louise");
        unite.Adresse.CodePostal.ShouldBe("1050");
        unite.Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Une_unite_absente_de_la_BCE_sans_site_ouvert_est_fermee_a_la_date_d_extraction_et_jamais_supprimee()
    {
        var gardee = Unite(NumeroUnite("21234567"));
        var disparue = Unite(NumeroUnite("21234568"));
        _affilie.AppliquerDonneesBce(Donnees(unites: [gardee, disparue]), Extraction, AucuneAutre);

        var ecarts = _affilie.AppliquerDonneesBce(Donnees(unites: [gardee]), Extraction.AddDays(30), AucuneAutre);

        ecarts.ShouldBeEmpty();
        _affilie.UnitesEtablissement.Count.ShouldBe(2);
        var fermee = _affilie.UnitesEtablissement.Single(u => u.Numero.Value == disparue.Numero);
        fermee.Validite.ValidTo.ShouldBe(Extraction.AddDays(30));
        _affilie.UnitesEtablissement.Single(u => u.Numero.Value == gardee.Numero).Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Une_unite_absente_qui_porte_un_site_ouvert_n_est_pas_fermee_et_devient_un_ecart()
    {
        var numero = NumeroUnite("21234567");
        var autre = Unite(NumeroUnite("21234568"));
        _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(numero), autre]), Extraction, AucuneAutre);
        _affilie.AjouterSite(_affilie.UnitesEtablissement.Single(u => u.Numero.Value == numero).Id, "Atelier", Exemples.Adresse(), null, null, Extraction);

        var ecarts = _affilie.AppliquerDonneesBce(Donnees(unites: [autre]), Extraction.AddDays(5), AucuneAutre);

        var ecart = ecarts.ShouldHaveSingleItem();
        ecart.Code.ShouldBe(CodesEcartBce.UniteAbsente);
        ecart.Reference.ShouldBe(numero);
        _affilie.UnitesEtablissement.Single(u => u.Numero.Value == numero).Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Une_liste_d_unites_vide_ne_ferme_rien_et_signale_un_ecart()
    {
        _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(NumeroUnite("21234567"))]), Extraction, AucuneAutre);

        var ecarts = _affilie.AppliquerDonneesBce(Donnees(), Extraction.AddDays(1), AucuneAutre);

        ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.UnitesAbsentes);
        _affilie.UnitesEtablissement.ShouldHaveSingleItem().Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Des_unites_non_communiquees_restent_inchangees()
    {
        _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(NumeroUnite("21234567"))]), Extraction, AucuneAutre);

        var ecarts = _affilie.AppliquerDonneesBce(new DonneesBce("Dupont SRL", "SRL", "10.711", null), Extraction.AddDays(1), AucuneAutre);

        ecarts.ShouldBeEmpty();
        _affilie.UnitesEtablissement.ShouldHaveSingleItem().Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Une_unite_deja_rattachee_a_un_autre_affilie_est_un_ecart_et_n_est_pas_ajoutee()
    {
        var prise = NumeroUnite("21234567");
        var libre = Unite(NumeroUnite("21234568"));

        var ecarts = _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(prise), libre]), Extraction, new HashSet<string> { prise });

        var ecart = ecarts.ShouldHaveSingleItem();
        ecart.Code.ShouldBe(CodesEcartBce.UniteAutreAffilie);
        ecart.Reference.ShouldBe(prise);
        _affilie.UnitesEtablissement.ShouldHaveSingleItem().Numero.Value.ShouldBe(libre.Numero);
    }

    [Fact]
    public void Une_unite_fermee_chez_l_affilie_mais_active_a_la_BCE_est_un_ecart_sans_reouverture()
    {
        var numero = NumeroUnite("21234567");
        _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(numero), Unite(NumeroUnite("21234568"))]), Extraction, AucuneAutre);
        _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(NumeroUnite("21234568"))]), Extraction.AddDays(10), AucuneAutre);

        var ecarts = _affilie.AppliquerDonneesBce(Donnees(unites: [Unite(numero), Unite(NumeroUnite("21234568"))]), Extraction.AddDays(20), AucuneAutre);

        ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.UniteFermee);
        _affilie.UnitesEtablissement.Single(u => u.Numero.Value == numero).Validite.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Une_donnee_invalide_est_un_ecart_et_le_reste_est_applique()
    {
        var valide = Unite(NumeroUnite("21234567"));
        var invalide = Unite("1234567890");
        var sansRue = Unite(NumeroUnite("21234568"), rue: " ");

        var ecarts = _affilie.AppliquerDonneesBce(Donnees("Dupont & Fils SA", "SA", "47.110", valide, invalide, sansRue), Extraction, AucuneAutre);

        ecarts.Select(e => e.Code).ShouldBe([CodesEcartBce.UniteInvalide, CodesEcartBce.UniteInvalide]);
        _affilie.UnitesEtablissement.ShouldHaveSingleItem().Numero.Value.ShouldBe(valide.Numero);
        _affilie.Denomination.ShouldBe("Dupont & Fils SA");
    }

    [Fact]
    public void Une_identification_invalide_est_un_ecart_et_la_fiche_reste_inchangee()
    {
        var ecarts = _affilie.AppliquerDonneesBce(Donnees("Dupont SRL", "SRL", "pas-un-code"), Extraction, AucuneAutre);

        ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.FicheInvalide);
        _affilie.Denomination.ShouldBe("Boulangerie Dupont");
        _affilie.CodeNace.ShouldBe("10.711");
    }

    [Theory]
    [InlineData(StatutAffilie.Resilie)]
    [InlineData(StatutAffilie.Actif)]
    public void Un_affilie_resilie_ou_actif_est_mis_a_jour(StatutAffilie statut)
    {
        if (statut == StatutAffilie.Resilie)
        {
            _affilie.Resilier(new DateOnly(2025, 12, 31));
        }

        _affilie.AppliquerDonneesBce(Donnees("Nouvelle dénomination"), Extraction, AucuneAutre).ShouldBeEmpty();

        _affilie.Denomination.ShouldBe("Nouvelle dénomination");
        _affilie.Statut.ShouldBe(statut);
    }

    [Fact]
    public void Un_affilie_absorbe_n_est_pas_modifie_et_devient_un_ecart()
    {
        var effet = new DateOnly(2026, 6, 1);
        var fusion = _affilie.ProjeterFusion(Guid.CreateVersion7(), effet);
        _affilie.RealiserOperation(fusion.Id, effet);
        var version = _affilie.NumeroVersion;

        var ecarts = _affilie.AppliquerDonneesBce(Donnees("Autre nom"), Extraction, AucuneAutre);

        ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.AffilieCloture);
        _affilie.Denomination.ShouldBe("Boulangerie Dupont");
        _affilie.NumeroVersion.ShouldBe(version);
    }
}
