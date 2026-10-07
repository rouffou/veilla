using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.PostesRisques.Application.Listes;
using Sepp.PostesRisques.Application.Projections;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Risques;

using Shouldly;

namespace Sepp.PostesRisques.Application.Tests;

/// <summary>AFF-30, AFF-31 : listes nominatives versionnées, propositions de l'employeur ; projections idempotentes.</summary>
public class ListesEtProjectionsTests
{
    private static readonly DateOnly Reference = new(2026, 10, 15);

    private readonly InMemoryStore _store = new();
    private readonly FixedClock _clock = new(PropositionsTests.Maintenant);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly Guid _cariste = Guid.CreateVersion7();
    private readonly Guid _autre = Guid.CreateVersion7();
    private readonly FakeUser _gestionnaire = new("gestionnaire", Roles.GestionnaireDossiers);
    private readonly FakeUser _employeur = new("employeur", Roles.Employeur);
    private readonly FakeUser _cpmt = new("cpmt", Roles.Cpmt);

    private FakePerimetre PerimetreEmployeur => new(true, _affilie);

    /// <summary>Un poste de cariste exposé à la conduite (poste de sécurité) et un poste administratif exposé au travail de nuit.</summary>
    private (Poste Cariste, Poste Bureau) Catalogue()
    {
        var conduite = PropositionsTests.AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var nuit = PropositionsTests.AjouterRisque(_store, "EX.NUIT.TRAVAIL", CategorieRisque.TravailNuit);
        var cariste = Exposer(Poste.Creer(_affilie, "Cariste", null, null), conduite);
        var bureau = Exposer(Poste.Creer(_affilie, "Veilleur", null, null), nuit);
        return (cariste, bureau);
    }

    private Poste Exposer(Poste poste, Risque risque)
    {
        var proposition = PropositionPosteRisque.Soumettre(poste, OrigineProposition.Interne, "gestionnaire", PropositionsTests.Maintenant, "motif",
            PropositionsTests.Effet, [new DemandeLienRisque(TypeModification.Ajout, risque.Id, risque.Code, NiveauExposition.Eleve)]);
        proposition.Valider("cpmt", PropositionsTests.Maintenant, new DateOnly(2026, 9, 1), Guid.CreateVersion7());
        poste.AppliquerProposition(proposition);
        _store.Postes.Add(poste);
        return poste;
    }

    private Task Affecter(Guid personne, Guid poste, DateOnly debut, DateOnly? fin = null, Guid? affectation = null) =>
        new AffectationModifieeHandler(_store, _store)
            .HandleAsync(new AffectationModifiee(affectation ?? Guid.CreateVersion7(), personne, poste, debut, fin), _ct);

    private Task Examen(Guid personne, DateOnly date, Guid? examen = null, Guid? affilie = null) =>
        new ExamenClotureHandler(_store, _store)
            .HandleAsync(new ExamenCloture(examen ?? Guid.CreateVersion7(), personne, affilie ?? _affilie, "EVALUATION_PERIODIQUE", date), _ct);

    private GenererListeNominativeHandler Generateur(ICurrentUser user, IPerimetreAffilies perimetre) =>
        new(new CalculListesNominatives(_store, _store, _store), _store, new PolitiqueConservationListes(_store, new ConservationListesOptions()),
            _store, _store, user, perimetre, _clock);

    private ValiderPropositionListeHandler Validateur(ICurrentUser user) =>
        new(_store, _store, new CalculListesNominatives(_store, _store, _store), new PolitiqueConservationListes(_store, new ConservationListesOptions()),
            _store, _store, user, _clock);

    [Fact]
    public async Task La_liste_des_postes_de_securite_reprend_les_travailleurs_affectes_et_leur_derniere_evaluation()
    {
        var (cariste, bureau) = Catalogue();
        await Affecter(_cariste, cariste.Id, new DateOnly(2025, 1, 1));
        await Affecter(_autre, bureau.Id, new DateOnly(2025, 1, 1));
        await Affecter(Guid.CreateVersion7(), cariste.Id, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 30));
        await Examen(_cariste, new DateOnly(2025, 11, 3));
        await Examen(_cariste, new DateOnly(2026, 5, 12));
        await Examen(_cariste, new DateOnly(2026, 12, 1));
        await Examen(_cariste, new DateOnly(2026, 8, 1), affilie: Guid.CreateVersion7());

        var result = await Generateur(_gestionnaire, FakePerimetre.Interne)
            .HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.PosteSecurite, Reference), _ct);

        result.Value.Version.ShouldBe(1);
        var liste = _store.Listes.ShouldHaveSingleItem();
        var ligne = liste.Lignes.ShouldHaveSingleItem();
        ligne.PersonneId.ShouldBe(_cariste);
        ligne.PosteId.ShouldBe(cariste.Id);
        ligne.CodesRisques.ShouldBe(["EX.SECU.CONDUITE"]);
        ligne.DateDerniereEvaluation.ShouldBe(new DateOnly(2026, 5, 12));
        liste.ConserverJusquAu.ShouldBe(new DateOnly(2031, 9, 28));
        liste.GenereePar.ShouldBe("gestionnaire");

        // AFF-32 : le service Obligations est informé de la nouvelle version (alerte « liste non revue »).
        var publie = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<Sepp.Contracts.PostesRisques.ListeNominativeGeneree>();
        publie.ListeNominativeId.ShouldBe(liste.Id);
        publie.AffilieId.ShouldBe(_affilie);
        publie.TypeListe.ShouldBe("PosteSecurite");
        publie.Version.ShouldBe(1);
        publie.DateReference.ShouldBe(Reference);
    }

    [Fact]
    public async Task Chaque_generation_cree_une_nouvelle_version_sans_reecrire_l_historique()
    {
        var (_, bureau) = Catalogue();
        await Affecter(_autre, bureau.Id, new DateOnly(2025, 1, 1));
        var generateur = Generateur(_employeur, PerimetreEmployeur);

        await generateur.HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.TravailNuit, Reference), _ct);
        var seconde = await generateur.HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.TravailNuit, Reference), _ct);

        seconde.Value.Version.ShouldBe(2);
        var historique = await new ListerListesNominativesHandler(_store, _employeur, PerimetreEmployeur)
            .HandleAsync(new ListerListesNominatives(_affilie, TypeListeNominative.TravailNuit), _ct);
        historique.Value.Select(l => l.Version).ShouldBe([2, 1]);
    }

    [Fact]
    public async Task La_duree_de_conservation_suit_le_parametre_legal_recu()
    {
        Catalogue();
        await new ParametreLegalModifieHandler(_store, _store).HandleAsync(
            new ParametreLegalModifie(PolitiqueConservationListes.CodeParametre, 7, "Annees", new DateOnly(2026, 1, 1), null), _ct);
        await new ParametreLegalModifieHandler(_store, _store).HandleAsync(
            new ParametreLegalModifie("SANTE.REPRISE.DELAI", 12, "JoursOuvrables", new DateOnly(2026, 1, 1), null), _ct);

        await Generateur(_gestionnaire, FakePerimetre.Interne).HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.PosteSecurite, Reference), _ct);

        _store.Parametres.ShouldHaveSingleItem();
        _store.Listes.Single().ConserverJusquAu.ShouldBe(new DateOnly(2033, 9, 28));
    }

    [Fact]
    public async Task Un_employeur_ne_genere_pas_la_liste_d_un_autre_affilie()
    {
        Catalogue();

        var result = await Generateur(_employeur, new FakePerimetre(true, Guid.CreateVersion7()))
            .HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.PosteSecurite, Reference), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _store.Listes.ShouldBeEmpty();
    }

    [Fact]
    public async Task L_employeur_propose_une_modification_de_liste_que_le_cpmt_valide()
    {
        var (cariste, _) = Catalogue();
        await Affecter(_cariste, cariste.Id, new DateOnly(2025, 1, 1));
        await Examen(_autre, new DateOnly(2026, 2, 2));
        var liste = await Generateur(_employeur, PerimetreEmployeur)
            .HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.PosteSecurite, Reference), _ct);

        var proposition = await new SoumettrePropositionListeHandler(_store, _store, _store, _store, _employeur, PerimetreEmployeur, _clock)
            .HandleAsync(new SoumettrePropositionListe(liste.Value.Id, "Remplacement du cariste",
                [new DemandeLigneListe(TypeModification.Retrait, _cariste, cariste.Id), new DemandeLigneListe(TypeModification.Ajout, _autre, cariste.Id)]), _ct);
        proposition.IsSuccess.ShouldBeTrue(proposition.Error?.Message);
        _store.PropositionsListe.Single().Origine.ShouldBe(OrigineProposition.PortailEmployeur);

        (await Validateur(_employeur).HandleAsync(new ValiderPropositionListe(proposition.Value), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Validateur(_gestionnaire).HandleAsync(new ValiderPropositionListe(proposition.Value), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        var nouvelle = await Validateur(_cpmt).HandleAsync(new ValiderPropositionListe(proposition.Value), _ct);

        nouvelle.Value.Version.ShouldBe(2);
        var v2 = _store.Listes.Single(l => l.Version == 2);
        var ligne = v2.Lignes.ShouldHaveSingleItem();
        ligne.PersonneId.ShouldBe(_autre);
        ligne.Origine.ShouldBe(OrigineLigne.AjustementValide);
        ligne.DateDerniereEvaluation.ShouldBe(new DateOnly(2026, 2, 2));
        v2.PropositionId.ShouldBe(proposition.Value);
        _store.Listes.Single(l => l.Version == 1).Lignes.ShouldHaveSingleItem().PersonneId.ShouldBe(_cariste);
        _store.PropositionsListe.Single().Statut.ShouldBe(StatutProposition.Validee);
    }

    [Fact]
    public async Task Ajouter_un_travailleur_sur_un_poste_non_expose_est_refuse_a_la_validation()
    {
        var (cariste, bureau) = Catalogue();
        await Affecter(_cariste, cariste.Id, new DateOnly(2025, 1, 1));
        var liste = await Generateur(_employeur, PerimetreEmployeur)
            .HandleAsync(new GenererListeNominative(_affilie, TypeListeNominative.PosteSecurite, Reference), _ct);
        var proposition = await new SoumettrePropositionListeHandler(_store, _store, _store, _store, _employeur, PerimetreEmployeur, _clock)
            .HandleAsync(new SoumettrePropositionListe(liste.Value.Id, "Ajout", [new DemandeLigneListe(TypeModification.Ajout, _autre, bureau.Id)]), _ct);

        var result = await Validateur(_cpmt).HandleAsync(new ValiderPropositionListe(proposition.Value), _ct);

        result.Error!.Code.ShouldBe("proposition.poste-non-expose");
        _store.Listes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Rejouer_un_evenement_d_affectation_ne_duplique_rien()
    {
        var affectation = Guid.CreateVersion7();
        var poste = Guid.CreateVersion7();
        var evt = new AffectationModifiee(affectation, _cariste, poste, new DateOnly(2026, 1, 1), null);
        var handler = new AffectationModifieeHandler(_store, _store);

        await handler.HandleAsync(evt, _ct);
        await handler.HandleAsync(evt, _ct);

        _store.Affectations.ShouldHaveSingleItem().PosteId.ShouldBe(poste);
    }

    [Fact]
    public async Task Un_evenement_d_affectation_plus_ancien_est_ignore()
    {
        var affectation = Guid.CreateVersion7();
        var poste = Guid.CreateVersion7();
        var handler = new AffectationModifieeHandler(_store, _store);
        var recent = new AffectationModifiee(affectation, _cariste, poste, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30))
        {
            OccurredAt = PropositionsTests.Maintenant,
        };
        var ancien = recent with { DateFin = null, OccurredAt = PropositionsTests.Maintenant.AddMinutes(-5) };

        await handler.HandleAsync(recent, _ct);
        await handler.HandleAsync(ancien, _ct);

        _store.Affectations.ShouldHaveSingleItem().DateFin.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public async Task Rejouer_un_examen_cloture_ne_duplique_rien()
    {
        var examen = Guid.CreateVersion7();

        await Examen(_cariste, new DateOnly(2026, 5, 12), examen);
        await Examen(_cariste, new DateOnly(2026, 5, 12), examen);

        _store.Examens.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 5, 12));
    }

    [Fact]
    public async Task Rejouer_un_parametre_legal_ne_duplique_rien()
    {
        var evt = new ParametreLegalModifie(PolitiqueConservationListes.CodeParametre, 6, "Annees", new DateOnly(2027, 1, 1), null);
        var handler = new ParametreLegalModifieHandler(_store, _store);

        await handler.HandleAsync(evt, _ct);
        await handler.HandleAsync(evt, _ct);

        _store.Parametres.ShouldHaveSingleItem().Valeur.ShouldBe(6);
        (await new PolitiqueConservationListes(_store, new ConservationListesOptions()).AnneesAsync(new DateOnly(2026, 12, 31), _ct)).ShouldBe(5);
        (await new PolitiqueConservationListes(_store, new ConservationListesOptions()).AnneesAsync(new DateOnly(2027, 1, 1), _ct)).ShouldBe(6);
    }
}
