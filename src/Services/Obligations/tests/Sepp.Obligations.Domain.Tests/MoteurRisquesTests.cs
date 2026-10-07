using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>SAN-01 : obligations déduites des risques du poste (évaluations préalable, périodique, actes, surveillance prolongée).</summary>
public class MoteurRisquesTests
{
    private const string Periodique = "EvaluationSantePeriodique";
    private const string Actes = "ActesMedicauxSupplementaires";
    private const string PrealableUniquement = "EvaluationPrealableUniquement";

    private static readonly DateOnly Debut = new(2026, 2, 1);

    private static Scenario Exposee(int? frequenceMois = 12, string type = Periodique, bool prolongee = false, DateOnly? finAffectation = null) =>
        new Scenario()
            .ProfilPoste(new DateOnly(2026, 1, 1), "R1")
            .Regle("R1", type, frequenceMois, prolongee)
            .Affecte(Debut, finAffectation);

    [Fact]
    public void Une_nouvelle_exposition_cree_une_evaluation_prealable_due_au_debut_de_l_exposition()
    {
        var s = Exposee();

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.Type.ShouldBe(TypeObligation.EvaluationPrealable);
        echeance.DateDue.ShouldBe(Debut);
        echeance.DateLimite.ShouldBe(Debut);
        echeance.Origine.ShouldBe(OrigineObligation.Regle);
        echeance.CodesRisques.ShouldBe(["R1"]);
        echeance.AffilieId.ShouldBe(s.Affilie);
        echeance.Realisation.ShouldBeNull();
        echeance.Justification.Regle.ShouldBe("regle-surveillance:R1");
        echeance.Justification.RegleVersion.ShouldBe(1);
        echeance.Justification.Entrees.ShouldContain(e => e.Key == "exposition_debut" && e.Value == "2026-02-01");
    }

    [Fact]
    public void Aucune_obligation_sans_affectation_a_un_poste_a_risque()
    {
        var s = new Scenario().ProfilPoste(new DateOnly(2026, 1, 1), "R1").Regle("R1", Periodique, 12);

        s.Calculer().Echeances.ShouldBeEmpty();
    }

    [Fact]
    public void Un_risque_sans_regle_de_surveillance_connue_est_signale_sans_creer_d_obligation()
    {
        var s = new Scenario().ProfilPoste(new DateOnly(2026, 1, 1), "R1").Affecte(Debut);

        var resultat = s.Calculer();

        resultat.Echeances.ShouldBeEmpty();
        resultat.ExpositionsSansRegle.ShouldHaveSingleItem().CodeRisque.ShouldBe("R1");
    }

    [Fact]
    public void Une_evaluation_realisee_avant_l_affectation_vaut_evaluation_prealable_et_fonde_le_cycle_periodique()
    {
        var s = Exposee();
        var examenId = s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));

        var echeances = s.Calculer().Echeances;

        echeances.Count.ShouldBe(2);
        var prealable = echeances[0];
        prealable.Type.ShouldBe(TypeObligation.EvaluationPrealable);
        prealable.Realisation.ShouldBe(new Realisation(new DateOnly(2026, 1, 20), examenId));
        var periodique = echeances[1];
        periodique.Type.ShouldBe(TypeObligation.EvaluationPeriodique);
        periodique.DateDue.ShouldBe(new DateOnly(2027, 1, 20));
        periodique.DateLimite.ShouldBe(new DateOnly(2027, 1, 20));
        periodique.Realisation.ShouldBeNull();
        periodique.Justification.Entrees.ShouldContain(e => e.Key == "frequence_mois" && e.Value == "12");
        periodique.Justification.Entrees.ShouldContain(e => e.Key == "source_frequence" && e.Value.StartsWith("règle du risque", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_evaluation_trop_ancienne_ne_vaut_pas_evaluation_prealable()
    {
        var s = Exposee();
        s.Examen("EVALUATION_PERIODIQUE", new DateOnly(2025, 6, 1));

        s.Calculer().Echeances.ShouldHaveSingleItem().Type.ShouldBe(TypeObligation.EvaluationPrealable);
    }

    [Fact]
    public void Chaque_evaluation_realise_le_cycle_precedent_et_fonde_le_suivant()
    {
        var s = Exposee();
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));
        var deuxieme = s.Examen("EVALUATION_PERIODIQUE", new DateOnly(2027, 1, 10));

        var periodiques = s.Calculer().Echeances.Where(e => e.Type == TypeObligation.EvaluationPeriodique).OrderBy(e => e.DateDue).ToList();

        periodiques.Count.ShouldBe(2);
        periodiques[0].Realisation.ShouldBe(new Realisation(new DateOnly(2027, 1, 10), deuxieme));
        periodiques[1].DateDue.ShouldBe(new DateOnly(2028, 1, 10));
        periodiques[1].Realisation.ShouldBeNull();
        periodiques[0].Cle.ShouldNotBe(periodiques[1].Cle);
    }

    [Fact]
    public void La_surcharge_du_cpmt_pour_le_travailleur_prime_sur_celle_du_poste_et_sur_la_regle()
    {
        var s = Exposee();
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));
        s.Surcharge("Poste", s.Poste, "R1", 24, new DateOnly(2026, 1, 1));
        s.Surcharge("Personne", s.Personne, "R1", 6, new DateOnly(2026, 1, 1));

        var periodique = s.Calculer().Echeances.Single(e => e.Type == TypeObligation.EvaluationPeriodique);

        periodique.DateDue.ShouldBe(new DateOnly(2026, 7, 20));
        periodique.Origine.ShouldBe(OrigineObligation.Surcharge);
        periodique.Justification.Entrees.ShouldContain(e => e.Key == "source_frequence" && e.Value.Contains("travailleur", StringComparison.Ordinal));
    }

    [Fact]
    public void La_surcharge_du_poste_s_applique_a_defaut_de_surcharge_du_travailleur()
    {
        var s = Exposee();
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));
        s.Surcharge("Poste", s.Poste, "R1", 24, new DateOnly(2026, 1, 1));

        var periodique = s.Calculer().Echeances.Single(e => e.Type == TypeObligation.EvaluationPeriodique);

        periodique.DateDue.ShouldBe(new DateOnly(2028, 1, 20));
        periodique.Origine.ShouldBe(OrigineObligation.Surcharge);
    }

    [Fact]
    public void Une_surcharge_cloturee_ne_s_applique_plus()
    {
        var s = Exposee();
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));
        s.Surcharge("Personne", s.Personne, "R1", 6, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1));

        var periodique = s.Calculer().Echeances.Single(e => e.Type == TypeObligation.EvaluationPeriodique);

        periodique.DateDue.ShouldBe(new DateOnly(2027, 1, 20));
        periodique.Origine.ShouldBe(OrigineObligation.Regle);
    }

    [Fact]
    public void La_surcharge_d_un_autre_travailleur_est_ignoree()
    {
        var s = Exposee();
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));
        s.Surcharge("Personne", Guid.CreateVersion7(), "R1", 6, new DateOnly(2026, 1, 1));

        s.Calculer().Echeances.Single(e => e.Type == TypeObligation.EvaluationPeriodique).Origine.ShouldBe(OrigineObligation.Regle);
    }

    [Fact]
    public void La_version_de_la_regle_en_vigueur_a_la_date_de_calcul_est_appliquee()
    {
        var s = Exposee();
        s.Regle("R1", Periodique, 6, valideDu: new DateOnly(2026, 9, 1), version: 2);
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));

        s.Calculer(new DateOnly(2026, 6, 15)).Echeances.Single(e => e.Type == TypeObligation.EvaluationPeriodique).DateDue.ShouldBe(new DateOnly(2027, 1, 20));
        var apres = s.Calculer(new DateOnly(2026, 10, 1)).Echeances.Single(e => e.Type == TypeObligation.EvaluationPeriodique);

        apres.DateDue.ShouldBe(new DateOnly(2026, 7, 20));
        apres.Justification.RegleVersion.ShouldBe(2);
    }

    [Fact]
    public void Une_surveillance_prolongee_prend_le_relais_apres_la_fin_de_l_exposition()
    {
        var s = Exposee(prolongee: true, finAffectation: new DateOnly(2026, 5, 1));
        s.Examen("EVALUATION_PREALABLE", Debut);

        var prolongee = s.Calculer().Echeances.Single(e => e.Type == TypeObligation.SurveillanceProlongee);

        prolongee.DateDue.ShouldBe(new DateOnly(2027, 2, 1));
        prolongee.Justification.Explication.ShouldContain("après la fin de l'exposition");
    }

    [Fact]
    public void Sans_surveillance_prolongee_la_chaine_s_arrete_a_la_fin_de_l_exposition()
    {
        var s = Exposee(finAffectation: new DateOnly(2026, 5, 1));
        s.Examen("EVALUATION_PREALABLE", Debut);

        s.Calculer().Echeances.ShouldAllBe(e => e.Type == TypeObligation.EvaluationPrealable);
    }

    [Fact]
    public void Un_cycle_periodique_qui_tombe_avant_la_fin_de_l_exposition_reste_une_evaluation_periodique()
    {
        var s = Exposee(frequenceMois: 3, prolongee: true, finAffectation: new DateOnly(2026, 12, 31));
        s.Examen("EVALUATION_PREALABLE", Debut);

        s.Calculer().Echeances.Single(e => e.Type != TypeObligation.EvaluationPrealable).Type.ShouldBe(TypeObligation.EvaluationPeriodique);
    }

    [Fact]
    public void Des_actes_medicaux_supplementaires_sont_dus_avec_leur_propre_frequence()
    {
        var s = Exposee(frequenceMois: 24, type: Actes);
        s.Examen("ACTES_MEDICAUX_SUPPLEMENTAIRES", new DateOnly(2026, 2, 3));

        var echeances = s.Calculer().Echeances;

        echeances.Count.ShouldBe(2);
        echeances[0].Type.ShouldBe(TypeObligation.ActesMedicauxSupplementaires);
        echeances[0].Realisation.ShouldNotBeNull();
        echeances[1].Type.ShouldBe(TypeObligation.ActesMedicauxSupplementaires);
        echeances[1].DateDue.ShouldBe(new DateOnly(2028, 2, 3));
    }

    [Fact]
    public void Une_evaluation_prealable_uniquement_ne_cree_pas_de_cycle_periodique()
    {
        var s = Exposee(frequenceMois: null, type: PrealableUniquement);
        s.Examen("EVALUATION_PREALABLE", Debut);

        s.Calculer().Echeances.ShouldHaveSingleItem().Type.ShouldBe(TypeObligation.EvaluationPrealable);
    }

    [Fact]
    public void Plusieurs_risques_qui_commencent_le_meme_jour_partagent_une_seule_evaluation_prealable()
    {
        var s = new Scenario().ProfilPoste(new DateOnly(2026, 1, 1), "R2", "R1").Regle("R1", Periodique, 12).Regle("R2", Periodique, 24).Affecte(Debut);

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.CodesRisques.ShouldBe(["R1", "R2"]);
        echeance.Justification.Entrees.ShouldContain(e => e.Key == "regle.R1");
        echeance.Justification.Entrees.ShouldContain(e => e.Key == "regle.R2");
    }

    [Fact]
    public void Changer_de_poste_en_restant_expose_au_meme_risque_ne_cree_pas_de_nouvelle_evaluation_prealable()
    {
        var s = new Scenario().Regle("R1", Periodique, 12);
        var autrePoste = Guid.CreateVersion7();
        s.Profils.Add(new ProfilRisquePosteLocal(s.Poste, new DateOnly(2026, 1, 1), s.Affilie, ["R1"], Scenario.T0));
        s.Profils.Add(new ProfilRisquePosteLocal(autrePoste, new DateOnly(2026, 1, 1), s.Affilie, ["R1"], Scenario.T0));
        s.Affecte(Debut, new DateOnly(2026, 4, 1));
        s.Affecte(new DateOnly(2026, 4, 1), poste: autrePoste);

        var resultat = s.Calculer();

        resultat.Expositions.ShouldHaveSingleItem().PosteIds.Count.ShouldBe(2);
        resultat.Echeances.ShouldHaveSingleItem().DateDue.ShouldBe(Debut);
    }

    [Fact]
    public void Un_nouveau_risque_au_meme_poste_cree_une_evaluation_prealable_a_la_date_du_nouveau_profil()
    {
        var s = Exposee();
        s.Regle("R2", Periodique, 12);
        s.Profils.Add(new ProfilRisquePosteLocal(s.Poste, new DateOnly(2026, 4, 1), s.Affilie, ["R1", "R2"], Scenario.T0));

        var echeances = s.Calculer().Echeances;

        echeances.Count.ShouldBe(2);
        echeances.Single(e => e.CodesRisques.SequenceEqual(["R2"])).DateDue.ShouldBe(new DateOnly(2026, 4, 1));
        echeances.Single(e => e.CodesRisques.SequenceEqual(["R1"])).DateDue.ShouldBe(Debut);
    }

    [Fact]
    public void Un_travailleur_sorti_de_l_entreprise_n_a_plus_d_evaluation_prealable_a_planifier()
    {
        var s = Exposee().Occupe(new DateOnly(2025, 1, 1), new DateOnly(2026, 5, 31));

        var resultat = s.Calculer();

        resultat.Echeances.ShouldBeEmpty();
        resultat.SortiDeToutes.ShouldBeTrue();
        resultat.EstSortiDe(s.Affilie).ShouldBeTrue();
    }

    [Fact]
    public void Un_travailleur_toujours_occupe_garde_ses_obligations()
    {
        var s = Exposee().Occupe(new DateOnly(2025, 1, 1));

        var resultat = s.Calculer();

        resultat.Echeances.ShouldHaveSingleItem();
        resultat.SortiDeToutes.ShouldBeFalse();
    }

    [Fact]
    public void Un_travailleur_sorti_garde_les_evaluations_deja_realisees()
    {
        var s = Exposee().Occupe(new DateOnly(2025, 1, 1), new DateOnly(2026, 5, 31));
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));

        // Le cycle périodique suivant n'est plus dû, mais l'évaluation réalisée reste connue pour la clôturer.
        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.Type.ShouldBe(TypeObligation.EvaluationPrealable);
        echeance.EstRealisee.ShouldBeTrue();
    }

    [Fact]
    public void Le_resultat_ne_depend_pas_de_l_ordre_de_reception_des_projections()
    {
        var s = Exposee();
        s.Regle("R2", Periodique, 24).Surcharge("Personne", s.Personne, "R1", 6, new DateOnly(2026, 1, 1));
        s.Profils.Add(new ProfilRisquePosteLocal(s.Poste, new DateOnly(2026, 4, 1), s.Affilie, ["R1", "R2"], Scenario.T0));
        s.Examen("EVALUATION_PREALABLE", new DateOnly(2026, 1, 20));
        s.Examen("EVALUATION_PERIODIQUE", new DateOnly(2026, 8, 1));
        s.Reprise(new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 2));
        s.Incapacite(new DateOnly(2026, 3, 2));

        var normal = Signature(s.Calculer());
        s.Affectations.Reverse();
        s.Profils.Reverse();
        s.Regles.Reverse();
        s.Surcharges.Reverse();
        s.Examens.Reverse();
        s.Reprises.Reverse();
        s.Incapacites.Reverse();
        var inverse = Signature(s.Calculer());

        inverse.ShouldBe(normal);
        normal.Count.ShouldBeGreaterThan(3);
    }

    private static List<string> Signature(ResultatCalcul resultat) =>
        resultat.Echeances
            .Select(e => $"{e.Cle}|{e.Type}|{e.Origine}|{e.DateDue}|{e.DateLimite}|{e.Realisation?.Date}|{string.Join(',', e.CodesRisques)}|{e.Justification.Explication}")
            .ToList();
}
