using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Documents;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Application.EvaluationSante;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

using Shouldly;

namespace Sepp.Documents.Application.Tests;

/// <summary>Saga de reprise (§14.6 étape 5) : formulaire d'évaluation de santé en trois exemplaires à chaque décision.</summary>
public class EvaluationSanteTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Personne = Guid.CreateVersion7();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Contexte _ctx = new(Roles.Cpmt);

    private DecisionEmiseHandler Handler() => new(_ctx.Generateur, _ctx.Store, _ctx.Store);

    private static DecisionEmise Decision(string categorie = "APTE_AVEC_MESURES") =>
        new(Guid.CreateVersion7(), Personne, Affilie, categorie, ["AMENAGEMENT_POSTE", "PAS_DE_CHARGES"], new DateOnly(2027, 10, 5));

    /// <summary>Publie les modèles de départ, comme le ferait le département médical après relecture.</summary>
    private void PublierModelesParDefaut()
    {
        foreach (var d in ModelesParDefaut.Definitions)
        {
            var modele = Modele.Creer(d.Code, d.Langue, TypeModele.Formulaire, d.Zone, d.Libelle, ModelesParDefaut.Avertissement, d.Contenu, ModelesParDefaut.Champs);
            modele.Valider("cpmt-dirigeant", _ctx.Horloge.GetUtcNow());
            modele.Publier("admin", _ctx.Horloge.GetUtcNow());
            _ctx.Store.Add(modele);
        }
    }

    [Fact]
    public async Task Une_decision_produit_trois_exemplaires_dans_les_bonnes_zones()
    {
        PublierModelesParDefaut();
        var decision = Decision();

        await Handler().HandleAsync(decision, _ct);

        _ctx.Store.Documents.Count.ShouldBe(3);
        _ctx.Store.Documents.ShouldAllBe(d => d.ObjetId == decision.DecisionId);
        _ctx.Store.Documents.Single(d => d.Exemplaire == "employeur").Zone.ShouldBe(ZoneDocument.Standard);
        _ctx.Store.Documents.Single(d => d.Exemplaire == "travailleur").Zone.ShouldBe(ZoneDocument.Medicale);
        _ctx.Store.Documents.Single(d => d.Exemplaire == "dossier").Zone.ShouldBe(ZoneDocument.Medicale);
        _ctx.Store.Documents.Single(d => d.Exemplaire == "employeur").DestinataireId.ShouldBe(Affilie);
        _ctx.Store.Documents.Single(d => d.Exemplaire == "travailleur").DestinataireId.ShouldBe(Personne);
        _ctx.Store.Documents.Single(d => d.Exemplaire == "dossier").TypeDestinataire.ShouldBe(TypeDestinataire.Dossier);
    }

    [Fact]
    public async Task Seuls_les_exemplaires_employeur_et_travailleur_sont_publies()
    {
        PublierModelesParDefaut();

        await Handler().HandleAsync(Decision(), _ct);

        var publies = _ctx.Store.Published.OfType<DocumentPublie>().ToList();
        publies.Count.ShouldBe(2);
        publies.Select(p => p.TypeDestinataire).Order().ShouldBe(["affilie", "personne"]);
        publies.Single(p => p.TypeDestinataire == "affilie").Zone.ShouldBe("standard");
        publies.Single(p => p.TypeDestinataire == "personne").Zone.ShouldBe("medicale");
        _ctx.Store.Documents.Single(d => d.Exemplaire == "dossier").Statut.ShouldBe(StatutDocument.Archive);
    }

    [Fact]
    public async Task L_exemplaire_employeur_ne_contient_que_la_decision()
    {
        PublierModelesParDefaut();
        var decision = Decision();

        await Handler().HandleAsync(decision, _ct);

        var employeur = _ctx.Store.Documents.Single(d => d.Exemplaire == "employeur");
        var contenu = FakeRendu.Texte(_ctx.Chiffrement.Dechiffrer(employeur.Zone, employeur.Id, _ctx.Stockage.Objets[employeur.StockageUri]));
        contenu.ShouldContain("Apte moyennant des mesures");
        contenu.ShouldContain(decision.DecisionId.ToString());
        contenu.ShouldContain("2027");

        // Aucun champ au-delà de la décision ne peut être fusionné dans le modèle de l'employeur (champs déclarés uniquement).
        var modele = _ctx.Store.Modeles.Single(m => m.Code == ModelesParDefaut.CodeEmployeur && m.Langue == Language.Fr);
        modele.Champs.Select(c => c.Nom).Order().ShouldBe(["categorie", "date_decision", "mesures", "reference_decision", "valide_jusqu_au"]);
        modele.Zone.ShouldBe(ZoneDocument.Standard);
    }

    [Fact]
    public async Task Le_rejeu_de_l_evenement_ne_cree_ni_document_ni_publication()
    {
        PublierModelesParDefaut();
        var decision = Decision();

        await Handler().HandleAsync(decision, _ct);
        await Handler().HandleAsync(decision, _ct);

        _ctx.Store.Documents.Count.ShouldBe(3);
        _ctx.Store.Published.OfType<DocumentPublie>().Count().ShouldBe(2);
        _ctx.Stockage.Objets.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Sans_modele_publie_l_evenement_echoue_pour_etre_rejoue_apres_publication()
    {
        // Les modèles de départ sont en brouillon : tant qu'ils ne sont pas validés, la génération est refusée.
        await new InitialiserModelesParDefaut(_ctx.Store, _ctx.Store).ExecuteAsync(_ct);

        await Should.ThrowAsync<InvalidOperationException>(() => Handler().HandleAsync(Decision(), _ct));

        _ctx.Store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_formulaire_suit_le_regime_linguistique_de_l_affilie()
    {
        PublierModelesParDefaut();
        _ctx.Source.Informations = new InformationsLinguistiques(RegimeLinguistique.Neerlandais, Language.Nl, Language.Fr);

        await Handler().HandleAsync(Decision(), _ct);

        _ctx.Store.Documents.ShouldAllBe(d => d.Langue == Language.Nl);
        var employeur = _ctx.Store.Documents.Single(d => d.Exemplaire == "employeur");
        FakeRendu.Texte(_ctx.Chiffrement.Dechiffrer(employeur.Zone, employeur.Id, _ctx.Stockage.Objets[employeur.StockageUri]))
            .ShouldContain("Geschikt mits maatregelen");
    }

    [Fact]
    public async Task L_initialisation_cree_les_neuf_modeles_de_depart_en_brouillon_une_seule_fois()
    {
        var init = new InitialiserModelesParDefaut(_ctx.Store, _ctx.Store);

        (await init.ExecuteAsync(_ct)).ShouldBe(9);
        (await init.ExecuteAsync(_ct)).ShouldBe(0);

        _ctx.Store.Modeles.ShouldAllBe(m => m.Statut == StatutModele.Brouillon);
        _ctx.Store.Modeles.Select(m => m.Langue).Distinct().Order().ShouldBe([Language.Fr, Language.Nl, Language.De]);
        _ctx.Store.Modeles.ShouldAllBe(m => m.Description!.Contains("valider", StringComparison.Ordinal));
    }

    [Fact]
    public void Les_modeles_de_depart_sont_valides_par_le_moteur_de_fusion()
    {
        foreach (var d in ModelesParDefaut.Definitions)
        {
            Should.NotThrow(() => Modele.Creer(d.Code, d.Langue, TypeModele.Formulaire, d.Zone, d.Libelle, null, d.Contenu, ModelesParDefaut.Champs));
        }
    }
}
