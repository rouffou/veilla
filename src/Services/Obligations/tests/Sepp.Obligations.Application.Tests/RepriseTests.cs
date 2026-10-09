using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.BffEmployeur;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Obligations.Application.Consultation;
using Sepp.Obligations.Application.Projections;
using Sepp.Obligations.Application.Reprises;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Reprises;

using Shouldly;

namespace Sepp.Obligations.Application.Tests;

/// <summary>ARC-33, POR-04 : orchestration du processus de reprise (annonce, compensation, jalons, minuteries, permissions).</summary>
public class RepriseTests
{
    private static readonly DateOnly DateReprise = new(2026, 6, 15);
    private static readonly DateOnly DebutAbsence = new(2026, 5, 1);

    private readonly Banc _banc = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private InMemoryStore Store => _banc.Store;

    private static FakeUser Gestionnaire => new("gest-1", Roles.GestionnaireDossiers);

    private EnregistrerRepriseHandler Annoncer(ICurrentUser? user = null, IPerimetreAffilies? perimetre = null) =>
        new(_banc.Enregistrement, perimetre ?? FakePerimetre.Interne, user ?? Gestionnaire, _banc.Clock);

    private AnnulerRepriseHandler Annuler(ICurrentUser? user = null, IPerimetreAffilies? perimetre = null) =>
        new(Store, Store, _banc.MiseAJour, Store, _banc.OptionsReprise, perimetre ?? FakePerimetre.Interne, user ?? Gestionnaire, _banc.Clock);

    private async Task<ResultatEnregistrement> Annonce(DateOnly? debutAbsence = null, DateOnly? dateReprise = null)
    {
        var resultat = await Annoncer().HandleAsync(
            new EnregistrerReprise(_banc.Personne, _banc.Affilie, dateReprise ?? DateReprise, debutAbsence ?? DebutAbsence), Ct);
        resultat.IsSuccess.ShouldBeTrue(resultat.Error?.Message);
        return resultat.Value;
    }

    private Obligation Obligation => Store.Obligations.Single(o => o.Type == TypeObligation.ExamenReprise);

    private ProcessusReprise Processus => Store.Processus.Single(p => !p.EstAnnulee);

    private Task RendezVousPlanifie(Guid rendezVousId, DateTimeOffset debut, int minute = 0) =>
        new RendezVousPlanifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new RendezVousPlanifie(rendezVousId, _banc.Personne, _banc.Affilie, debut, [Obligation.Id]) { OccurredAt = _banc.Clock.GetUtcNow().AddMinutes(minute) }, Ct);

    private Task Examen(Guid examenId, DateOnly date) =>
        new ExamenClotureHandler(Store, _banc.MiseAJour).HandleAsync(
            new ExamenCloture(examenId, _banc.Personne, _banc.Affilie, TypesExamen.ExamenReprise, date), Ct);

    private Task Decision(Guid decisionId, Guid? examenId, DateTimeOffset quand) =>
        new DecisionEmiseHandler(Store, Store, _banc.Synchronisation, Store).HandleAsync(
            new DecisionEmise(decisionId, _banc.Personne, _banc.Affilie, "Apte", [], null, examenId) { OccurredAt = quand }, Ct);

    [Fact]
    public async Task Une_annonce_cree_le_processus_l_obligation_et_publie_RepriseEnregistree()
    {
        var resultat = await Annonce();

        resultat.Cree.ShouldBeTrue();
        var processus = Processus;
        processus.Id.ShouldBe(resultat.RepriseId);
        processus.Statut.ShouldBe(StatutReprise.ObligationOuverte);
        processus.ObligationId.ShouldBe(Obligation.Id);
        processus.DateLimite.ShouldBe(Obligation.DateLimite);
        processus.Origine.ShouldBe(OrigineReprise.Interne);
        var evenement = Store.Publies<RepriseEnregistree>().ShouldHaveSingleItem();
        evenement.RepriseId.ShouldBe(processus.Id);
        evenement.Statut.ShouldBe("Enregistree");
        evenement.Origine.ShouldBe("Interne");
        Store.Publies<ObligationCreee>().ShouldHaveSingleItem().TypeExamen.ShouldBe(TypesExamen.ExamenReprise);
        Store.Reprises.Single().RepriseId.ShouldBe(processus.Id);
    }

    [Fact]
    public async Task Une_annonce_en_double_renvoie_le_meme_processus_sans_nouvel_evenement()
    {
        var premiere = await Annonce();
        var publies = Store.Published.Count;

        var seconde = await Annonce();

        seconde.RepriseId.ShouldBe(premiere.RepriseId);
        seconde.Cree.ShouldBeFalse();
        seconde.Modifie.ShouldBeFalse();
        Store.Processus.Count.ShouldBe(1);
        Store.Published.Count.ShouldBe(publies);
    }

    [Fact]
    public async Task Une_annonce_dont_seul_le_debut_d_absence_differe_est_une_modification()
    {
        var premiere = await Annonce();

        var seconde = await Annonce(debutAbsence: new DateOnly(2026, 5, 4));

        seconde.RepriseId.ShouldBe(premiere.RepriseId);
        seconde.Modifie.ShouldBeTrue();
        Processus.DebutAbsence.ShouldBe(new DateOnly(2026, 5, 4));
        Store.Publies<RepriseEnregistree>().Select(e => e.Statut).ShouldBe(["Enregistree", "Modifiee"]);
        Store.Obligations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Une_modification_qui_ramene_l_absence_sous_quatre_semaines_donne_ExamenNonRequis_et_annule_l_obligation()
    {
        await Annonce();

        await Annonce(debutAbsence: new DateOnly(2026, 6, 10));

        Processus.Statut.ShouldBe(StatutReprise.ExamenNonRequis);
        Obligation.Statut.ShouldBe(StatutObligation.Annule);
        Store.Publies<RepriseEnregistree>().Last().Statut.ShouldBe("NonRequise");
        Store.Publies<ObligationCloturee>().ShouldHaveSingleItem().Statut.ShouldBe("Annule");
    }

    [Fact]
    public async Task Une_absence_de_moins_de_quatre_semaines_donne_ExamenNonRequis_sans_obligation()
    {
        var resultat = await Annonce(debutAbsence: new DateOnly(2026, 6, 8));

        Store.Obligations.ShouldBeEmpty();
        Processus.Statut.ShouldBe(StatutReprise.ExamenNonRequis);
        resultat.Statut.ShouldBe(StatutReprise.ExamenNonRequis);
        Store.Publies<RepriseEnregistree>().Select(e => e.Statut).ShouldBe(["Enregistree", "NonRequise"]);
        Store.Publies<ObligationCreee>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_annonce_invalide_est_refusee()
    {
        var resultat = await Annoncer().HandleAsync(new EnregistrerReprise(_banc.Personne, _banc.Affilie, DateReprise, DateReprise.AddDays(1)), Ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Validation);
        Store.Processus.ShouldBeEmpty();
    }

    [Fact]
    public async Task L_annulation_compense_l_obligation_publie_ObligationCloturee_et_RepriseEnregistree_Annulee()
    {
        var annonce = await Annonce();

        var resultat = await Annuler().HandleAsync(new AnnulerReprise(annonce.RepriseId, "ErreurDeSaisie"), Ct);

        resultat.IsSuccess.ShouldBeTrue();
        Store.Processus.Single().Statut.ShouldBe(StatutReprise.Annulee);
        Store.Processus.Single().MotifAnnulation.ShouldBe("ErreurDeSaisie");
        Obligation.Statut.ShouldBe(StatutObligation.Annule);
        var cloture = Store.Publies<ObligationCloturee>().ShouldHaveSingleItem();
        cloture.ObligationId.ShouldBe(Obligation.Id);
        cloture.Statut.ShouldBe("Annule");
        cloture.TypeExamen.ShouldBe(TypesExamen.ExamenReprise);
        Store.Publies<RepriseEnregistree>().Last().Statut.ShouldBe("Annulee");
        Store.Reprises.Single().Annulee.ShouldBeTrue();
    }

    [Fact]
    public async Task Une_annulation_repetee_est_sans_effet()
    {
        var annonce = await Annonce();
        await Annuler().HandleAsync(new AnnulerReprise(annonce.RepriseId, null), Ct);
        var publies = Store.Published.Count;

        (await Annuler().HandleAsync(new AnnulerReprise(annonce.RepriseId, null), Ct)).IsSuccess.ShouldBeTrue();

        Store.Published.Count.ShouldBe(publies);
    }

    [Fact]
    public async Task L_annulation_apres_la_cloture_de_l_examen_est_un_conflit()
    {
        var annonce = await Annonce();
        await Examen(Guid.CreateVersion7(), DateReprise.AddDays(2));

        var resultat = await Annuler().HandleAsync(new AnnulerReprise(annonce.RepriseId, null), Ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        Processus.Statut.ShouldBe(StatutReprise.ExamenRealise);
        Obligation.Statut.ShouldBe(StatutObligation.Realise);
    }

    [Fact]
    public async Task Une_nouvelle_annonce_apres_l_annulation_cree_un_nouveau_processus_et_rouvre_l_obligation()
    {
        var premiere = await Annonce();
        await Annuler().HandleAsync(new AnnulerReprise(premiere.RepriseId, null), Ct);

        var seconde = await Annonce();

        seconde.RepriseId.ShouldNotBe(premiere.RepriseId);
        seconde.Cree.ShouldBeTrue();
        Store.Processus.Count.ShouldBe(2);
        Obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        Store.Reprises.Single().RepriseId.ShouldBe(seconde.RepriseId);
        Store.Reprises.Single().Annulee.ShouldBeFalse();
    }

    [Fact]
    public async Task L_examen_clos_realise_l_obligation_publie_la_cloture_et_la_decision_termine_le_processus()
    {
        await Annonce();
        var examen = Guid.CreateVersion7();

        await Examen(examen, DateReprise.AddDays(2));
        Processus.Statut.ShouldBe(StatutReprise.ExamenRealise);
        Store.Publies<ObligationCloturee>().ShouldHaveSingleItem().Statut.ShouldBe("Realise");

        await Decision(Guid.CreateVersion7(), examen, _banc.Clock.GetUtcNow());

        Processus.Statut.ShouldBe(StatutReprise.Terminee);
        Processus.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public async Task Une_decision_recue_avant_l_examen_est_parquee_puis_appliquee()
    {
        await Annonce();
        var examen = Guid.CreateVersion7();
        var decision = Guid.CreateVersion7();

        await Decision(decision, examen, _banc.Clock.GetUtcNow());
        Store.Decisions.ShouldHaveSingleItem().DecisionId.ShouldBe(decision);
        Processus.DecisionId.ShouldBeNull();

        await Examen(examen, DateReprise.AddDays(2));

        Processus.DecisionId.ShouldBe(decision);
        Processus.Statut.ShouldBe(StatutReprise.Terminee);
    }

    [Fact]
    public async Task La_decision_la_plus_recente_remplace_la_precedente_et_une_decision_sans_examen_est_ignoree()
    {
        await Annonce();
        var examen = Guid.CreateVersion7();
        var ancienne = Guid.CreateVersion7();
        var recente = Guid.CreateVersion7();
        var maintenant = _banc.Clock.GetUtcNow();

        await Decision(recente, examen, maintenant.AddMinutes(5));
        await Decision(ancienne, examen, maintenant);
        await Decision(Guid.CreateVersion7(), null, maintenant.AddMinutes(10));
        await Examen(examen, DateReprise.AddDays(2));

        Store.Decisions.ShouldHaveSingleItem().DecisionId.ShouldBe(recente);
        Processus.DecisionId.ShouldBe(recente);
    }

    [Fact]
    public async Task Le_document_de_la_decision_destine_a_l_employeur_est_retenu()
    {
        await Annonce();
        var examen = Guid.CreateVersion7();
        var decision = Guid.CreateVersion7();
        await Examen(examen, DateReprise.AddDays(2));
        await Decision(decision, examen, _banc.Clock.GetUtcNow());
        var handler = new DocumentPublieHandler(Store, Store);

        await handler.HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "Standard", "Personne", _banc.Personne, "DECISION", "decision", decision), Ct);
        Processus.DocumentEmployeurId.ShouldBeNull();

        var document = Guid.CreateVersion7();
        await handler.HandleAsync(new DocumentPublie(document, "Standard", "Affilie", _banc.Affilie, "DECISION", "decision", decision), Ct);
        await handler.HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "Standard", "Affilie", _banc.Affilie, "AUTRE", null, null), Ct);

        Processus.DocumentEmployeurId.ShouldBe(document);
    }

    [Fact]
    public async Task Un_rendez_vous_puis_une_convocation_font_passer_le_processus_a_convoquee()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        var debut = _banc.Clock.GetUtcNow().AddDays(5);
        await RendezVousPlanifie(rendezVous, debut);
        Processus.Statut.ShouldBe(StatutReprise.Planifiee);

        await new ConvocationEnvoyeeHandler(Store, _banc.MiseAJour).HandleAsync(
            new ConvocationEnvoyee(Guid.CreateVersion7(), rendezVous, _banc.Personne, _banc.Affilie, [Obligation.Id], "Courrier", true, _banc.Clock.GetUtcNow().AddMinutes(10)), Ct);

        Obligation.Statut.ShouldBe(StatutObligation.Convoque);
        Processus.Statut.ShouldBe(StatutReprise.Convoquee);
        Processus.ConvocationEnvoyeeLe.ShouldNotBeNull();
    }

    [Fact]
    public async Task Une_convocation_recue_avant_le_rendez_vous_convoque_quand_il_arrive()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        await new ConvocationEnvoyeeHandler(Store, _banc.MiseAJour).HandleAsync(
            new ConvocationEnvoyee(Guid.CreateVersion7(), rendezVous, _banc.Personne, _banc.Affilie, [Obligation.Id], "Courrier", true, _banc.Clock.GetUtcNow().AddMinutes(10)), Ct);

        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));

        Obligation.Statut.ShouldBe(StatutObligation.Convoque);
        Processus.Statut.ShouldBe(StatutReprise.Convoquee);
    }

    [Fact]
    public async Task Une_convocation_non_remise_leve_une_alerte_et_la_replanification_automatique_demande_l_urgence_si_activee()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));

        await new ConvocationNonRemiseHandler(Store, _banc.MiseAJour).HandleAsync(
            new ConvocationNonRemise(Guid.CreateVersion7(), rendezVous, _banc.Personne, _banc.Affilie, [Obligation.Id], "Email", false, _banc.Clock.GetUtcNow().AddMinutes(10)), Ct);

        Processus.ConvocationNonRemise.ShouldBeTrue();
        Processus.Alertes().ShouldContain(a => a.Type == Domain.Calcul.TypeAlerte.RepriseConvocationNonRemise);
        Store.Publies<PlanificationUrgenteDemandee>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_absence_passe_l_obligation_a_absent_leve_une_alerte_et_ne_demande_pas_l_urgence_par_defaut()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));

        await new AbsenceRendezVousConstateeHandler(Store, _banc.MiseAJour).HandleAsync(
            new AbsenceRendezVousConstatee(rendezVous, _banc.Personne, _banc.Affilie, _banc.Clock.GetUtcNow().AddDays(5), [Obligation.Id]), Ct);

        Obligation.Statut.ShouldBe(StatutObligation.Absent);
        Processus.NombreAbsences.ShouldBe(1);
        Processus.ReplanificationRequise.ShouldBeTrue();
        Store.Publies<PlanificationUrgenteDemandee>().ShouldBeEmpty();
        var alertes = await new ListerAlertesHandler(Store, Store, _banc.Recalcul, FakePerimetre.Interne, Gestionnaire, _banc.Clock, _banc.Options, Store)
            .HandleAsync(new ListerAlertes(_banc.Affilie), Ct);
        alertes.Value.ShouldContain(a => a.Type == "RepriseReplanificationRequise" && a.PersonneId == _banc.Personne);
    }

    [Fact]
    public async Task Une_absence_demande_la_planification_urgente_si_la_replanification_automatique_est_activee()
    {
        _banc.OptionsReprise.ReplanificationAutomatique = true;
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));

        await new AbsenceRendezVousConstateeHandler(Store, _banc.MiseAJour).HandleAsync(
            new AbsenceRendezVousConstatee(rendezVous, _banc.Personne, _banc.Affilie, _banc.Clock.GetUtcNow().AddDays(5), [Obligation.Id]), Ct);

        var demande = Store.Publies<PlanificationUrgenteDemandee>().ShouldHaveSingleItem();
        demande.Motif.ShouldBe("Absence");
        demande.ObligationId.ShouldBe(Obligation.Id);
        demande.RendezVousId.ShouldBe(rendezVous);
        demande.TypeExamen.ShouldBe(TypesExamen.ExamenReprise);
        demande.DateLimite.ShouldBe(Obligation.DateLimite!.Value);
    }

    [Fact]
    public async Task Une_absence_recue_deux_fois_ou_avant_le_rendez_vous_donne_le_meme_resultat()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        var absence = new AbsenceRendezVousConstatee(rendezVous, _banc.Personne, _banc.Affilie, _banc.Clock.GetUtcNow().AddDays(5), [Obligation.Id]);
        var handler = new AbsenceRendezVousConstateeHandler(Store, _banc.MiseAJour);

        await handler.HandleAsync(absence, Ct);
        await handler.HandleAsync(absence, Ct);
        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));

        Obligation.Statut.ShouldBe(StatutObligation.Absent);
        Processus.NombreAbsences.ShouldBe(1);
    }

    [Fact]
    public async Task Un_rendez_vous_annule_hors_compensation_demande_une_replanification()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));

        await new RendezVousAnnuleHandler(Store, _banc.MiseAJour).HandleAsync(new RendezVousAnnule(rendezVous, _banc.Personne, "Demande"), Ct);

        Obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        Processus.ReplanificationRequise.ShouldBeTrue();
        Processus.MotifReplanification.ShouldBe(MotifUrgence.AnnulationRendezVous);
    }

    [Fact]
    public async Task Un_rendez_vous_replanifie_met_a_jour_la_date_et_une_date_au_dela_de_la_limite_leve_une_alerte()
    {
        await Annonce();
        var rendezVous = Guid.CreateVersion7();
        await RendezVousPlanifie(rendezVous, _banc.Clock.GetUtcNow().AddDays(5));
        var apresLimite = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);

        await new RendezVousReplanifieHandler(Store, _banc.MiseAJour).HandleAsync(
            new RendezVousReplanifie(rendezVous, _banc.Personne, _banc.Affilie, _banc.Clock.GetUtcNow().AddDays(5), apresLimite, "AbsenceRessource")
            { OccurredAt = _banc.Clock.GetUtcNow().AddHours(1) }, Ct);

        Obligation.DateRendezVous.ShouldBe(apresLimite);
        Processus.DebutRendezVous.ShouldBe(apresLimite);
        Processus.Alertes().ShouldContain(a => a.Type == Domain.Calcul.TypeAlerte.RepriseRendezVousApresDateLimite);
    }

    [Fact]
    public async Task Une_urgence_non_couverte_donne_le_statut_NonCouverte()
    {
        await Annonce();

        await new UrgenceNonCouverteHandler(Store, _banc.Synchronisation, Store).HandleAsync(
            new UrgenceNonCouverte(Obligation.Id, _banc.Personne, _banc.Affilie, TypesExamen.ExamenReprise, Obligation.DateLimite!.Value), Ct);

        Processus.Statut.ShouldBe(StatutReprise.NonCouverte);
    }

    [Fact]
    public async Task La_sortie_de_l_entreprise_rend_le_processus_sans_objet_et_publie_la_cloture()
    {
        await Annonce();
        var occupation = Guid.CreateVersion7();
        await new OccupationDebuteeHandler(Store, _banc.MiseAJour).HandleAsync(
            new Sepp.Contracts.Personnes.OccupationDebutee(occupation, _banc.Personne, _banc.Affilie, new DateOnly(2025, 1, 1)), Ct);
        await new OccupationTermineeHandler(Store, _banc.MiseAJour).HandleAsync(
            new Sepp.Contracts.Personnes.OccupationTerminee(occupation, _banc.Personne, _banc.Affilie, new DateOnly(2026, 6, 10)), Ct);

        Obligation.Statut.ShouldBe(StatutObligation.SortiEntreprise);
        Processus.Statut.ShouldBe(StatutReprise.SansObjet);
        Store.Publies<ObligationCloturee>().ShouldHaveSingleItem().Statut.ShouldBe("SortiEntreprise");
    }

    [Fact]
    public async Task L_annonce_par_evenement_delegue_a_l_enregistrement_avec_l_origine_Evenement()
    {
        var handler = new RepriseAnnonceeHandler(_banc.Enregistrement);
        var annonce = new RepriseAnnoncee(_banc.Personne, _banc.Affilie, DateReprise, DebutAbsence);

        await handler.HandleAsync(annonce, Ct);
        await handler.HandleAsync(annonce, Ct);

        Store.Processus.ShouldHaveSingleItem().Origine.ShouldBe(OrigineReprise.Evenement);
        Store.Publies<RepriseEnregistree>().ShouldHaveSingleItem().Origine.ShouldBe("Evenement");
    }

    [Fact]
    public async Task Les_minuteries_alertent_a_l_approche_de_l_echeance_puis_marquent_hors_delai_avec_le_temps_simule()
    {
        await Annonce();
        var traitement = new TraiterMinuteriesReprise(Store, Store, Store, Store, _banc.OptionsReprise, _banc.Clock);
        Processus.TypeMinuterie.ShouldBe(TypeMinuterie.EcheanceMenacee);
        Processus.ProchaineEcheance.ShouldBe(new DateOnly(2026, 6, 25));

        (await traitement.ExecuterLotAsync(Ct)).ProcessusTraites.ShouldBe(0);

        _banc.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero));
        var premier = await traitement.ExecuterLotAsync(Ct);
        premier.ProcessusTraites.ShouldBe(1);
        premier.MinuteriesDeclenchees.ShouldBe(1);
        Processus.Alertes().ShouldContain(a => a.Type == Domain.Calcul.TypeAlerte.RepriseEcheanceMenacee);
        (await traitement.ExecuterLotAsync(Ct)).ProcessusTraites.ShouldBe(0);

        _banc.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 30, 9, 0, 0, TimeSpan.Zero));
        (await traitement.ExecuterLotAsync(Ct)).MinuteriesDeclenchees.ShouldBe(1);
        Processus.HorsDelai.ShouldBeTrue();
        Processus.EnRetardAu(new DateOnly(2026, 6, 30)).ShouldBeTrue();
        Processus.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public async Task Les_reprises_externes_sont_limitees_au_perimetre_de_l_affilie()
    {
        var employeur = Banc.Employeur;
        var dansPerimetre = new FakePerimetre(true, _banc.Affilie);
        var autre = new FakePerimetre(true, Guid.CreateVersion7());

        (await Annoncer(employeur, autre).HandleAsync(new EnregistrerReprise(_banc.Personne, _banc.Affilie, DateReprise, DebutAbsence), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        var resultat = await Annoncer(employeur, dansPerimetre).HandleAsync(new EnregistrerReprise(_banc.Personne, _banc.Affilie, DateReprise, DebutAbsence), Ct);
        resultat.IsSuccess.ShouldBeTrue();
        Processus.Origine.ShouldBe(OrigineReprise.PortailEmployeur);

        var lecture = new ObtenirRepriseHandler(Store, dansPerimetre, employeur, _banc.Clock);
        (await lecture.HandleAsync(new ObtenirReprise(resultat.Value.RepriseId), Ct)).Value.ExamenId.ShouldBeNull();
        (await new ObtenirRepriseHandler(Store, autre, employeur, _banc.Clock).HandleAsync(new ObtenirReprise(resultat.Value.RepriseId), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);

        var liste = new ListerReprisesHandler(Store, dansPerimetre, employeur, _banc.Clock);
        (await liste.HandleAsync(new ListerReprises(null, null, null), Ct)).Value.ShouldHaveSingleItem();
        (await new ListerReprisesHandler(Store, autre, employeur, _banc.Clock).HandleAsync(new ListerReprises(null, null, null), Ct)).Value.ShouldBeEmpty();
        (await new ListerReprisesHandler(Store, autre, employeur, _banc.Clock).HandleAsync(new ListerReprises(_banc.Affilie, null, null), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Les_permissions_de_reprise_sont_exigees()
    {
        var annonce = await Annonce();
        var commande = new EnregistrerReprise(_banc.Personne, _banc.Affilie, DateReprise, DebutAbsence);

        (await Annoncer(Banc.Cpmt).HandleAsync(commande, Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Annoncer(Banc.Travailleur).HandleAsync(commande, Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Annuler(Banc.Employeur, new FakePerimetre(true, _banc.Affilie)).HandleAsync(new AnnulerReprise(annonce.RepriseId, null), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Annuler(Banc.Cpmt).HandleAsync(new AnnulerReprise(annonce.RepriseId, null), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ObtenirRepriseHandler(Store, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock).HandleAsync(new ObtenirReprise(annonce.RepriseId), Ct)).IsSuccess.ShouldBeTrue();
        (await new ObtenirRepriseHandler(Store, FakePerimetre.Interne, Banc.Travailleur, _banc.Clock).HandleAsync(new ObtenirReprise(annonce.RepriseId), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Annuler().HandleAsync(new AnnulerReprise(annonce.RepriseId, "Inconnu"), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await Annuler().HandleAsync(new AnnulerReprise(Guid.CreateVersion7(), null), Ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task La_liste_se_filtre_par_statut_et_par_echeance()
    {
        await Annonce();
        var liste = new ListerReprisesHandler(Store, FakePerimetre.Interne, Banc.Cpmt, _banc.Clock);

        (await liste.HandleAsync(new ListerReprises(null, StatutReprise.ObligationOuverte, null), Ct)).Value.ShouldHaveSingleItem();
        (await liste.HandleAsync(new ListerReprises(null, StatutReprise.Terminee, null), Ct)).Value.ShouldBeEmpty();
        (await liste.HandleAsync(new ListerReprises(null, null, new DateOnly(2026, 6, 28)), Ct)).Value.ShouldBeEmpty();
        (await liste.HandleAsync(new ListerReprises(null, null, new DateOnly(2026, 6, 29)), Ct)).Value.ShouldHaveSingleItem();
    }

    [Fact]
    public void Les_codes_de_type_d_examen_du_service_sont_des_codes_partages()
    {
        foreach (var type in Enum.GetValues<TypeObligation>())
        {
            TypesExamen.Connus.ShouldContain(type.Code(), $"{type} n'est pas dans Sepp.Contracts.Examens.TypesExamen.");
        }
    }
}
