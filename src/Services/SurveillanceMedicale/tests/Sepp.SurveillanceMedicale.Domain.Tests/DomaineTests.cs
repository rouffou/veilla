using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.SurveillanceMedicale.Domain;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Projections;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Domain.Tests;

public sealed class DossierSanteTests
{
    private static readonly DateOnly Jour = new(2026, 3, 2);

    [Fact]
    public void Le_gestionnaire_et_le_vaccinateur_sont_en_relation_de_soin()
    {
        var dossier = DossierSante.Ouvrir(Guid.CreateVersion7(), "cpmt-1", Jour);
        dossier.EstSuiviPar("cpmt-1").ShouldBeTrue();
        dossier.EstSuiviPar("infirmier-1").ShouldBeFalse();

        dossier.Vacciner("hepatite_b", 1, Jour, null, null, "infirmier-1", null);
        dossier.EstSuiviPar("infirmier-1").ShouldBeTrue();
        dossier.DomainEvents.OfType<VaccinationEnregistree>().Single().VaccinCode.ShouldBe("HEPATITE_B");
    }

    [Fact]
    public void Un_mesurage_n_est_verse_qu_une_fois_et_seulement_pendant_le_rattachement()
    {
        var dossier = DossierSante.Ouvrir(Guid.CreateVersion7(), null, Jour);
        var groupe = Guid.CreateVersion7();
        dossier.RattacherGroupeExposition(groupe, Guid.CreateVersion7(), new Validity(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1)));

        dossier.EstExposeeViaGroupe(groupe, new DateOnly(2026, 2, 1)).ShouldBeTrue();
        dossier.EstExposeeViaGroupe(groupe, new DateOnly(2026, 8, 1)).ShouldBeFalse();

        var mesurage = Guid.CreateVersion7();
        dossier.EnregistrerExposition("BRUIT", "ELEVE", Jour, Jour, mesurage, groupe).ShouldNotBeNull();
        dossier.EnregistrerExposition("BRUIT", "ELEVE", Jour, Jour, mesurage, groupe).ShouldBeNull();
        dossier.Expositions.Count.ShouldBe(1);
    }

    [Fact]
    public void Le_cycle_d_archivage_mene_a_la_purge_proposee_et_une_activite_le_reactive()
    {
        var dossier = DossierSante.Ouvrir(Guid.CreateVersion7(), "cpmt-1", Jour);
        Should.Throw<DomainException>(dossier.VerifierDestructionPossible);

        dossier.Archiver(Jour, Jour.AddYears(15));
        dossier.PeutEtreProposeeALaPurge(Jour.AddYears(14)).ShouldBeFalse();
        Should.Throw<DomainException>(() => dossier.ProposerPurge(Jour.AddYears(14)));

        dossier.ProposerPurge(Jour.AddYears(15));
        dossier.StatutArchivage.ShouldBe(StatutArchivage.PurgeProposee);
        dossier.VerifierDestructionPossible();

        dossier.AjouterPieceJointe(Guid.CreateVersion7(), CategoriePieceJointe.Imagerie, "Radiographie", null, Jour, Jour);
        dossier.StatutArchivage.ShouldBe(StatutArchivage.Actif);
        dossier.DatePurgePrevue.ShouldBeNull();
    }

    [Fact]
    public void Un_test_tuberculinique_se_lit_dans_les_sept_jours_et_une_seule_fois()
    {
        var dossier = DossierSante.Ouvrir(Guid.CreateVersion7(), null, Jour);
        var test = dossier.PoserTestTuberculinique(Jour, "infirmier-1");
        Should.Throw<DomainException>(() => dossier.LireTestTuberculinique(test.Id, Jour.AddDays(9), LectureTuberculinique.Creer(ResultatTuberculinique.Negatif, 2)));
        dossier.LireTestTuberculinique(test.Id, Jour.AddDays(3), LectureTuberculinique.Creer(ResultatTuberculinique.Negatif, 2));
        Should.Throw<DomainException>(() => dossier.LireTestTuberculinique(test.Id, Jour.AddDays(3), LectureTuberculinique.Creer(ResultatTuberculinique.Positif, 12)));
    }

    [Fact]
    public void Un_code_n_accepte_pas_de_texte_libre() =>
        Should.Throw<DomainException>(() => Garde.Code("pas de port de charges", "mesure"));
}

public sealed class ExamenTests
{
    private const string EvaluationPeriodique = "EVALUATION_PERIODIQUE";

    private static readonly DateOnly Jour = new(2026, 3, 2);

    private static Examen Nouveau(string type = EvaluationPeriodique) =>
        Examen.Ouvrir(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), type, Jour, "cpmt-1", null, []);

    private static readonly ValeurReference[] References =
    [
        ValeurReference.Definir(TypeActe.Audiometrie, "AUDIO.PERTE_4000_OD", new LocalizedLabel("a", "b", "c"), "dB", null, 25m, new DateOnly(2026, 1, 1)),
    ];

    [Fact]
    public void Un_resultat_inhabituel_declenche_une_proposition_d_augmenter_la_frequence()
    {
        var examen = Nouveau();
        var normales = EvaluateurResultats.Evaluer(TypeActe.Audiometrie, [new Mesure("audio.perte_4000_od", 10m, null, null)], References, Jour);
        examen.EnregistrerActe(TypeActe.Audiometrie, normales, null, SourceResultat.Saisie, Jour).Inhabituel.ShouldBeFalse();
        examen.PropositionsFrequence.ShouldBeEmpty();

        var evaluees = EvaluateurResultats.Evaluer(TypeActe.Audiometrie, [new Mesure("AUDIO.PERTE_4000_OD", 40m, null, null)], References, Jour);
        evaluees.Single().ShouldSatisfyAllConditions(m => m.Inhabituelle.ShouldBeTrue(), m => m.ReferenceMax.ShouldBe(25m), m => m.Unite.ShouldBe("dB"));
        var resultat = examen.EnregistrerActe(TypeActe.Audiometrie, evaluees, null, SourceResultat.ImportAppareil, Jour);

        var proposition = examen.PropositionsFrequence.Single();
        proposition.ResultatActeId.ShouldBe(resultat.Id);
        examen.DeciderProposition(proposition.Id, true, "cpmt-1", Jour);
        proposition.Statut.ShouldBe(StatutProposition.Acceptee);
        Should.Throw<DomainException>(() => examen.DeciderProposition(proposition.Id, false, "cpmt-1", Jour));
    }

    [Fact]
    public void Un_examen_cloture_publie_son_type_et_sa_date_et_ne_se_modifie_plus()
    {
        var examen = Nouveau();
        examen.SaisirObservation("Anamnèse sans particularité", null, DateTimeOffset.UtcNow);
        examen.Cloturer(Jour, null);

        var evenement = examen.DomainEvents.OfType<ExamenClotureLocal>().Single();
        evenement.TypeExamen.ShouldBe(EvaluationPeriodique);
        Should.Throw<DomainException>(() => examen.SaisirObservation("ajout", null, DateTimeOffset.UtcNow));
    }
}

public sealed class ObligationDueTests
{
    private const string CodeReprise = "EXAMEN_REPRISE";

    private static readonly DateTimeOffset Instant = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    // Reprise le jeudi 30 avril 2026 ; date limite calculée par Obligations (10 jours ouvrables) : lundi 18 mai.
    private static ObligationDue Reprise(DateOnly? limite) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), CodeReprise, new DateOnly(2026, 4, 30), limite);

    [Theory]
    [InlineData("2026-04-29", true)]
    [InlineData("2026-04-30", false)]
    [InlineData("2026-05-18", false)]
    [InlineData("2026-05-19", true)]
    public void Le_hors_delai_se_lit_dans_l_intervalle_date_due_date_limite_bornes_incluses(string dateExamen, bool horsDelai) =>
        Reprise(new DateOnly(2026, 5, 18)).EstHorsDelai(DateOnly.Parse(dateExamen, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(horsDelai);

    [Fact]
    public void Sans_date_limite_le_respect_du_delai_est_indetermine() =>
        Reprise(null).EstHorsDelai(new DateOnly(2026, 6, 1)).ShouldBeNull();

    [Fact]
    public void Le_retrait_est_idempotent_et_seule_une_creation_plus_recente_le_leve()
    {
        var obligation = Reprise(new DateOnly(2026, 5, 18));
        obligation.Retirer("Annule", Instant).ShouldBeTrue();
        obligation.Retirer("Annule", Instant).ShouldBeFalse();
        obligation.Retirer("SortiEntreprise", Instant.AddMinutes(-1)).ShouldBeFalse();
        obligation.StatutRetrait.ShouldBe("Annule");

        obligation.Appliquer(obligation.PersonneId, obligation.AffilieId, CodeReprise, obligation.DateDue, obligation.DateLimite, Instant.AddMinutes(-5));
        obligation.EstRetiree.ShouldBeTrue();

        obligation.Appliquer(obligation.PersonneId, obligation.AffilieId, CodeReprise, obligation.DateDue, obligation.DateLimite, Instant.AddMinutes(5));
        obligation.ShouldSatisfyAllConditions(o => o.EstRetiree.ShouldBeFalse(), o => o.StatutRetrait.ShouldBeNull());
    }
}

public sealed class DecisionTests
{
    private const string EvaluationPeriodique = "EVALUATION_PERIODIQUE";

    private static readonly DateOnly Jour = new(2026, 3, 2);

    private static Decision Rediger(ContenuDecision contenu) =>
        Decision.Rediger(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), EvaluationPeriodique, Jour, "cpmt-1", contenu);

    private static PolitiqueRecours Politique => new(BusinessCalendar.Belgian(2026), new DelaisRecours());

    [Theory]
    [InlineData(CategorieDecision.Apte, "PROTECTION_AUDITIVE", null)]
    [InlineData(CategorieDecision.ApteAvecMesures, null, null)]
    [InlineData(CategorieDecision.InaptitudeTemporaire, null, null)]
    [InlineData(CategorieDecision.EcartementMaternite, null, null)]
    [InlineData(CategorieDecision.InaptitudeDefinitive, null, "2026-06-01")]
    public void Les_regles_de_categorie_sont_appliquees(CategorieDecision categorie, string? mesure, string? valide)
    {
        var contenu = new ContenuDecision(categorie, mesure is null ? [] : [mesure], valide is null ? null : DateOnly.Parse(valide, System.Globalization.CultureInfo.InvariantCulture), null, null);
        Should.Throw<DomainException>(() => Rediger(contenu));
    }

    [Fact]
    public void Seul_l_auteur_signe_et_la_signature_transmet_categorie_mesures_et_validite()
    {
        var decision = Rediger(new ContenuDecision(CategorieDecision.ApteAvecMesures, ["pas_travail_nuit"], Jour.AddMonths(12), "Motif médical confidentiel", "Conseils"));
        Should.Throw<DomainException>(() => decision.Signer("cpmt-2", "REF", DateTimeOffset.UtcNow));

        decision.Signer("cpmt-1", "REF-1", new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero));
        decision.Statut.ShouldBe(StatutDecision.Emise);
        var transmise = decision.DomainEvents.OfType<DecisionTransmise>().Single();
        transmise.Mesures.ShouldBe(["PAS_TRAVAIL_NUIT"]);
        transmise.ExamenId.ShouldBe(decision.ExamenId);
        CodesDecision.Code(transmise.Categorie).ShouldBe("APTE_AVEC_MESURES");
        Should.Throw<DomainException>(() => decision.Modifier(new ContenuDecision(CategorieDecision.Apte, [], null, null, null)));
    }

    [Fact]
    public void Un_recours_tardif_est_signale_et_une_issue_reformee_retransmet_la_decision()
    {
        var decision = Rediger(new ContenuDecision(CategorieDecision.InaptitudeDefinitive, [], null, "Justification", null));
        Should.Throw<DomainException>(() => decision.IntroduireRecours(TypeRecours.RecoursMedecinInspecteur, Jour, Politique));

        decision.Signer("cpmt-1", "REF-1", new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero));
        decision.ClearDomainEvents();

        // Remise le mardi 3 mars : 7 jours ouvrables → jeudi 12 mars.
        var recours = decision.IntroduireRecours(TypeRecours.RecoursMedecinInspecteur, new DateOnly(2026, 3, 13), Politique);
        recours.DateLimiteIntroduction.ShouldBe(new DateOnly(2026, 3, 12));
        recours.IntroduitDansLeDelai.ShouldBeFalse();
        Should.Throw<DomainException>(() => decision.IntroduireRecours(TypeRecours.RecoursMedecinInspecteur, new DateOnly(2026, 3, 13), Politique));

        Should.Throw<DomainException>(() => decision.EnregistrerIssue(recours.Id, IssueRecours.Reformee, new DateOnly(2026, 4, 1), null, null));
        decision.EnregistrerIssue(recours.Id, IssueRecours.Reformee, new DateOnly(2026, 4, 1),
            new ContenuDecision(CategorieDecision.Mutation, ["MUTATION_POSTE_SANS_BRUIT"], null, null, null), "Décision du médecin-inspecteur");

        decision.Categorie.ShouldBe(CategorieDecision.Mutation);
        decision.Justification.ShouldBe("Justification");
        var retransmise = decision.DomainEvents.OfType<DecisionTransmise>().Single();
        retransmise.ShouldSatisfyAllConditions(
            t => t.Categorie.ShouldBe(CategorieDecision.Mutation),
            t => t.DecisionId.ShouldBe(decision.Id),
            t => t.ExamenId.ShouldBe(decision.ExamenId));
    }
}

public sealed class VaccinationsEtConservationTests
{
    private static readonly DateOnly Jour = new(2026, 3, 2);

    [Fact]
    public void Un_lot_perime_ou_epuise_ne_peut_plus_etre_administre()
    {
        Should.Throw<DomainException>(() => LotVaccin.Receptionner(Guid.CreateVersion7(), "GRIPPE", "L1", Jour, 10, Jour));
        var lot = LotVaccin.Receptionner(Guid.CreateVersion7(), "GRIPPE", "l1", Jour.AddDays(30), 1, Jour);
        Should.Throw<DomainException>(() => lot.Consommer("TETANOS", Jour));
        lot.Consommer("GRIPPE", Jour);
        Should.Throw<DomainException>(() => lot.Consommer("GRIPPE", Jour));
        Should.Throw<DomainException>(() => lot.Ajuster(5));
        lot.Ajuster(1);
        Should.Throw<DomainException>(() => lot.Consommer("GRIPPE", Jour.AddDays(30)));
    }

    [Fact]
    public void Les_rappels_suivent_le_schema_de_base_puis_la_periodicite()
    {
        var schema = SchemaVaccinal.Definir("TETANOS", new LocalizedLabel("t", "t", "t"), ["EX.BIO.TETANOS"], 3, [1, 6], 120);
        var dossier = DossierSante.Ouvrir(Guid.CreateVersion7(), null, Jour);

        CalculRappels.Calculer([schema], [], dossier, Jour).ShouldBeEmpty();
        CalculRappels.Calculer([schema], ["EX.BIO.TETANOS"], dossier, Jour).Single().DoseSuivante.ShouldBe(1);

        dossier.Vacciner("TETANOS", 1, Jour, null, null, "inf", null);
        var deuxieme = CalculRappels.Calculer([schema], [], dossier, Jour).Single();
        deuxieme.ShouldSatisfyAllConditions(r => r.DoseSuivante.ShouldBe(2), r => r.DateDue.ShouldBe(Jour.AddMonths(1)), r => r.SchemaDeBase.ShouldBeTrue());

        dossier.Vacciner("TETANOS", 2, Jour.AddMonths(1), null, null, "inf", null);
        dossier.Vacciner("TETANOS", 3, Jour.AddMonths(7), null, null, "inf", null);
        var rappel = CalculRappels.Calculer([schema], [], dossier, Jour.AddYears(11)).Single();
        rappel.ShouldSatisfyAllConditions(r => r.SchemaDeBase.ShouldBeFalse(), r => r.DateDue.ShouldBe(Jour.AddMonths(7).AddMonths(120)), r => r.EnRetard.ShouldBeTrue());
    }

    [Fact]
    public void La_conservation_est_d_au_moins_quinze_ans_et_allongee_par_les_expositions()
    {
        var durees = new[] { DureeConservationExposition.Definir("AMIANTE", 40, "base"), DureeConservationExposition.Definir("CANCERIGENE", 30, "base") };
        PolitiqueConservationDossier.DureeAnnees(10, ["BRUIT"], durees).ShouldBe(15);
        PolitiqueConservationDossier.DureeAnnees(20, ["BRUIT"], durees).ShouldBe(20);
        PolitiqueConservationDossier.DureeAnnees(15, ["CANCERIGENE.BENZENE", "AMIANTE"], durees).ShouldBe(40);
        PolitiqueConservationDossier.DureeAnnees(15, ["CANCERIGENEX"], durees).ShouldBe(15);
        PolitiqueConservationDossier.DatePurge(Jour, 15, [], durees).ShouldBe(Jour.AddYears(15));
        Should.Throw<DomainException>(() => DureeConservationExposition.Definir("X", 10, "base"));
    }

    [Fact]
    public void La_destruction_exige_une_validation_humaine() =>
        Should.Throw<DomainException>(() => PreuveDestruction.Etablir(Guid.CreateVersion7(), Guid.CreateVersion7(), Jour, DateTimeOffset.UtcNow, "system", "auto", 1, "AB"));

    [Fact]
    public void Un_questionnaire_est_controle_par_son_modele()
    {
        var modele = ModeleQuestionnaire.Creer("Q", 1, new LocalizedLabel("q", "q", "q"),
        [
            new QuestionModele("FUME", new LocalizedLabel("f", "f", "f"), TypeReponse.OuiNon, true, []),
            new QuestionModele("HEURES", new LocalizedLabel("h", "h", "h"), TypeReponse.Nombre, false, []),
        ]);
        modele.VerifierReponses([new ReponseQuestionnaire("FUME", "NON"), new ReponseQuestionnaire("HEURES", "7.5")]);
        Should.Throw<DomainException>(() => modele.VerifierReponses([new ReponseQuestionnaire("HEURES", "7")]));
        Should.Throw<DomainException>(() => modele.VerifierReponses([new ReponseQuestionnaire("FUME", "PEUT-ETRE")]));
        Should.Throw<DomainException>(() => modele.VerifierReponses([new ReponseQuestionnaire("FUME", "OUI"), new ReponseQuestionnaire("INCONNUE", "x")]));
    }
}
