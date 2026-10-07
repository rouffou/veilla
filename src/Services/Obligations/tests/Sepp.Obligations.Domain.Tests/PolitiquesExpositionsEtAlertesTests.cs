using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>ARC-21 : politiques légales injectées, valeurs par défaut documentées.</summary>
public class PolitiquesLegalesTests
{
    private static readonly DateOnly Date = new(2026, 6, 15);

    [Theory]
    [InlineData(CodesParametres.RepriseAbsenceMinimum, 4, UniteDuree.Semaines)]
    [InlineData(CodesParametres.RepriseDelai, 10, UniteDuree.JoursOuvrables)]
    [InlineData(CodesParametres.ConsultationSpontaneeDelai, 10, UniteDuree.JoursOuvrables)]
    [InlineData(CodesParametres.PreRepriseDelai, 10, UniteDuree.JoursOuvrables)]
    [InlineData(CodesParametres.EstimationPotentiel, 8, UniteDuree.Semaines)]
    [InlineData(CodesParametres.InvitationReintegrationDelai, 49, UniteDuree.JoursCalendrier)]
    [InlineData(CodesParametres.RevueListesNominatives, 12, UniteDuree.Mois)]
    public void Les_valeurs_par_defaut_sont_celles_du_catalogue_des_parametres_legaux(string code, int valeur, UniteDuree unite)
    {
        var duree = new PolitiquesLegales([]).Duree(code, Date);

        duree.Valeur.ShouldBe(valeur);
        duree.Unite.ShouldBe(unite);
        duree.Source.ShouldBe("valeur par défaut");
    }

    [Fact]
    public void Un_parametre_recu_prime_sur_la_valeur_par_defaut_selon_sa_periode_de_validite()
    {
        var politiques = new PolitiquesLegales(
        [
            new ParametreLegalLocal(CodesParametres.RepriseDelai, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), 7, "JoursOuvrables", Scenario.T0),
            new ParametreLegalLocal(CodesParametres.RepriseDelai, new DateOnly(2026, 6, 1), null, 12, "JoursOuvrables", Scenario.T0),
        ]);

        politiques.Duree(CodesParametres.RepriseDelai, new DateOnly(2026, 3, 1)).Valeur.ShouldBe(7);
        politiques.Duree(CodesParametres.RepriseDelai, Date).Valeur.ShouldBe(12);
        politiques.Duree(CodesParametres.RepriseDelai, Date).Source.ShouldContain("Référentiels");
        politiques.Duree(CodesParametres.RepriseDelai, new DateOnly(2025, 1, 1)).Source.ShouldBe("valeur par défaut");
    }

    [Theory]
    [InlineData(-3, "JoursOuvrables")]
    [InlineData(2.5, "JoursOuvrables")]
    [InlineData(5, "Inconnue")]
    public void Un_parametre_inexploitable_est_ignore(decimal valeur, string unite)
    {
        var politiques = new PolitiquesLegales([new ParametreLegalLocal(CodesParametres.RepriseDelai, new DateOnly(2026, 1, 1), null, valeur, unite, Scenario.T0)]);

        politiques.Duree(CodesParametres.RepriseDelai, Date).Source.ShouldBe("valeur par défaut");
    }

    [Fact]
    public void Un_parametre_inconnu_sans_valeur_par_defaut_est_une_erreur_de_programmation()
    {
        Should.Throw<InvalidOperationException>(() => new PolitiquesLegales([]).Duree("INCONNU", Date));
    }

    [Fact]
    public void Chaque_unite_s_ajoute_a_une_date_selon_le_calendrier()
    {
        var calendrier = BusinessCalendar.Belgian(2026);
        var lundi = new DateOnly(2026, 4, 27);

        new DureeLegale("X", 10, UniteDuree.JoursOuvrables, "t").AjouterA(lundi, calendrier).ShouldBe(new DateOnly(2026, 5, 12));
        new DureeLegale("X", 10, UniteDuree.JoursCalendrier, "t").AjouterA(lundi, calendrier).ShouldBe(new DateOnly(2026, 5, 7));
        new DureeLegale("X", 2, UniteDuree.Semaines, "t").AjouterA(lundi, calendrier).ShouldBe(new DateOnly(2026, 5, 11));
        new DureeLegale("X", 2, UniteDuree.Mois, "t").AjouterA(lundi, calendrier).ShouldBe(new DateOnly(2026, 6, 27));
        new DureeLegale("X", 1, UniteDuree.Annees, "t").AjouterA(lundi, calendrier).ShouldBe(new DateOnly(2027, 4, 27));
    }

    [Fact]
    public void Le_calendrier_ouvrable_ajoute_les_jours_supplementaires_aux_dix_jours_feries_legaux()
    {
        var calendrier = CalendrierOuvrable.Construire([new CalendrierLocal(2026, [new DateOnly(2026, 9, 28)], Scenario.T0)], 2026, 2026);

        calendrier.IsHoliday(new DateOnly(2026, 7, 21)).ShouldBeTrue();
        calendrier.IsHoliday(new DateOnly(2026, 9, 28)).ShouldBeTrue();
        calendrier.IsBusinessDay(new DateOnly(2026, 9, 29)).ShouldBeTrue();
    }

    [Fact]
    public void Un_calendrier_n_est_remplace_que_par_un_etat_plus_recent()
    {
        var calendrier = new CalendrierLocal(2026, [new DateOnly(2026, 9, 28)], Scenario.T0);

        calendrier.Appliquer([new DateOnly(2026, 9, 29)], Scenario.T0.AddDays(-1)).ShouldBeFalse();
        calendrier.JoursSupplementaires.ShouldBe([new DateOnly(2026, 9, 28)]);
        calendrier.Appliquer([new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 29)], Scenario.T0.AddDays(1)).ShouldBeTrue();
        calendrier.JoursSupplementaires.ShouldBe([new DateOnly(2026, 9, 29)]);
    }
}

/// <summary>AFF-22 : déduction des expositions à partir des affectations et des profils de risques.</summary>
public class ExpositionsTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Personne = Guid.CreateVersion7();

    private static AffectationLocale Affectation(Guid poste, DateOnly debut, DateOnly? fin) =>
        new(Guid.CreateVersion7(), Personne, poste, debut, fin, Scenario.T0);

    private static ProfilRisquePosteLocal Profil(Guid poste, DateOnly du, params string[] codes) => new(poste, du, Affilie, codes, Scenario.T0);

    [Fact]
    public void La_periode_d_exposition_est_l_intersection_de_l_affectation_et_du_profil()
    {
        var poste = Guid.CreateVersion7();

        var exposition = CalculExpositions.Calculer(
            [Affectation(poste, new DateOnly(2026, 2, 1), new DateOnly(2026, 8, 1))],
            [Profil(poste, new DateOnly(2026, 4, 1), "R1")]).ShouldHaveSingleItem();

        exposition.Debut.ShouldBe(new DateOnly(2026, 4, 1));
        exposition.Fin.ShouldBe(new DateOnly(2026, 8, 1));
        exposition.EstActiveAu(new DateOnly(2026, 7, 31)).ShouldBeTrue();
        exposition.EstActiveAu(new DateOnly(2026, 8, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Un_risque_retire_du_profil_met_fin_a_l_exposition_a_la_date_du_nouveau_profil()
    {
        var poste = Guid.CreateVersion7();

        var exposition = CalculExpositions.Calculer(
            [Affectation(poste, new DateOnly(2026, 1, 1), null)],
            [Profil(poste, new DateOnly(2026, 1, 1), "R1", "R2"), Profil(poste, new DateOnly(2026, 6, 1), "R1")]).Single(e => e.CodeRisque == "R2");

        exposition.Fin.ShouldBe(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Un_profil_sans_changement_de_risque_ne_coupe_pas_l_exposition()
    {
        var poste = Guid.CreateVersion7();

        var exposition = CalculExpositions.Calculer(
            [Affectation(poste, new DateOnly(2026, 1, 1), null)],
            [Profil(poste, new DateOnly(2026, 1, 1), "R1"), Profil(poste, new DateOnly(2026, 6, 1), "R1", "R2")]).Single(e => e.CodeRisque == "R1");

        exposition.Debut.ShouldBe(new DateOnly(2026, 1, 1));
        exposition.Fin.ShouldBeNull();
    }

    [Fact]
    public void Deux_periodes_separees_donnent_deux_expositions()
    {
        var poste = Guid.CreateVersion7();

        var expositions = CalculExpositions.Calculer(
            [Affectation(poste, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 1)), Affectation(poste, new DateOnly(2026, 1, 1), null)],
            [Profil(poste, new DateOnly(2024, 1, 1), "R1")]);

        expositions.Count.ShouldBe(2);
        expositions[0].Fin.ShouldBe(new DateOnly(2025, 6, 1));
        expositions[1].Debut.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void Une_affectation_sans_profil_de_risques_n_expose_a_rien()
    {
        CalculExpositions.Calculer([Affectation(Guid.CreateVersion7(), new DateOnly(2026, 1, 1), null)], []).ShouldBeEmpty();
    }

    [Fact]
    public void Un_profil_recu_plus_tard_pour_la_meme_cle_ne_remplace_pas_un_evenement_plus_recent()
    {
        var profil = Profil(Guid.CreateVersion7(), new DateOnly(2026, 1, 1), "R1");

        profil.Appliquer(Affilie, ["R2"], Scenario.T0.AddDays(-1)).ShouldBeFalse();
        profil.CodesRisques.ShouldBe(["R1"]);
        profil.Appliquer(Affilie, [" r2 ", "R2"], Scenario.T0.AddDays(1)).ShouldBeTrue();
        profil.CodesRisques.ShouldBe(["R2"]);
    }
}

public class TypesObligationTests
{
    [Fact]
    public void Chaque_type_a_un_code_publie_un_libelle_dans_les_quatre_langues_et_se_relit()
    {
        foreach (var type in Enum.GetValues<TypeObligation>())
        {
            var code = type.Code();
            code.ShouldBe(code.ToUpperInvariant());
            TypesObligation.TryParse(code, out var relu).ShouldBeTrue(code);
            relu.ShouldBe(type);
            var libelle = type.Libelle();
            new[] { Language.Fr, Language.Nl, Language.De, Language.En }.Select(libelle.In).Distinct().Count().ShouldBe(4, code);
        }
    }

    [Theory]
    [InlineData(TypeObligation.EvaluationPeriodique, "EVALUATION_PERIODIQUE")]
    [InlineData(TypeObligation.ExamenReprise, "EXAMEN_REPRISE")]
    [InlineData(TypeObligation.EstimationPotentielTravail, "ESTIMATION_POTENTIEL_TRAVAIL")]
    [InlineData(TypeObligation.ActesMedicauxSupplementaires, "ACTES_MEDICAUX_SUPPLEMENTAIRES")]
    public void Les_codes_publies_suivent_le_vocabulaire_des_evenements(TypeObligation type, string code) => type.Code().ShouldBe(code);

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("INCONNU")]
    [InlineData(null)]
    public void Un_code_invalide_n_est_pas_lu(string? code) => TypesObligation.TryParse(code, out _).ShouldBeFalse();

    [Fact]
    public void Seuls_les_types_qui_revelent_une_information_protegee_sont_confidentiels()
    {
        Enum.GetValues<TypeObligation>().Where(t => t.EstConfidentiel()).ShouldBe(
            [TypeObligation.VisitePreReprise, TypeObligation.ConsultationSpontanee, TypeObligation.ProtectionMaternite], ignoreOrder: true);
    }

    [Fact]
    public void Seuls_les_examens_a_periodicite_peuvent_etre_anticipes()
    {
        Enum.GetValues<TypeObligation>().Where(t => t.PeutEtreAnticipe()).ShouldBe(
        [
            TypeObligation.EvaluationPrealable, TypeObligation.EvaluationPeriodique, TypeObligation.ActesMedicauxSupplementaires,
            TypeObligation.SurveillanceProlongee,
        ], ignoreOrder: true);
    }
}

public class DemandeTravailleurTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 6, 15);

    [Fact]
    public void Une_demande_conserve_le_type_et_la_date_sans_motif()
    {
        var demande = DemandeTravailleur.Enregistrer(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TypeObligation.VisitePreReprise, Aujourdhui, Aujourdhui, " agent-1 ");

        demande.Type.ShouldBe(TypeObligation.VisitePreReprise);
        demande.DateDemande.ShouldBe(Aujourdhui);
        demande.EnregistreePar.ShouldBe("agent-1");
    }

    [Fact]
    public void Seules_la_pre_reprise_et_la_consultation_spontanee_peuvent_etre_demandees()
    {
        Should.Throw<DomainException>(() => DemandeTravailleur.Enregistrer(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TypeObligation.ExamenReprise, Aujourdhui, Aujourdhui, "agent"));
    }

    [Fact]
    public void Une_demande_a_une_date_future_ou_sans_auteur_est_refusee()
    {
        Should.Throw<DomainException>(() => DemandeTravailleur.Enregistrer(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TypeObligation.ConsultationSpontanee, Aujourdhui.AddDays(1), Aujourdhui, "agent"));
        Should.Throw<DomainException>(() => DemandeTravailleur.Enregistrer(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TypeObligation.ConsultationSpontanee, Aujourdhui, Aujourdhui, " "));
        Should.Throw<DomainException>(() => DemandeTravailleur.Enregistrer(
            Guid.Empty, Guid.CreateVersion7(), TypeObligation.ConsultationSpontanee, Aujourdhui, Aujourdhui, "agent"));
    }
}

/// <summary>AFF-32 : alertes de non-couverture.</summary>
public class AlertesTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 6, 15);

    [Fact]
    public void Un_travailleur_expose_dont_l_echeance_approche_sans_rendez_vous_declenche_une_alerte()
    {
        var personne = Guid.CreateVersion7();
        var proche = Obligation.Creer(personne, Fabrique.Echeance(cle: "A", due: new DateOnly(2026, 7, 1)), Scenario.T0);
        var lointaine = Obligation.Creer(Guid.CreateVersion7(), Fabrique.Echeance(cle: "B", due: new DateOnly(2027, 7, 1)), Scenario.T0);

        var alerte = DetectionAlertes.TravailleursSansSurveillancePlanifiee(Fabrique.Affilie, [proche, lointaine], Aujourdhui, 30).ShouldHaveSingleItem();

        alerte.Type.ShouldBe(TypeAlerte.TravailleurExposeSansSurveillancePlanifiee);
        alerte.PersonneId.ShouldBe(personne);
        alerte.ObligationIds.ShouldBe([proche.Id]);
        alerte.Message.ShouldContain("R1");
    }

    [Fact]
    public void Une_echeance_depassee_est_signalee_comme_telle()
    {
        var retard = Obligation.Creer(Guid.CreateVersion7(), Fabrique.Echeance(due: new DateOnly(2026, 5, 1)), Scenario.T0);

        DetectionAlertes.TravailleursSansSurveillancePlanifiee(Fabrique.Affilie, [retard], Aujourdhui, 30).ShouldHaveSingleItem()
            .Message.ShouldContain("dépassée");
    }

    [Fact]
    public void Une_obligation_planifiee_ou_d_evenement_ne_declenche_pas_d_alerte_de_couverture()
    {
        var planifiee = Fabrique.AuStatut(StatutObligation.Planifie, Fabrique.Echeance(due: new DateOnly(2026, 6, 20)));
        var evenement = Obligation.Creer(
            Guid.CreateVersion7(), Fabrique.Echeance(cle: "R", type: TypeObligation.ExamenReprise, due: new DateOnly(2026, 6, 20), origine: OrigineObligation.Evenement), Scenario.T0);

        DetectionAlertes.TravailleursSansSurveillancePlanifiee(Fabrique.Affilie, [planifiee, evenement], Aujourdhui, 30).ShouldBeEmpty();
    }

    [Fact]
    public void Un_travailleur_absent_au_rendez_vous_redevient_a_couvrir()
    {
        var absent = Fabrique.AuStatut(StatutObligation.Absent, Fabrique.Echeance(due: new DateOnly(2026, 6, 20)));

        DetectionAlertes.TravailleursSansSurveillancePlanifiee(Fabrique.Affilie, [absent], Aujourdhui, 30).ShouldHaveSingleItem();
    }

    [Fact]
    public void Un_poste_occupe_sans_profil_de_risques_declenche_une_alerte()
    {
        var sans = Guid.CreateVersion7();
        var avec = Guid.CreateVersion7();
        var affectations = new[]
        {
            new AffectationLocale(Guid.CreateVersion7(), Guid.CreateVersion7(), sans, new DateOnly(2026, 1, 1), null, Scenario.T0),
            new AffectationLocale(Guid.CreateVersion7(), Guid.CreateVersion7(), sans, new DateOnly(2026, 2, 1), null, Scenario.T0),
            new AffectationLocale(Guid.CreateVersion7(), Guid.CreateVersion7(), avec, new DateOnly(2026, 1, 1), null, Scenario.T0),
        };

        var alerte = DetectionAlertes.PostesSansAnalyseRisques(Fabrique.Affilie, affectations, new HashSet<Guid> { avec }).ShouldHaveSingleItem();

        alerte.PosteId.ShouldBe(sans);
        alerte.Message.ShouldContain("2 travailleur(s)");
    }

    [Fact]
    public void Une_liste_nominative_non_revue_depuis_douze_mois_declenche_une_alerte_sur_sa_derniere_version()
    {
        var calendrier = BusinessCalendar.Belgian(2025, 2026);
        var delai = new PolitiquesLegales([]).Duree(CodesParametres.RevueListesNominatives, Aujourdhui);
        var ancienne = new ListeNominativeLocale(Guid.CreateVersion7(), Fabrique.Affilie, "EXPOSES", 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10));
        var recente = new ListeNominativeLocale(Guid.CreateVersion7(), Fabrique.Affilie, "EXPOSES", 2, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 10));
        var revue = new ListeNominativeLocale(Guid.CreateVersion7(), Fabrique.Affilie, "NUIT", 1, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10));
        var autreAffilie = new ListeNominativeLocale(Guid.CreateVersion7(), Guid.CreateVersion7(), "EXPOSES", 1, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 10));

        var alertes = DetectionAlertes.ListesNonRevues(Fabrique.Affilie, [ancienne, recente, revue, autreAffilie], delai, calendrier, Aujourdhui).ToList();

        var alerte = alertes.ShouldHaveSingleItem();
        alerte.TypeListe.ShouldBe("EXPOSES");
        alerte.Depuis.ShouldBe(new DateOnly(2025, 5, 10));
        alerte.Message.ShouldContain("version 2");
    }
}
