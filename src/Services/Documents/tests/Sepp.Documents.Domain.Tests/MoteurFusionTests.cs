using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Fusion;

using Shouldly;

namespace Sepp.Documents.Domain.Tests;

/// <summary>DOC-01 : le moteur de fusion est simple, typé et ne peut exécuter aucun code.</summary>
public class MoteurFusionTests
{
    private static readonly DefinitionChamp[] Champs =
    [
        new("nom", TypeChamp.Texte, true),
        new("date", TypeChamp.Date, false),
        new("montant", TypeChamp.Nombre, false),
        new("urgent", TypeChamp.Booleen, false),
        new("mesures", TypeChamp.Liste, false),
    ];

    private static Dictionary<string, ValeurChamp> Valeurs(params (string Nom, ValeurChamp Valeur)[] valeurs) =>
        valeurs.ToDictionary(v => v.Nom, v => v.Valeur, StringComparer.Ordinal);

    [Fact]
    public void Un_champ_est_remplace_par_sa_valeur()
    {
        var resultat = MoteurFusion.Fusionner("# Bonjour {{nom}}", Champs, Valeurs(("nom", ValeurChamp.Simple("Marie"))), Language.Fr);

        resultat.Blocs.ShouldHaveSingleItem().ShouldBe(new Bloc(TypeBloc.Titre, "Bonjour Marie"));
    }

    [Fact]
    public void La_structure_vient_du_modele_et_jamais_des_valeurs()
    {
        // Une valeur qui ressemble à du balisage reste du texte brut : ni titre, ni puce, ni balise, ni nouvelle ligne.
        var hostile = "# Titre\n- puce\n{{#si nom}} {{nom}} {{/si}}\n---";

        var resultat = MoteurFusion.Fusionner("Texte : {{nom}}", Champs, Valeurs(("nom", ValeurChamp.Simple(hostile))), Language.Fr);

        var bloc = resultat.Blocs.ShouldHaveSingleItem();
        bloc.Type.ShouldBe(TypeBloc.Paragraphe);
        bloc.Texte.ShouldBe("Texte : # Titre - puce {{#si nom}} {{nom}} {{/si}} ---");
    }

    [Theory]
    [InlineData("{{System.Environment.Exit(1)}}")]
    [InlineData("{{nom.ToUpper()}}")]
    [InlineData("{{ 1 + 1 }}")]
    [InlineData("{{#executer nom}}")]
    [InlineData("{{Nom}}")]
    public void Une_balise_qui_n_est_pas_un_champ_declare_est_refusee(string contenu)
    {
        var erreurs = MoteurFusion.Analyser(contenu, Champs);

        erreurs.ShouldNotBeEmpty();
    }

    [Fact]
    public void Un_champ_non_declare_est_refuse_a_l_analyse_et_a_la_fusion()
    {
        MoteurFusion.Analyser("{{inconnu}}", Champs).ShouldContain(e => e.Contains("non déclaré", StringComparison.Ordinal));
        Should.Throw<DomainException>(() => MoteurFusion.Fusionner("{{nom}}", Champs,
            Valeurs(("nom", ValeurChamp.Simple("x")), ("autre", ValeurChamp.Simple("y"))), Language.Fr));
    }

    [Fact]
    public void Un_champ_obligatoire_absent_est_refuse()
    {
        var ex = Should.Throw<DomainException>(() => MoteurFusion.Fusionner("{{nom}}", Champs, Valeurs(), Language.Fr));

        ex.Message.ShouldContain("nom");
    }

    [Fact]
    public void Les_dates_et_les_nombres_suivent_la_langue_du_document()
    {
        var valeurs = Valeurs(("nom", ValeurChamp.Simple("x")), ("date", ValeurChamp.Date(new DateOnly(2026, 3, 1))), ("montant", ValeurChamp.Simple("1234.5")));

        MoteurFusion.Fusionner("{{date}} / {{montant}}", Champs, valeurs, Language.Fr).TexteIntegral.ShouldBe("1 mars 2026 / 1234,5");
        MoteurFusion.Fusionner("{{date}} / {{montant}}", Champs, valeurs, Language.Nl).TexteIntegral.ShouldBe("1 maart 2026 / 1234,5");
        MoteurFusion.Fusionner("{{date}} / {{montant}}", Champs, valeurs, Language.De).TexteIntegral.ShouldStartWith("1. März 2026");
    }

    [Fact]
    public void Une_date_ou_un_nombre_mal_formes_sont_refuses()
    {
        Should.Throw<DomainException>(() => MoteurFusion.Fusionner("{{date}}", Champs,
            Valeurs(("nom", ValeurChamp.Simple("x")), ("date", ValeurChamp.Simple("01/03/2026"))), Language.Fr));
        Should.Throw<DomainException>(() => MoteurFusion.Fusionner("{{montant}}", Champs,
            Valeurs(("nom", ValeurChamp.Simple("x")), ("montant", ValeurChamp.Simple("beaucoup"))), Language.Fr));
    }

    [Fact]
    public void Une_condition_et_une_repetition_sont_evaluees()
    {
        const string modele = """
            {{#si urgent}}
            Urgent
            {{#sinon}}
            Normal
            {{/si}}

            {{#chaque mesures}}
            - {{.}}
            {{/chaque}}
            """;

        var urgent = MoteurFusion.Fusionner(modele, Champs, Valeurs(("nom", ValeurChamp.Simple("x")), ("urgent", ValeurChamp.Simple("true")),
            ("mesures", ValeurChamp.Liste(["a", "b"]))), Language.Fr);
        var normal = MoteurFusion.Fusionner(modele, Champs, Valeurs(("nom", ValeurChamp.Simple("x")), ("urgent", ValeurChamp.Simple("false"))), Language.Fr);

        urgent.Blocs.Select(b => b.Texte).ShouldBe(["Urgent", "a", "b"]);
        normal.Blocs.Select(b => b.Texte).ShouldBe(["Normal"]);
    }

    [Fact]
    public void Les_blocs_non_fermes_ou_mal_apparies_sont_refuses()
    {
        MoteurFusion.Analyser("{{#si urgent}}\nx", Champs).ShouldNotBeEmpty();
        MoteurFusion.Analyser("x\n{{/si}}", Champs).ShouldNotBeEmpty();
        MoteurFusion.Analyser("{{#chaque nom}}\nx\n{{/chaque}}", Champs).ShouldContain(e => e.Contains("Liste", StringComparison.Ordinal));
        MoteurFusion.Analyser("{{.}}", Champs).ShouldNotBeEmpty();
        MoteurFusion.Analyser("{{#sinon}}", Champs).ShouldNotBeEmpty();
    }

    [Fact]
    public void Les_valeurs_trop_longues_sont_refusees_et_les_caracteres_de_controle_neutralises()
    {
        Should.Throw<DomainException>(() => MoteurFusion.Fusionner("{{nom}}", Champs,
            Valeurs(("nom", ValeurChamp.Simple(new string('a', MoteurFusion.LongueurMaximaleValeur + 1)))), Language.Fr));

        MoteurFusion.Fusionner("{{nom}}", Champs, Valeurs(("nom", ValeurChamp.Simple("a\u0000b\r\nc\u0007d"))), Language.Fr)
            .TexteIntegral.ShouldBe("a b c d");
    }

    [Fact]
    public void Deux_champs_de_meme_nom_ou_un_nom_invalide_sont_refuses()
    {
        MoteurFusion.Analyser("x", [new("a", TypeChamp.Texte, false), new("a", TypeChamp.Texte, false)]).ShouldNotBeEmpty();
        MoteurFusion.Analyser("x", [new("A b", TypeChamp.Texte, false)]).ShouldNotBeEmpty();
    }
}
