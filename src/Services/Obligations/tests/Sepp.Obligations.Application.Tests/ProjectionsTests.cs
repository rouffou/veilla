using Sepp.Contracts;
using Sepp.Contracts.BffEmployeur;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Planification;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.Reintegration;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Application.Projections;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

using Shouldly;

namespace Sepp.Obligations.Application.Tests;

/// <summary>
/// SAN-01, SAN-04, ARC-31 : projections locales alimentées par événements, recalcul immédiat, idempotence et
/// indépendance vis-à-vis de l'ordre de réception.
/// </summary>
public class ProjectionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Banc _banc = new();

    private InMemoryStore Store => _banc.Store;

    private static DateTimeOffset T(int minutes) => new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    private Task Affectation(Guid? id = null, DateOnly? debut = null, DateOnly? fin = null, int minute = 0) =>
        new AffectationModifieeHandler(Store, _banc.MiseAJour).HandleAsync(
            new AffectationModifiee(id ?? _affectationId, _banc.Personne, _banc.Poste, debut ?? new DateOnly(2026, 2, 1), fin) { OccurredAt = T(minute) }, Ct);

    private readonly Guid _affectationId = Guid.CreateVersion7();

    private Task Profil(int minute = 0, params string[] codes) =>
        new ProfilRisquePosteModifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new ProfilRisquePosteModifie(_banc.Poste, _banc.Affilie, codes.Length == 0 ? ["R1"] : codes, new DateOnly(2026, 1, 1)) { OccurredAt = T(minute) }, Ct);

    private Task Regle(int version = 1, int? frequence = 12, string type = "EvaluationSantePeriodique", string code = "R1") =>
        new RegleSurveillanceModifieeHandler(Store, _banc.MiseAJour).HandleAsync(
            new RegleSurveillanceModifiee(Guid.CreateVersion7(), code, "Physique", version, type, frequence, false, new DateOnly(2026, 1, 1)), Ct);

    private Task Occupation(Guid? id = null) =>
        new OccupationDebuteeHandler(Store, _banc.MiseAJour).HandleAsync(
            new OccupationDebutee(id ?? _occupationId, _banc.Personne, _banc.Affilie, new DateOnly(2025, 1, 1)), Ct);

    private readonly Guid _occupationId = Guid.CreateVersion7();

    private Task OccupationFinie(DateOnly fin, int minute = 0) =>
        new OccupationTermineeHandler(Store, _banc.MiseAJour).HandleAsync(
            new OccupationTerminee(_occupationId, _banc.Personne, _banc.Affilie, fin) { OccurredAt = T(minute) }, Ct);

    private Task Examen(Guid id, string type, DateOnly date) =>
        new ExamenClotureHandler(Store, _banc.MiseAJour).HandleAsync(new ExamenCloture(id, _banc.Personne, _banc.Affilie, type, date), Ct);

    private async Task Contexte()
    {
        await Affectation();
        await Profil();
        await Regle();
    }

    private string Resume() =>
        string.Join(
            "\n",
            Store.Obligations
                .Where(o => o.Statut != StatutObligation.SortiEntreprise && !(o.Statut == StatutObligation.Annule && o.MotifAnnulation == MotifAnnulation.Recalcul))
                .OrderBy(o => o.Cle, StringComparer.Ordinal)
                .Select(o => $"{o.Cle}|{o.Type}|{o.Origine}|{string.Join(',', o.CodesRisques)}|{o.DateDue}|{o.DateLimite}|{o.Statut}|{o.DateRealisation}|{o.ExamenId}"))
            .Replace(_banc.Affilie.ToString("N"), "AFFILIE", StringComparison.Ordinal);

    [Fact]
    public async Task Les_projections_d_une_nouvelle_exposition_creent_l_evaluation_prealable_et_publient_ObligationCreee()
    {
        await Contexte();

        var obligation = Store.Obligations.ShouldHaveSingleItem();
        obligation.Type.ShouldBe(TypeObligation.EvaluationPrealable);
        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        var evenement = Store.Publies<ObligationCreee>().ShouldHaveSingleItem();
        evenement.ObligationId.ShouldBe(obligation.Id);
        evenement.PersonneId.ShouldBe(_banc.Personne);
        evenement.AffilieId.ShouldBe(_banc.Affilie);
        evenement.TypeExamen.ShouldBe("EVALUATION_PREALABLE");
        evenement.DateDue.ShouldBe(new DateOnly(2026, 2, 1));
        evenement.DateLimite.ShouldBe(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task Un_evenement_rejoue_est_sans_effet()
    {
        await Contexte();
        var avant = Resume();
        var publies = Store.Published.Count;
        var traces = Store.Obligations.Single().Traces.Count;

        await Contexte();

        Resume().ShouldBe(avant);
        Store.Published.Count.ShouldBe(publies);
        Store.Obligations.Single().Traces.Count.ShouldBe(traces);
        Store.Affectations.Count.ShouldBe(1);
        Store.Regles.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Un_evenement_plus_ancien_que_l_etat_connu_est_ignore()
    {
        await Affectation(fin: new DateOnly(2026, 4, 1), minute: 10);
        await Profil();
        await Regle();

        await Affectation(fin: null, minute: 5);

        Store.Affectations.Single().DateFin.ShouldBe(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public async Task Les_evenements_recus_dans_n_importe_quel_ordre_donnent_le_meme_resultat()
    {
        var examen1 = Guid.CreateVersion7();
        var examen2 = Guid.CreateVersion7();
        var evenements = new List<Func<ProjectionsTests, Task>>
        {
            t => t.Affectation(),
            t => t.Profil(),
            t => t.Regle(),
            t => t.Occupation(),
            t => t.Examen(examen1, "EVALUATION_PREALABLE", new DateOnly(2026, 1, 20)),
            t => t.Examen(examen2, "EVALUATION_PERIODIQUE", new DateOnly(2027, 1, 10)),
        };

        var attendu = await ResultatPour(evenements, [0, 1, 2, 3, 4, 5]);

        attendu.ShouldContain("EvaluationPeriodique", Case.Sensitive, "le cycle périodique doit exister");
        var ordres = Permutations(Enumerable.Range(0, evenements.Count).ToArray()).Take(720).ToList();
        ordres.Count.ShouldBe(720);
        foreach (var ordre in ordres.Where((_, i) => i % 7 == 0))
        {
            (await ResultatPour(evenements, ordre)).ShouldBe(attendu, string.Join(',', ordre));
        }
    }

    private static async Task<string> ResultatPour(List<Func<ProjectionsTests, Task>> evenements, int[] ordre)
    {
        var scenario = new ProjectionsTests();
        foreach (var i in ordre)
        {
            await evenements[i](scenario);
            // Chaque événement est aussi livré une seconde fois (livraison « au moins une fois »).
            await evenements[i](scenario);
        }

        return scenario.Resume();
    }

    private static IEnumerable<int[]> Permutations(int[] elements)
    {
        if (elements.Length <= 1)
        {
            yield return elements;
            yield break;
        }

        for (var i = 0; i < elements.Length; i++)
        {
            var reste = elements.Where((_, j) => j != i).ToArray();
            foreach (var suite in Permutations(reste))
            {
                yield return [elements[i], .. suite];
            }
        }
    }

    [Fact]
    public async Task L_examen_cloture_realise_l_obligation_et_ouvre_le_cycle_suivant()
    {
        await Contexte();
        var examen = Guid.CreateVersion7();

        await Examen(examen, "EVALUATION_PREALABLE", new DateOnly(2026, 2, 3));

        var prealable = Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPrealable);
        prealable.Statut.ShouldBe(StatutObligation.Realise);
        prealable.ExamenId.ShouldBe(examen);
        var periodique = Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPeriodique);
        periodique.DateDue.ShouldBe(new DateOnly(2027, 2, 3));
        Store.Publies<ObligationCreee>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Une_nouvelle_version_de_la_regle_recalcule_les_travailleurs_exposes()
    {
        await Contexte();
        await Examen(Guid.CreateVersion7(), "EVALUATION_PREALABLE", new DateOnly(2026, 2, 3));
        Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPeriodique).DateDue.ShouldBe(new DateOnly(2027, 2, 3));

        // Nouvelle version applicable dès le 1er janvier 2026 : fréquence ramenée à 6 mois.
        await new RegleSurveillanceModifieeHandler(Store, _banc.MiseAJour).HandleAsync(
            new RegleSurveillanceModifiee(Guid.CreateVersion7(), "R1", "Physique", 2, "EvaluationSantePeriodique", 6, false, new DateOnly(2026, 6, 1)), Ct);

        var periodique = Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPeriodique);
        periodique.DateDue.ShouldBe(new DateOnly(2026, 8, 3));
        periodique.Traces.Count.ShouldBe(2);
        periodique.Traces.Select(t => t.RegleVersion).ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public async Task La_fin_d_occupation_fait_passer_les_obligations_ouvertes_a_sorti_de_l_entreprise()
    {
        await Contexte();
        await Occupation();

        await OccupationFinie(new DateOnly(2026, 5, 31));

        Store.Obligations.Single().Statut.ShouldBe(StatutObligation.SortiEntreprise);
        Store.Publies<ObligationCreee>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task La_fin_d_occupation_recue_avant_le_debut_donne_le_meme_etat()
    {
        await Contexte();

        await OccupationFinie(new DateOnly(2026, 5, 31));
        await Occupation();

        Store.Occupations.ShouldHaveSingleItem().DateDebut.ShouldBe(new DateOnly(2025, 1, 1));
        Store.Obligations.Single().Statut.ShouldBe(StatutObligation.SortiEntreprise);
    }

    [Fact]
    public async Task Une_surcharge_du_cpmt_pour_le_travailleur_recalcule_ses_echeances_et_la_trace_la_justifie()
    {
        await Contexte();
        await Examen(Guid.CreateVersion7(), "EVALUATION_PREALABLE", new DateOnly(2026, 2, 3));
        var surcharge = Guid.CreateVersion7();

        await new SurchargeFrequenceDefinieHandler(Store, _banc.MiseAJour).HandleAsync(
            new SurchargeFrequenceDefinie(surcharge, _banc.Affilie, "Personne", _banc.Personne, "R1", 6, new DateOnly(2026, 1, 1), null), Ct);

        var periodique = Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPeriodique);
        periodique.DateDue.ShouldBe(new DateOnly(2026, 8, 3));
        periodique.Origine.ShouldBe(OrigineObligation.Surcharge);

        // Clôture de la surcharge : retour à la règle du risque.
        await new SurchargeFrequenceDefinieHandler(Store, _banc.MiseAJour).HandleAsync(
            new SurchargeFrequenceDefinie(surcharge, _banc.Affilie, "Personne", _banc.Personne, "R1", 6, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1))
            { OccurredAt = DateTimeOffset.UtcNow.AddMinutes(1) }, Ct);
        Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPeriodique).DateDue.ShouldBe(new DateOnly(2027, 2, 3));
    }

    [Fact]
    public async Task Une_surcharge_de_poste_recalcule_les_travailleurs_affectes_a_ce_poste()
    {
        await Contexte();
        await Examen(Guid.CreateVersion7(), "EVALUATION_PREALABLE", new DateOnly(2026, 2, 3));

        await new SurchargeFrequenceDefinieHandler(Store, _banc.MiseAJour).HandleAsync(
            new SurchargeFrequenceDefinie(Guid.CreateVersion7(), _banc.Affilie, "Poste", _banc.Poste, "R1", 24, new DateOnly(2026, 1, 1), null), Ct);

        Store.Obligations.Single(o => o.Type == TypeObligation.EvaluationPeriodique).DateDue.ShouldBe(new DateOnly(2028, 2, 3));
    }

    [Fact]
    public async Task Un_nouveau_profil_de_risques_cree_les_obligations_du_risque_ajoute()
    {
        await Contexte();
        await Regle(code: "R2");

        await new ProfilRisquePosteModifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new ProfilRisquePosteModifie(_banc.Poste, _banc.Affilie, ["R1", "R2"], new DateOnly(2026, 5, 1)), Ct);

        Store.Obligations.Count(o => o.Type == TypeObligation.EvaluationPrealable).ShouldBe(2);
        Store.Obligations.Single(o => o.CodesRisques.SequenceEqual(["R2"])).DateDue.ShouldBe(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public async Task Un_risque_retire_du_profil_annule_l_obligation_ouverte_correspondante()
    {
        await Contexte();
        await Regle(code: "R2");
        await new ProfilRisquePosteModifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new ProfilRisquePosteModifie(_banc.Poste, _banc.Affilie, ["R1", "R2"], new DateOnly(2026, 5, 1)), Ct);

        // Le même profil est republié sans R2 pour la même date d'effet (correction) : l'obligation de R2 n'est plus due.
        await new ProfilRisquePosteModifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new ProfilRisquePosteModifie(_banc.Poste, _banc.Affilie, ["R1"], new DateOnly(2026, 5, 1)) { OccurredAt = DateTimeOffset.UtcNow.AddMinutes(1) }, Ct);

        var r2 = Store.Obligations.Single(o => o.CodesRisques.SequenceEqual(["R2"]));
        r2.Statut.ShouldBe(StatutObligation.Annule);
        r2.MotifAnnulation.ShouldBe(MotifAnnulation.Recalcul);
    }

    [Fact]
    public async Task La_reprise_annoncee_cree_l_examen_de_reprise_a_dix_jours_ouvrables()
    {
        _banc.OccupationActive();
        await new RepriseAnnonceeHandler(_banc.Enregistrement).HandleAsync(
            new RepriseAnnoncee(_banc.Personne, _banc.Affilie, new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 23)), Ct);

        var obligation = Store.Obligations.ShouldHaveSingleItem();
        obligation.Type.ShouldBe(TypeObligation.ExamenReprise);
        obligation.DateLimite.ShouldBe(new DateOnly(2026, 5, 12));
        Store.Publies<ObligationCreee>().ShouldHaveSingleItem().TypeExamen.ShouldBe("EXAMEN_REPRISE");
    }

    [Fact]
    public async Task L_incapacite_notifiee_cree_l_estimation_du_potentiel_et_la_reprise_la_supprime_si_elle_est_anterieure()
    {
        var incapacite = new IncapaciteNotifiee(Guid.CreateVersion7(), _banc.Personne, _banc.Affilie, new DateOnly(2026, 4, 15), "MUTUALITE");
        await new IncapaciteNotifieeHandler(Store, _banc.MiseAJour).HandleAsync(incapacite, Ct);
        var estimation = Store.Obligations.Single(o => o.Type == TypeObligation.EstimationPotentielTravail);
        estimation.DateDue.ShouldBe(new DateOnly(2026, 6, 10));
        estimation.DateLimite.ShouldBeNull();

        _banc.OccupationActive();
        await new RepriseAnnonceeHandler(_banc.Enregistrement).HandleAsync(
            new RepriseAnnoncee(_banc.Personne, _banc.Affilie, new DateOnly(2026, 5, 20), new DateOnly(2026, 4, 15)), Ct);

        estimation.Statut.ShouldBe(StatutObligation.Annule);
    }

    [Fact]
    public async Task Le_trajet_de_reintegration_cree_l_evaluation_dans_les_quarante_neuf_jours_et_sa_fin_la_cloture()
    {
        var trajet = Guid.CreateVersion7();
        await new TrajetDemarreHandler(Store, _banc.MiseAJour).HandleAsync(
            new TrajetDemarre(trajet, _banc.Personne, _banc.Affilie, "TRAVAILLEUR", new DateOnly(2026, 5, 4)), Ct);
        var evaluation = Store.Obligations.ShouldHaveSingleItem();
        evaluation.Type.ShouldBe(TypeObligation.EvaluationReintegration);
        evaluation.DateLimite.ShouldBe(new DateOnly(2026, 6, 22));

        await new TrajetTermineHandler(Store, _banc.MiseAJour).HandleAsync(
            new TrajetTermine(trajet, _banc.Personne, _banc.Affilie, "ABANDONNE", new DateOnly(2026, 5, 20)), Ct);

        evaluation.Statut.ShouldBe(StatutObligation.Annule);
    }

    [Fact]
    public async Task L_etat_particulier_de_protection_de_la_maternite_cree_l_obligation_pour_les_risques_du_poste()
    {
        await Contexte();

        await new EtatParticulierDeclareHandler(Store, _banc.MiseAJour).HandleAsync(
            new EtatParticulierDeclare(Guid.CreateVersion7(), _banc.Personne, "PROTECTION_MATERNITE", new DateOnly(2026, 6, 1), null), Ct);

        var maternite = Store.Obligations.Single(o => o.Type == TypeObligation.ProtectionMaternite);
        maternite.DateDue.ShouldBe(new DateOnly(2026, 6, 1));
        maternite.CodesRisques.ShouldBe(["R1"]);
        Store.Publies<ObligationCreee>().ShouldContain(e => e.TypeExamen == "PROTECTION_MATERNITE");
    }

    [Fact]
    public async Task Un_parametre_legal_modifie_recalcule_les_echeances_qui_en_dependent()
    {
        _banc.OccupationActive();
        await new RepriseAnnonceeHandler(_banc.Enregistrement).HandleAsync(
            new RepriseAnnoncee(_banc.Personne, _banc.Affilie, new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 23)), Ct);

        await new ParametreLegalModifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new ParametreLegalModifie(CodesParametres.RepriseDelai, 5, "JoursOuvrables", new DateOnly(2026, 1, 1), null), Ct);

        var obligation = Store.Obligations.Single();
        obligation.DateLimite.ShouldBe(new DateOnly(2026, 5, 5));
        obligation.Traces.Count.ShouldBe(2);
        Store.Publies<ObligationCreee>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Un_parametre_legal_etranger_au_moteur_est_ignore()
    {
        await new ParametreLegalModifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new ParametreLegalModifie("FACTURATION.TAUX", 3, "Mois", new DateOnly(2026, 1, 1), null), Ct);

        Store.Parametres.ShouldBeEmpty();
    }

    [Fact]
    public async Task Les_jours_feries_supplementaires_recalculent_les_delais_en_jours_ouvrables()
    {
        _banc.OccupationActive();
        await new RepriseAnnonceeHandler(_banc.Enregistrement).HandleAsync(
            new RepriseAnnoncee(_banc.Personne, _banc.Affilie, new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 23)), Ct);

        await new JoursFeriesModifiesHandler(Store, _banc.MiseAJour).HandleAsync(new JoursFeriesModifies(2026, [new DateOnly(2026, 5, 4)]), Ct);

        Store.Obligations.Single().DateLimite.ShouldBe(new DateOnly(2026, 5, 13));

        // Jour supprimé du calendrier : l'état complet suivant ne le contient plus.
        await new JoursFeriesModifiesHandler(Store, _banc.MiseAJour).HandleAsync(
            new JoursFeriesModifies(2026, []) { OccurredAt = DateTimeOffset.UtcNow.AddMinutes(1) }, Ct);
        Store.Obligations.Single().DateLimite.ShouldBe(new DateOnly(2026, 5, 12));
    }

    [Fact]
    public async Task Un_evenement_de_jours_feries_sans_detail_d_un_producteur_anterieur_est_ignore()
    {
        await new JoursFeriesModifiesHandler(Store, _banc.MiseAJour).HandleAsync(new JoursFeriesModifies(2026), Ct);

        Store.Calendriers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_rendez_vous_planifie_puis_annule_fait_passer_l_obligation_de_planifie_a_a_planifier()
    {
        await Contexte();
        var obligation = Store.Obligations.Single();
        var rendezVous = Guid.CreateVersion7();
        var debut = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

        await new RendezVousPlanifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new RendezVousPlanifie(rendezVous, _banc.Personne, _banc.Affilie, debut, [obligation.Id]) { OccurredAt = T(1) }, Ct);
        obligation.Statut.ShouldBe(StatutObligation.Planifie);
        obligation.RendezVousId.ShouldBe(rendezVous);

        await new RendezVousAnnuleHandler(Store, _banc.MiseAJour).HandleAsync(new RendezVousAnnule(rendezVous, _banc.Personne, "ANNULE_PAR_PATIENT") { OccurredAt = T(2) }, Ct);
        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        obligation.RendezVousId.ShouldBeNull();
    }

    [Fact]
    public async Task Un_rendez_vous_annule_recu_avant_sa_planification_reste_annule()
    {
        await Contexte();
        var obligation = Store.Obligations.Single();
        var rendezVous = Guid.CreateVersion7();

        await new RendezVousAnnuleHandler(Store, _banc.MiseAJour).HandleAsync(new RendezVousAnnule(rendezVous, _banc.Personne, "ANNULE") { OccurredAt = T(2) }, Ct);
        await new RendezVousPlanifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new RendezVousPlanifie(rendezVous, _banc.Personne, _banc.Affilie, new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), [obligation.Id]) { OccurredAt = T(1) }, Ct);

        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
    }

    [Fact]
    public async Task Une_liste_nominative_generee_est_conservee_pour_l_alerte_de_revue()
    {
        var liste = Guid.CreateVersion7();
        var handler = new ListeNominativeGenereeHandler(Store, Store);

        await handler.HandleAsync(new ListeNominativeGeneree(liste, _banc.Affilie, "EXPOSES", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5)), Ct);
        await handler.HandleAsync(new ListeNominativeGeneree(liste, _banc.Affilie, "EXPOSES", 2, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5)), Ct);

        var enregistree = Store.Listes.ShouldHaveSingleItem();
        enregistree.Version.ShouldBe(2);
        enregistree.DateGeneration.ShouldBe(new DateOnly(2026, 3, 5));
    }

    [Fact]
    public async Task Aucun_appel_synchrone_les_gestionnaires_ne_dependent_que_de_ports_locaux()
    {
        // Le recalcul s'appuie uniquement sur les projections : un travailleur inconnu n'a aucune obligation.
        await new ExamenClotureHandler(Store, _banc.MiseAJour).HandleAsync(
            new ExamenCloture(Guid.CreateVersion7(), Guid.CreateVersion7(), _banc.Affilie, "EVALUATION_PERIODIQUE", new DateOnly(2026, 1, 1)), Ct);

        Store.Obligations.ShouldBeEmpty();
        Store.Examens.ShouldHaveSingleItem();
    }
}
