using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Modeles;

using Shouldly;

namespace Sepp.Documents.Domain.Tests;

/// <summary>DOC-01 : versions de modèles, validation avant publication.</summary>
public class ModeleTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static Modele Brouillon(ZoneDocument zone = ZoneDocument.Standard) =>
        Modele.Creer("courrier.test", Language.Fr, TypeModele.Courrier, zone, "Courrier de test", null, "# Bonjour {{nom}}",
            [new ChampDeclare("nom", TypeChamp.Texte, true, "Nom")]);

    [Fact]
    public void Un_modele_est_cree_en_brouillon_version_1_avec_un_code_normalise()
    {
        var modele = Brouillon();

        modele.Code.ShouldBe("COURRIER.TEST");
        modele.Version.ShouldBe(1);
        modele.Statut.ShouldBe(StatutModele.Brouillon);
        modele.Champs.ShouldHaveSingleItem().Nom.ShouldBe("nom");
    }

    [Fact]
    public void Un_contenu_avec_un_champ_non_declare_ne_peut_pas_etre_enregistre()
    {
        var ex = Should.Throw<DomainException>(() => Modele.Creer("X", Language.Fr, TypeModele.Courrier, ZoneDocument.Standard, "X", null, "{{inconnu}}", []));

        ex.Message.ShouldContain("inconnu");
    }

    [Fact]
    public void Un_modele_est_valide_puis_publie_et_n_est_plus_modifiable()
    {
        var modele = Brouillon();

        modele.Valider("admin", Maintenant);
        modele.Statut.ShouldBe(StatutModele.Valide);
        modele.ValidePar.ShouldBe("admin");
        Should.Throw<DomainException>(() => modele.ModifierBrouillon("X", null, "texte", []));

        modele.Publier("admin", Maintenant);
        modele.Statut.ShouldBe(StatutModele.Publie);
        Should.Throw<DomainException>(() => modele.ModifierBrouillon("X", null, "texte", []));
    }

    [Fact]
    public void Un_modele_non_valide_ne_peut_pas_etre_publie() =>
        Should.Throw<DomainException>(() => Brouillon().Publier("admin", Maintenant)).Message.ShouldContain("validé");

    [Fact]
    public void Une_nouvelle_version_reprend_le_contenu_et_exige_un_numero_superieur()
    {
        var modele = Brouillon();
        modele.Valider("admin", Maintenant);
        modele.Publier("admin", Maintenant);

        var suivante = modele.NouvelleVersion(2);

        suivante.Version.ShouldBe(2);
        suivante.Statut.ShouldBe(StatutModele.Brouillon);
        suivante.Contenu.ShouldBe(modele.Contenu);
        suivante.Champs.Select(c => c.Nom).ShouldBe(["nom"]);
        Should.Throw<DomainException>(() => modele.NouvelleVersion(1));
    }

    [Fact]
    public void Une_version_validee_peut_etre_renvoyee_en_brouillon()
    {
        var modele = Brouillon();
        modele.Valider("admin", Maintenant);

        modele.RenvoyerEnBrouillon();

        modele.Statut.ShouldBe(StatutModele.Brouillon);
        modele.ValidePar.ShouldBeNull();
    }

    [Fact]
    public void Seule_la_version_publiee_peut_etre_retiree()
    {
        var modele = Brouillon();
        Should.Throw<DomainException>(modele.Retirer);
        modele.Valider("admin", Maintenant);
        modele.Publier("admin", Maintenant);

        modele.Retirer();

        modele.Statut.ShouldBe(StatutModele.Retire);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("é")]
    public void Un_code_invalide_est_refuse(string code) =>
        Should.Throw<DomainException>(() => Modele.NormaliserCode(code));

    [Fact]
    public void La_fusion_d_un_modele_utilise_ses_champs_declares()
    {
        var resultat = Brouillon().Fusionner(new Dictionary<string, ValeurChamp> { ["nom"] = ValeurChamp.Simple("Marie") });

        resultat.TexteIntegral.ShouldBe("Bonjour Marie");
    }
}
