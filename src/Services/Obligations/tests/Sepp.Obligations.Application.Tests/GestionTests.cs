using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Obligations;
using Sepp.Obligations.Application.Gestion;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

using Shouldly;

namespace Sepp.Obligations.Application.Tests;

/// <summary>SAN-02 (statuts), demandes du travailleur, recalcul à la demande et traitement périodique (ObligationEchue).</summary>
public class GestionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Banc _banc = new();

    private InMemoryStore Store => _banc.Store;

    private Obligation Ajouter(string cle = "A", DateOnly? due = null, DateOnly? limite = null, TypeObligation type = TypeObligation.EvaluationPeriodique)
    {
        var date = due ?? new DateOnly(2026, 7, 1);
        var obligation = Obligation.Creer(
            _banc.Personne,
            new EcheanceCalculee(
                cle, _banc.Affilie, type, OrigineObligation.Regle, ["R1"], date, limite ?? date, null,
                new Justification("regle-surveillance:R1", 1, "Calcul", [])),
            Banc.Midi);
        Store.Obligations.Add(obligation);
        return obligation;
    }

    private ChangerStatutObligationHandler Statut(ICurrentUser? user = null) => new(Store, Store, Store, user ?? Banc.Cpmt, _banc.Clock);

    [Fact]
    public async Task Le_cpmt_fait_passer_une_obligation_planifiee_a_convoquee_puis_absente()
    {
        var obligation = Ajouter();
        obligation.Planifier(Guid.CreateVersion7(), new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));

        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Convoquer), Ct)).IsSuccess.ShouldBeTrue();
        obligation.Statut.ShouldBe(StatutObligation.Convoque);
        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.MarquerAbsent), Ct)).IsSuccess.ShouldBeTrue();
        obligation.Statut.ShouldBe(StatutObligation.Absent);
        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Excuser), Ct)).IsSuccess.ShouldBeTrue();
        obligation.Statut.ShouldBe(StatutObligation.Excuse);
        Store.Saves.ShouldBe(3);
    }

    [Fact]
    public async Task Une_transition_interdite_est_un_conflit()
    {
        var obligation = Ajouter();

        var resultat = await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Convoquer), Ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        resultat.Error.Code.ShouldBe("obligation.transition-interdite");
        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
    }

    [Fact]
    public async Task Un_employeur_ne_peut_pas_changer_le_statut()
    {
        var obligation = Ajouter();

        var resultat = await Statut(Banc.Employeur).HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Annuler, Motif: "Autre"), Ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Une_obligation_inconnue_est_introuvable()
    {
        (await Statut().HandleAsync(new ChangerStatutObligation(Guid.CreateVersion7(), ActionStatut.Excuser), Ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Recalcul")]
    [InlineData("texte libre sur la sante")]
    public async Task L_annulation_exige_un_motif_sous_forme_de_code_hors_recalcul(string? motif)
    {
        var obligation = Ajouter();

        var resultat = await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Annuler, Motif: motif), Ct);

        resultat.Error!.Code.ShouldBe("obligation.motif-invalide");
    }

    [Fact]
    public async Task L_annulation_par_decision_est_enregistree_avec_son_motif()
    {
        var obligation = Ajouter();

        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Annuler, Motif: "decision_cpmt"), Ct)).IsSuccess.ShouldBeTrue();

        obligation.Statut.ShouldBe(StatutObligation.Annule);
        obligation.MotifAnnulation.ShouldBe(MotifAnnulation.DecisionCpmt);
    }

    [Fact]
    public async Task Le_report_exige_une_date_future()
    {
        var obligation = Ajouter();

        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Reporter), Ct)).Error!.Code.ShouldBe("obligation.date-report-obligatoire");
        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Reporter, new DateOnly(2026, 6, 15)), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await Statut().HandleAsync(new ChangerStatutObligation(obligation.Id, ActionStatut.Reporter, new DateOnly(2026, 9, 1)), Ct)).IsSuccess.ShouldBeTrue();

        obligation.Statut.ShouldBe(StatutObligation.Reporte);
        obligation.DateReport.ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task La_demande_de_pre_reprise_cree_immediatement_une_obligation_a_dix_jours_ouvrables()
    {
        var handler = new EnregistrerDemandeTravailleurHandler(Store, _banc.Recalcul, Store, Banc.Cpmt, _banc.Clock);

        var resultat = await handler.HandleAsync(new EnregistrerDemandeTravailleur(_banc.Personne, _banc.Affilie, TypeObligation.VisitePreReprise, new DateOnly(2026, 6, 1)), Ct);

        resultat.IsSuccess.ShouldBeTrue();
        var demande = Store.Demandes.ShouldHaveSingleItem();
        demande.Id.ShouldBe(resultat.Value);
        demande.EnregistreePar.ShouldBe("cpmt-1");
        var obligation = Store.Obligations.ShouldHaveSingleItem();
        obligation.Type.ShouldBe(TypeObligation.VisitePreReprise);
        obligation.DateLimite.ShouldBe(new DateOnly(2026, 6, 15));
        Store.Publies<ObligationCreee>().ShouldHaveSingleItem().TypeExamen.ShouldBe("VISITE_PRE_REPRISE");
        Store.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task La_consultation_spontanee_sans_date_prend_la_date_du_jour()
    {
        var handler = new EnregistrerDemandeTravailleurHandler(Store, _banc.Recalcul, Store, new FakeUser("gest-1", Roles.GestionnaireDossiers), _banc.Clock);

        await handler.HandleAsync(new EnregistrerDemandeTravailleur(_banc.Personne, _banc.Affilie, TypeObligation.ConsultationSpontanee), Ct);

        var obligation = Store.Obligations.ShouldHaveSingleItem();
        obligation.DateDue.ShouldBe(new DateOnly(2026, 6, 15));
        obligation.DateLimite.ShouldBe(new DateOnly(2026, 6, 29));
    }

    [Fact]
    public async Task Une_demande_invalide_ou_non_autorisee_est_refusee()
    {
        var interne = new EnregistrerDemandeTravailleurHandler(Store, _banc.Recalcul, Store, Banc.Cpmt, _banc.Clock);
        var employeur = new EnregistrerDemandeTravailleurHandler(Store, _banc.Recalcul, Store, Banc.Employeur, _banc.Clock);

        (await interne.HandleAsync(new EnregistrerDemandeTravailleur(_banc.Personne, _banc.Affilie, TypeObligation.ExamenReprise), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await interne.HandleAsync(new EnregistrerDemandeTravailleur(_banc.Personne, _banc.Affilie, TypeObligation.ConsultationSpontanee, new DateOnly(2027, 1, 1)), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await employeur.HandleAsync(new EnregistrerDemandeTravailleur(_banc.Personne, _banc.Affilie, TypeObligation.ConsultationSpontanee), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        Store.Demandes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_recalcul_exige_une_seule_cible_et_la_permission_de_gestion()
    {
        var handler = new DemanderRecalculHandler(_banc.Recalcul, Store, Store, Store, Banc.Cpmt, _banc.Clock);

        (await handler.HandleAsync(new DemanderRecalcul(null, null), Ct)).Error!.Code.ShouldBe("recalcul.cible");
        (await handler.HandleAsync(new DemanderRecalcul(_banc.Personne, _banc.Affilie), Ct)).Error!.Code.ShouldBe("recalcul.cible");
        (await new DemanderRecalculHandler(_banc.Recalcul, Store, Store, Store, Banc.Employeur, _banc.Clock)
            .HandleAsync(new DemanderRecalcul(_banc.Personne, null), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Le_recalcul_d_un_affilie_traite_les_travailleurs_occupes_et_ceux_qui_ont_des_obligations_ouvertes()
    {
        var occupe = Guid.CreateVersion7();
        var occupation = new Sepp.Obligations.Domain.Projections.OccupationLocale(Guid.CreateVersion7(), occupe, _banc.Affilie);
        occupation.Debuter(new DateOnly(2025, 1, 1));
        Store.Occupations.Add(occupation);
        Ajouter();
        var handler = new DemanderRecalculHandler(_banc.Recalcul, Store, Store, Store, Banc.Cpmt, _banc.Clock);

        var resultat = await handler.HandleAsync(new DemanderRecalcul(null, _banc.Affilie), Ct);

        resultat.Value.ShouldBe(2);

        // Sans projection ni demande qui la justifie, l'obligation ouverte n'est plus attendue : annulée par le recalcul.
        Store.Obligations.Single().Statut.ShouldBe(StatutObligation.Annule);
    }

    [Fact]
    public async Task Le_traitement_periodique_annule_les_obligations_que_plus_aucune_projection_ne_justifie_sans_les_signaler_echues()
    {
        var orpheline = Ajouter("ORPHELINE", new DateOnly(2026, 6, 1));
        var traitement = new TraitementEcheances(_banc.Recalcul, Store, Store, Store, Store, _banc.Clock, Store);

        var resultat = await traitement.ExecuterAsync(Ct);

        resultat.PersonnesRecalculees.ShouldBe(1);
        resultat.ObligationsEchues.ShouldBe(0);
        orpheline.Statut.ShouldBe(StatutObligation.Annule);
        Store.Publies<ObligationEchue>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_obligation_attendue_dont_la_date_limite_est_depassee_est_signalee_echue_une_seule_fois()
    {
        // Reprise annoncée : l'examen est dû, la date limite (12 mai) est dépassée au 15 juin.
        Store.Reprises.Add(new Sepp.Obligations.Domain.Projections.RepriseLocale(_banc.Personne, _banc.Affilie, new DateOnly(2026, 4, 27), new DateOnly(2026, 3, 23), Banc.Midi));
        await _banc.Recalcul.RecalculerAsync([_banc.Personne], Ct);
        var traitement = new TraitementEcheances(_banc.Recalcul, Store, Store, Store, Store, _banc.Clock, Store);

        var premier = await traitement.ExecuterAsync(Ct);
        var second = await traitement.ExecuterAsync(Ct);

        premier.ObligationsEchues.ShouldBe(1);
        second.ObligationsEchues.ShouldBe(0);
        var echue = Store.Publies<ObligationEchue>().ShouldHaveSingleItem();
        echue.TypeExamen.ShouldBe("EXAMEN_REPRISE");
        echue.DateLimite.ShouldBe(new DateOnly(2026, 5, 12));
        echue.PersonneId.ShouldBe(_banc.Personne);
    }

    [Fact]
    public async Task Le_traitement_periodique_a_la_demande_exige_la_permission_de_gestion()
    {
        var traitement = new TraitementEcheances(_banc.Recalcul, Store, Store, Store, Store, _banc.Clock, Store);

        (await new TraiterEcheancesHandler(traitement, Banc.Employeur).HandleAsync(new TraiterEcheances(), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new TraiterEcheancesHandler(traitement, Banc.Cpmt).HandleAsync(new TraiterEcheances(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task L_ecoulement_du_temps_recalcule_les_travailleurs_dont_l_occupation_a_pris_fin()
    {
        var banc = new Banc(new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero));
        var occupation = new Sepp.Obligations.Domain.Projections.OccupationLocale(Guid.CreateVersion7(), banc.Personne, banc.Affilie);
        occupation.Debuter(new DateOnly(2025, 1, 1));
        occupation.Terminer(new DateOnly(2026, 5, 31), Banc.Midi);
        banc.Store.Occupations.Add(occupation);
        banc.Store.Reprises.Add(new Sepp.Obligations.Domain.Projections.RepriseLocale(banc.Personne, banc.Affilie, new DateOnly(2026, 5, 18), new DateOnly(2026, 4, 1), Banc.Midi));
        await banc.Recalcul.RecalculerAsync([banc.Personne], Ct);
        banc.Store.Obligations.Single().Statut.ShouldBe(StatutObligation.APlanifier);

        banc.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero));
        await new TraitementEcheances(banc.Recalcul, banc.Store, banc.Store, banc.Store, banc.Store, banc.Clock, banc.Store).ExecuterAsync(Ct);

        banc.Store.Obligations.Single().Statut.ShouldBe(StatutObligation.SortiEntreprise);
    }
}
