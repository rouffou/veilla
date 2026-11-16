using Sepp.Testing.Architecture;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

// Garde-fou (VariablesTerraform) : la lecture de Terraform ignore les commentaires HCL sans abîmer le contenu des chaînes.
public class SansCommentairesTests
{
    [Fact]
    public void Un_commentaire_bloc_sur_plusieurs_lignes_est_retire()
    {
        var source = "avant = 1\n/* premier\n   actif = false\n   dernier */\napres = 2\n";

        var resultat = VariablesTerraform.SansCommentaires(source);

        resultat.ShouldBe("avant = 1\n\napres = 2\n");
        resultat.ShouldNotContain("actif");
    }

    [Fact]
    public void Les_commentaires_dieze_et_double_barre_sont_retires_jusqu_a_la_fin_de_ligne()
    {
        var source = "a = 1 # commentaire\nb = 2 // autre\nc = 3\n";

        VariablesTerraform.SansCommentaires(source).ShouldBe("a = 1 \nb = 2 \nc = 3\n");
    }

    [Fact]
    public void Les_marqueurs_de_commentaire_dans_une_chaine_sont_conserves()
    {
        var source = "url = \"https://exemple.be/#ancre\"\nmotif = \"a /* pas un commentaire */ b\"\necho = \"x # y // z\" # vrai commentaire\n";

        var resultat = VariablesTerraform.SansCommentaires(source);

        resultat.ShouldContain("\"https://exemple.be/#ancre\"");
        resultat.ShouldContain("\"a /* pas un commentaire */ b\"");
        resultat.ShouldContain("\"x # y // z\"");
        resultat.ShouldNotContain("vrai commentaire");
    }

    [Fact]
    public void Un_guillemet_echappe_ne_ferme_pas_la_chaine()
    {
        var source = "t = \"a \\\" # reste dans la chaine\" # retire\n";

        var resultat = VariablesTerraform.SansCommentaires(source);

        resultat.ShouldContain("# reste dans la chaine");
        resultat.ShouldNotContain("retire");
    }

    [Fact]
    public void Un_actif_false_commente_n_inactive_pas_le_service()
    {
        VariablesTerraform.EstActif("{ zone = \"standard\" /* actif = false\n */ }").ShouldBeTrue();
        VariablesTerraform.EstActif("{ zone = \"standard\", actif = false }").ShouldBeFalse();
    }
}
