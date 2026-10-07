using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>§5.1 : obligations déclenchées par un événement (reprise, demande du travailleur, maternité, incapacité, réintégration).</summary>
public class MoteurEvenementsTests
{
    private static readonly DateOnly Reprise = new(2026, 4, 27);
    private static readonly DateOnly AbsenceDeCinqSemaines = new(2026, 3, 23);

    [Fact]
    public void L_examen_de_reprise_est_du_le_jour_de_la_reprise_au_plus_tard_dix_jours_ouvrables_apres()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines);

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.Type.ShouldBe(TypeObligation.ExamenReprise);
        echeance.Origine.ShouldBe(OrigineObligation.Evenement);
        echeance.DateDue.ShouldBe(Reprise);

        // Le 1er mai (fête du travail) n'est pas un jour ouvrable : 27 avril + 10 jours ouvrables = 12 mai.
        echeance.DateLimite.ShouldBe(new DateOnly(2026, 5, 12));
        echeance.Justification.Regle.ShouldBe("parametre:SANTE.REPRISE.DELAI");
        echeance.Justification.Entrees.ShouldContain(e => e.Key == "delai" && e.Value.Contains("valeur par défaut", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_jour_ferie_supplementaire_recu_de_referentiels_decale_l_echeance()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines).JourFerie(new DateOnly(2026, 5, 4));

        s.Calculer().Echeances.ShouldHaveSingleItem().DateLimite.ShouldBe(new DateOnly(2026, 5, 13));
    }

    [Fact]
    public void Un_delai_modifie_dans_referentiels_remplace_la_valeur_par_defaut_et_la_trace_l_indique()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines)
            .Parametre(CodesParametres.RepriseDelai, 5, "JoursOuvrables", new DateOnly(2026, 1, 1));

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.DateLimite.ShouldBe(new DateOnly(2026, 5, 5));
        echeance.Justification.Entrees.ShouldContain(e => e.Key == "delai" && e.Value.Contains("Référentiels", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_parametre_pas_encore_en_vigueur_n_est_pas_applique()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines)
            .Parametre(CodesParametres.RepriseDelai, 5, "JoursOuvrables", new DateOnly(2027, 1, 1));

        s.Calculer().Echeances.ShouldHaveSingleItem().DateLimite.ShouldBe(new DateOnly(2026, 5, 12));
    }

    [Fact]
    public void Un_parametre_d_unite_inconnue_retombe_sur_la_valeur_par_defaut()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines)
            .Parametre(CodesParametres.RepriseDelai, 5, "Siecles", new DateOnly(2026, 1, 1));

        s.Calculer().Echeances.ShouldHaveSingleItem().DateLimite.ShouldBe(new DateOnly(2026, 5, 12));
    }

    [Fact]
    public void Une_absence_de_moins_de_quatre_semaines_ne_donne_pas_d_examen_de_reprise()
    {
        var s = new Scenario().Reprise(Reprise, new DateOnly(2026, 4, 6));

        s.Calculer().Echeances.ShouldBeEmpty();
    }

    [Fact]
    public void Une_absence_d_exactement_quatre_semaines_donne_un_examen_de_reprise()
    {
        var s = new Scenario().Reprise(Reprise, new DateOnly(2026, 3, 30));

        s.Calculer().Echeances.ShouldHaveSingleItem().Type.ShouldBe(TypeObligation.ExamenReprise);
    }

    [Fact]
    public void L_examen_de_reprise_clot_est_marque_realise()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines);
        var examenId = s.Examen("EXAMEN_REPRISE", new DateOnly(2026, 4, 30));

        s.Calculer().Echeances.ShouldHaveSingleItem().Realisation.ShouldBe(new Realisation(new DateOnly(2026, 4, 30), examenId));
    }

    [Fact]
    public void Un_examen_d_un_autre_type_ne_realise_pas_l_examen_de_reprise()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines);
        s.Examen("EVALUATION_PERIODIQUE", new DateOnly(2026, 4, 30));

        s.Calculer().Echeances.ShouldHaveSingleItem().Realisation.ShouldBeNull();
    }

    [Theory]
    [InlineData(TypeObligation.VisitePreReprise, "parametre:SANTE.PRE_REPRISE.DELAI")]
    [InlineData(TypeObligation.ConsultationSpontanee, "parametre:SANTE.CONSULTATION_SPONTANEE.DELAI")]
    public void Une_demande_du_travailleur_donne_une_obligation_a_dix_jours_ouvrables(TypeObligation type, string regle)
    {
        var s = new Scenario().Demande(type, new DateOnly(2026, 6, 1));

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.Type.ShouldBe(type);
        echeance.DateDue.ShouldBe(new DateOnly(2026, 6, 1));
        echeance.DateLimite.ShouldBe(new DateOnly(2026, 6, 15));
        echeance.Justification.Regle.ShouldBe(regle);
        echeance.Cle.ShouldStartWith("DEMANDE:");
    }

    [Fact]
    public void Un_jour_ferie_dans_le_delai_d_une_consultation_spontanee_repousse_la_date_limite()
    {
        var s = new Scenario().Demande(TypeObligation.ConsultationSpontanee, new DateOnly(2026, 7, 13));

        // Le 21 juillet (fête nationale) est férié : 13 juillet + 10 jours ouvrables = 28 juillet.
        s.Calculer().Echeances.ShouldHaveSingleItem().DateLimite.ShouldBe(new DateOnly(2026, 7, 28));
    }

    [Fact]
    public void L_estimation_du_potentiel_de_travail_est_due_huit_semaines_apres_le_debut_de_l_incapacite()
    {
        var s = new Scenario().Incapacite(new DateOnly(2026, 3, 2));

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.Type.ShouldBe(TypeObligation.EstimationPotentielTravail);
        echeance.DateDue.ShouldBe(new DateOnly(2026, 4, 27));
        echeance.DateLimite.ShouldBeNull();
        echeance.Justification.Regle.ShouldBe("parametre:REINTEGRATION.ESTIMATION_POTENTIEL");
    }

    [Fact]
    public void Une_reprise_avant_les_huit_semaines_supprime_l_estimation_du_potentiel()
    {
        var s = new Scenario().Incapacite(new DateOnly(2026, 3, 2)).Reprise(new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 2));

        s.Calculer().Echeances.ShouldNotContain(e => e.Type == TypeObligation.EstimationPotentielTravail);
    }

    [Fact]
    public void Une_reprise_apres_les_huit_semaines_laisse_l_estimation_du_potentiel_due()
    {
        var s = new Scenario().Incapacite(new DateOnly(2026, 3, 2)).Reprise(new DateOnly(2026, 6, 1), new DateOnly(2026, 3, 2));

        s.Calculer().Echeances.ShouldContain(e => e.Type == TypeObligation.EstimationPotentielTravail);
    }

    [Fact]
    public void L_evaluation_de_reintegration_est_due_dans_les_quarante_neuf_jours_de_la_demande()
    {
        var s = new Scenario().Trajet(new DateOnly(2026, 5, 4));

        var echeance = s.Calculer().Echeances.ShouldHaveSingleItem();

        echeance.Type.ShouldBe(TypeObligation.EvaluationReintegration);
        echeance.DateDue.ShouldBe(new DateOnly(2026, 5, 4));
        echeance.DateLimite.ShouldBe(new DateOnly(2026, 6, 22));
    }

    [Fact]
    public void Un_trajet_termine_sans_evaluation_n_a_plus_d_obligation()
    {
        var s = new Scenario().Trajet(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 20));

        s.Calculer().Echeances.ShouldBeEmpty();
    }

    [Fact]
    public void Un_trajet_termine_garde_l_evaluation_realisee()
    {
        var s = new Scenario().Trajet(new DateOnly(2026, 5, 4), new DateOnly(2026, 6, 10));
        s.Examen("EVALUATION_REINTEGRATION", new DateOnly(2026, 5, 20));

        s.Calculer().Echeances.ShouldHaveSingleItem().EstRealisee.ShouldBeTrue();
    }

    [Fact]
    public void La_protection_de_la_maternite_est_due_des_la_declaration_pour_les_risques_du_poste()
    {
        var s = new Scenario().ProfilPoste(new DateOnly(2026, 1, 1), "R1").Regle("R1", "EvaluationSantePeriodique", 12)
            .Affecte(new DateOnly(2026, 2, 1)).Maternite(new DateOnly(2026, 5, 10));

        var maternite = s.Calculer().Echeances.Single(e => e.Type == TypeObligation.ProtectionMaternite);

        maternite.DateDue.ShouldBe(new DateOnly(2026, 5, 10));
        maternite.DateLimite.ShouldBeNull();
        maternite.CodesRisques.ShouldBe(["R1"]);
        maternite.Justification.Regle.ShouldBe("etat-particulier:PROTECTION_MATERNITE");
    }

    [Fact]
    public void La_protection_de_la_maternite_sans_exposition_a_un_risque_ne_cree_pas_d_obligation()
    {
        var s = new Scenario().Affecte(new DateOnly(2026, 2, 1)).Maternite(new DateOnly(2026, 5, 10));

        s.Calculer().Echeances.ShouldBeEmpty();
    }

    [Fact]
    public void Un_autre_etat_particulier_ne_declenche_pas_la_protection_de_la_maternite()
    {
        var s = new Scenario().ProfilPoste(new DateOnly(2026, 1, 1), "R1").Regle("R1", "EvaluationSantePeriodique", 12).Affecte(new DateOnly(2026, 2, 1));
        s.Etats.Add(new Projections.EtatParticulierLocal(Guid.CreateVersion7(), s.Personne, "TRAVAIL_DE_NUIT", new DateOnly(2026, 5, 10), null, Scenario.T0));

        s.Calculer().Echeances.ShouldNotContain(e => e.Type == TypeObligation.ProtectionMaternite);
    }

    [Fact]
    public void Un_travailleur_sorti_n_a_plus_d_obligation_d_evenement_non_realisee()
    {
        var s = new Scenario().Reprise(Reprise, AbsenceDeCinqSemaines).Occupe(new DateOnly(2025, 1, 1), new DateOnly(2026, 5, 31));

        s.Calculer().Echeances.ShouldBeEmpty();
    }
}
