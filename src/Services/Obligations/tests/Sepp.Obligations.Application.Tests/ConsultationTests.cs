using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Application.Consultation;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

using Shouldly;

namespace Sepp.Obligations.Application.Tests;

/// <summary>Consultation : obligations par travailleur et par affilié, trace, regroupables, alertes (SAN-01 à SAN-04, AFF-32, POR-02).</summary>
public class ConsultationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Banc _banc = new();

    private InMemoryStore Store => _banc.Store;

    private Obligation Ajouter(
        string cle, DateOnly due, TypeObligation type = TypeObligation.EvaluationPeriodique, Guid? personne = null, Guid? affilie = null, DateOnly? limite = null)
    {
        var echeance = new EcheanceCalculee(
            cle,
            affilie ?? _banc.Affilie,
            type,
            OrigineObligation.Regle,
            ["R1"],
            due,
            limite ?? due,
            null,
            new Justification("regle-surveillance:R1", 1, $"Calcul {cle}", [new KeyValuePair<string, string>("frequence_mois", "12")]));
        var obligation = Obligation.Creer(personne ?? _banc.Personne, echeance, Banc.Midi);
        Store.Obligations.Add(obligation);
        return obligation;
    }

    private ListerObligationsPersonneHandler PersonneHandler(Sepp.BuildingBlocks.Application.Security.ICurrentUser user, IPerimetreAffilies? perimetre = null) =>
        new(Store, perimetre ?? FakePerimetre.Interne, user, _banc.Clock, _banc.Options);

    private ListerObligationsAffilieHandler AffilieHandler(Sepp.BuildingBlocks.Application.Security.ICurrentUser user, IPerimetreAffilies? perimetre = null) =>
        new(Store, perimetre ?? FakePerimetre.Interne, user, _banc.Clock, _banc.Options);

    [Fact]
    public async Task Le_cpmt_voit_les_obligations_d_un_travailleur_triees_par_echeance()
    {
        Ajouter("B", new DateOnly(2026, 9, 1));
        Ajouter("A", new DateOnly(2026, 7, 1));

        var resultat = await PersonneHandler(Banc.Cpmt).HandleAsync(new ListerObligationsPersonne(_banc.Personne, false, null, Language.Fr), Ct);

        resultat.Value.Select(o => o.DateDue).ShouldBe([new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 1)]);
        resultat.Value[0].Type.ShouldBe("EVALUATION_PERIODIQUE");
        resultat.Value[0].TypeLibelle.ShouldBe("Évaluation de santé périodique");
        resultat.Value[0].StatutsSuivants.ShouldContain("Planifie");
    }

    [Fact]
    public async Task Le_libelle_du_type_suit_la_langue_demandee()
    {
        Ajouter("A", new DateOnly(2026, 7, 1));

        var resultat = await PersonneHandler(Banc.Cpmt).HandleAsync(new ListerObligationsPersonne(_banc.Personne, false, null, Language.Nl), Ct);

        resultat.Value.ShouldHaveSingleItem().TypeLibelle.ShouldBe("Periodieke gezondheidsbeoordeling");
    }

    [Fact]
    public async Task Un_travailleur_n_a_pas_acces_aux_obligations()
    {
        var resultat = await PersonneHandler(Banc.Travailleur).HandleAsync(new ListerObligationsPersonne(_banc.Personne, false, null, Language.Fr), Ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Les_filtres_statut_et_ouvertes_seulement_s_appliquent()
    {
        Ajouter("A", new DateOnly(2026, 7, 1)).Realiser(new Realisation(new DateOnly(2026, 6, 20), Guid.CreateVersion7()));
        Ajouter("B", new DateOnly(2026, 9, 1));

        var ouvertes = await PersonneHandler(Banc.Cpmt).HandleAsync(new ListerObligationsPersonne(_banc.Personne, true, null, Language.Fr), Ct);
        var realisees = await PersonneHandler(Banc.Cpmt).HandleAsync(new ListerObligationsPersonne(_banc.Personne, false, StatutObligation.Realise, Language.Fr), Ct);

        ouvertes.Value.ShouldHaveSingleItem().Statut.ShouldBe("APlanifier");
        realisees.Value.ShouldHaveSingleItem().Statut.ShouldBe("Realise");
    }

    [Fact]
    public async Task Un_employeur_ne_voit_que_les_obligations_de_son_affilie_et_pas_les_types_confidentiels()
    {
        Ajouter("A", new DateOnly(2026, 7, 1));
        Ajouter("M", new DateOnly(2026, 6, 1), TypeObligation.ProtectionMaternite);
        Ajouter("C", new DateOnly(2026, 6, 10), TypeObligation.ConsultationSpontanee);
        Ajouter("AUTRE", new DateOnly(2026, 7, 1), affilie: Guid.CreateVersion7());
        var employeur = new FakePerimetre(true, _banc.Affilie);

        var resultat = await PersonneHandler(Banc.Employeur, employeur).HandleAsync(new ListerObligationsPersonne(_banc.Personne, false, null, Language.Fr), Ct);

        resultat.Value.ShouldHaveSingleItem().Type.ShouldBe("EVALUATION_PERIODIQUE");
        var interne = await PersonneHandler(Banc.Cpmt).HandleAsync(new ListerObligationsPersonne(_banc.Personne, false, null, Language.Fr), Ct);
        interne.Value.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Un_employeur_ne_peut_pas_consulter_un_autre_affilie()
    {
        var resultat = await AffilieHandler(Banc.Employeur, new FakePerimetre(true, _banc.Affilie))
            .HandleAsync(new ListerObligationsAffilie(Guid.CreateVersion7(), null, null, false, Language.Fr), Ct);

        resultat.Error!.Code.ShouldBe("perimetre.interdit");
    }

    [Fact]
    public async Task Les_obligations_d_un_affilie_se_filtrent_par_categorie_du_tableau_de_bord()
    {
        var autre = Guid.CreateVersion7();
        Ajouter("RETARD", new DateOnly(2026, 6, 1));
        Ajouter("DUE", new DateOnly(2026, 7, 10), personne: autre);
        Ajouter("AVENIR", new DateOnly(2027, 6, 1), personne: autre);
        var planifiee = Ajouter("PLAN", new DateOnly(2026, 8, 1), personne: autre);
        planifiee.Planifier(Guid.CreateVersion7(), new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero));
        Ajouter("REALISEE", new DateOnly(2026, 6, 1), personne: autre).Realiser(new Realisation(new DateOnly(2026, 6, 2), Guid.CreateVersion7()));
        var employeur = new FakePerimetre(true, _banc.Affilie);

        async Task<IEnumerable<string>> Cles(CategorieObligation? categorie) =>
            (await AffilieHandler(Banc.Employeur, employeur).HandleAsync(new ListerObligationsAffilie(_banc.Affilie, categorie, null, false, Language.Fr), Ct)).Value
                .Select(o => Store.Obligations.Single(x => x.Id == o.Id).Cle);

        (await Cles(CategorieObligation.EnRetard)).ShouldBe(["RETARD"]);
        (await Cles(CategorieObligation.Due)).ShouldBe(["DUE"]);
        (await Cles(CategorieObligation.Planifiee)).ShouldBe(["PLAN"]);
        (await Cles(CategorieObligation.AVenir)).ShouldBe(["AVENIR"]);
        (await Cles(null)).Count().ShouldBe(5);
    }

    [Fact]
    public async Task Le_filtre_par_type_s_applique()
    {
        Ajouter("A", new DateOnly(2026, 7, 1));
        Ajouter("R", new DateOnly(2026, 6, 15), TypeObligation.ExamenReprise);

        var resultat = await AffilieHandler(Banc.Cpmt).HandleAsync(
            new ListerObligationsAffilie(_banc.Affilie, null, TypeObligation.ExamenReprise, false, Language.Fr), Ct);

        resultat.Value.ShouldHaveSingleItem().Type.ShouldBe("EXAMEN_REPRISE");
    }

    [Fact]
    public async Task La_synthese_compte_les_obligations_ouvertes_par_categorie_et_par_type()
    {
        Ajouter("RETARD", new DateOnly(2026, 6, 1));
        Ajouter("RETARD2", new DateOnly(2026, 5, 1), personne: Guid.CreateVersion7());
        Ajouter("DUE", new DateOnly(2026, 7, 10), TypeObligation.EvaluationPrealable);
        Ajouter("AVENIR", new DateOnly(2027, 6, 1));
        Ajouter("MATERNITE", new DateOnly(2026, 7, 1), TypeObligation.ProtectionMaternite);
        var handler = new SynthetiserObligationsAffilieHandler(Store, new FakePerimetre(true, _banc.Affilie), Banc.Employeur, _banc.Clock, _banc.Options);

        var synthese = (await handler.HandleAsync(new SynthetiserObligationsAffilie(_banc.Affilie), Ct)).Value;

        synthese.EnRetard.ShouldBe(2);
        synthese.Dues.ShouldBe(1);
        synthese.AVenir.ShouldBe(1);
        synthese.Planifiees.ShouldBe(0);
        synthese.Date.ShouldBe(new DateOnly(2026, 6, 15));

        // La protection de la maternité est confidentielle : elle n'apparaît pas pour un profil externe.
        synthese.ParType.ShouldNotContainKey("PROTECTION_MATERNITE");
        synthese.ParType["EVALUATION_PERIODIQUE"].ShouldBe(3);
    }

    [Fact]
    public async Task La_trace_de_calcul_explique_l_obligation_du_plus_recent_au_plus_ancien()
    {
        var obligation = Ajouter("A", new DateOnly(2026, 7, 1));
        obligation.Actualiser(
            new EcheanceCalculee(
                "A", _banc.Affilie, TypeObligation.EvaluationPeriodique, OrigineObligation.Surcharge, ["R1"], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), null,
                new Justification("regle-surveillance:R1", 2, "Fréquence surchargée par le CPMT.", [new KeyValuePair<string, string>("frequence_mois", "6")])),
            Banc.Midi.AddDays(1));
        var handler = new ObtenirTraceCalculHandler(Store, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock, _banc.Options);

        var trace = (await handler.HandleAsync(new ObtenirTraceCalcul(obligation.Id, Language.Fr), Ct)).Value;

        trace.Traces.Count.ShouldBe(2);
        trace.Traces[0].Explication.ShouldBe("Fréquence surchargée par le CPMT.");
        trace.Traces[0].RegleVersion.ShouldBe(2);
        trace.Traces[0].Entrees.ShouldContain(e => e.Cle == "frequence_mois" && e.Valeur == "6");
        trace.Traces[1].Explication.ShouldBe("Calcul A");
        trace.Obligation.Origine.ShouldBe("Surcharge");
    }

    [Fact]
    public async Task La_trace_d_une_obligation_confidentielle_ou_hors_perimetre_est_introuvable_pour_un_externe()
    {
        var maternite = Ajouter("M", new DateOnly(2026, 6, 1), TypeObligation.ProtectionMaternite);
        var autre = Ajouter("X", new DateOnly(2026, 6, 1), affilie: Guid.CreateVersion7());
        var handler = new ObtenirTraceCalculHandler(Store, new FakePerimetre(true, _banc.Affilie), Banc.Employeur, _banc.Clock, _banc.Options);

        (await handler.HandleAsync(new ObtenirTraceCalcul(maternite.Id, Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await handler.HandleAsync(new ObtenirTraceCalcul(autre.Id, Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await handler.HandleAsync(new ObtenirTraceCalcul(Guid.CreateVersion7(), Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Une_obligation_se_consulte_par_son_identifiant()
    {
        var obligation = Ajouter("A", new DateOnly(2026, 7, 1));
        var handler = new ObtenirObligationHandler(Store, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock, _banc.Options);

        (await handler.HandleAsync(new ObtenirObligation(obligation.Id, Language.Fr), Ct)).Value.Id.ShouldBe(obligation.Id);
        (await handler.HandleAsync(new ObtenirObligation(Guid.CreateVersion7(), Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Les_regroupables_proposent_un_seul_rendez_vous_par_travailleur_dans_la_fenetre()
    {
        Ajouter("A", new DateOnly(2026, 7, 1));
        Ajouter("B", new DateOnly(2026, 7, 20));
        Ajouter("C", new DateOnly(2026, 6, 25), personne: Guid.CreateVersion7());
        var handler = new ListerRegroupablesHandler(Store, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock, _banc.Options);

        var defaut = (await handler.HandleAsync(new ListerRegroupables(_banc.Affilie, null, Language.Fr), Ct)).Value;
        var etroite = (await handler.HandleAsync(new ListerRegroupables(_banc.Affilie, 10, Language.Fr), Ct)).Value;

        defaut.Count.ShouldBe(2);
        defaut.Single(p => p.EstRegroupement).Obligations.Count.ShouldBe(2);
        etroite.Single(p => p.PersonneId == _banc.Personne).Obligations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Une_fenetre_de_regroupement_hors_bornes_est_refusee()
    {
        var handler = new ListerRegroupablesHandler(Store, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock, _banc.Options);

        (await handler.HandleAsync(new ListerRegroupables(_banc.Affilie, -1, Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new ListerRegroupables(_banc.Affilie, 400, Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Les_alertes_de_non_couverture_reunissent_les_trois_types_d_alerte()
    {
        // Travailleur exposé dont l'échéance approche sans rendez-vous.
        Ajouter("A", new DateOnly(2026, 6, 30));

        // Poste occupé sans profil de risques.
        var posteSansProfil = Guid.CreateVersion7();
        var occupant = Guid.CreateVersion7();
        var occupation = new OccupationLocale(Guid.CreateVersion7(), occupant, _banc.Affilie);
        occupation.Debuter(new DateOnly(2025, 1, 1));
        Store.Occupations.Add(occupation);
        Store.Affectations.Add(new AffectationLocale(Guid.CreateVersion7(), occupant, posteSansProfil, new DateOnly(2025, 1, 1), null, Banc.Midi));

        // Liste nominative non revue depuis plus de 12 mois.
        Store.Listes.Add(new ListeNominativeLocale(Guid.CreateVersion7(), _banc.Affilie, "EXPOSES", 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 5)));
        var handler = new ListerAlertesHandler(Store, Store, _banc.Recalcul, new FakePerimetre(true, _banc.Affilie), Banc.Employeur, _banc.Clock, _banc.Options);

        var alertes = (await handler.HandleAsync(new ListerAlertes(_banc.Affilie), Ct)).Value;

        alertes.Select(a => a.Type).Order().ShouldBe(["ListeNominativeNonRevue", "PosteSansAnalyseRisques", "TravailleurExposeSansSurveillancePlanifiee"]);
        alertes.Single(a => a.Type == "PosteSansAnalyseRisques").PosteId.ShouldBe(posteSansProfil);
    }

    [Fact]
    public async Task Le_delai_de_revue_des_listes_vient_du_parametre_legal_recu()
    {
        Store.Listes.Add(new ListeNominativeLocale(Guid.CreateVersion7(), _banc.Affilie, "EXPOSES", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5)));
        var handler = new ListerAlertesHandler(Store, Store, _banc.Recalcul, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock, _banc.Options);

        (await handler.HandleAsync(new ListerAlertes(_banc.Affilie), Ct)).Value.ShouldBeEmpty();

        Store.Parametres.Add(new ParametreLegalLocal(CodesParametres.RevueListesNominatives, new DateOnly(2026, 1, 1), null, 3, "Mois", Banc.Midi));
        (await handler.HandleAsync(new ListerAlertes(_banc.Affilie), Ct)).Value.ShouldHaveSingleItem().Type.ShouldBe("ListeNominativeNonRevue");
    }

    [Fact]
    public async Task Les_alertes_d_un_autre_affilie_sont_interdites_a_un_externe()
    {
        var handler = new ListerAlertesHandler(Store, Store, _banc.Recalcul, new FakePerimetre(true, _banc.Affilie), Banc.Employeur, _banc.Clock, _banc.Options);

        (await handler.HandleAsync(new ListerAlertes(Guid.CreateVersion7()), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
}
