using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>SAN-03 : regroupement dans une fenêtre paramétrable.</summary>
public class RegroupementTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 6, 15);

    private static Obligation Pour(
        Guid personne, string cle, DateOnly due, TypeObligation type = TypeObligation.EvaluationPeriodique, DateOnly? limite = null, bool sansLimite = false)
    {
        var echeance = Fabrique.Echeance(cle: cle, type: type, due: due, limite: limite);
        return Obligation.Creer(personne, sansLimite ? echeance with { DateLimite = null } : echeance, Scenario.T0);
    }

    [Fact]
    public void Deux_echeances_proches_du_meme_travailleur_donnent_un_seul_rendez_vous()
    {
        var personne = Guid.CreateVersion7();
        var a = Pour(personne, "A", new DateOnly(2026, 7, 1));
        var b = Pour(personne, "B", new DateOnly(2026, 7, 20));

        var proposition = Regroupement.Proposer([a, b], Aujourdhui, 30).ShouldHaveSingleItem();

        proposition.EstRegroupement.ShouldBeTrue();
        proposition.PersonneId.ShouldBe(personne);
        proposition.Obligations.ShouldBe([a, b]);
        proposition.DateAuPlusTard.ShouldBe(new DateOnly(2026, 7, 1));
        proposition.DateProposee.ShouldBe(new DateOnly(2026, 6, 20));
    }

    [Fact]
    public void Deux_travailleurs_ne_sont_jamais_regroupes()
    {
        var propositions = Regroupement.Proposer(
            [Pour(Guid.CreateVersion7(), "A", new DateOnly(2026, 7, 1)), Pour(Guid.CreateVersion7(), "B", new DateOnly(2026, 7, 1))], Aujourdhui, 30);

        propositions.Count.ShouldBe(2);
        propositions.ShouldAllBe(p => !p.EstRegroupement);
    }

    [Fact]
    public void Une_echeance_hors_de_la_fenetre_n_est_pas_proposee()
    {
        var personne = Guid.CreateVersion7();

        Regroupement.Proposer([Pour(personne, "A", new DateOnly(2026, 9, 30))], Aujourdhui, 30).ShouldBeEmpty();
    }

    [Fact]
    public void Elargir_la_fenetre_regroupe_davantage()
    {
        var personne = Guid.CreateVersion7();
        var a = Pour(personne, "A", new DateOnly(2026, 7, 1));
        var b = Pour(personne, "B", new DateOnly(2026, 8, 25));

        Regroupement.Proposer([a, b], Aujourdhui, 30).ShouldHaveSingleItem().Obligations.ShouldBe([a]);
        Regroupement.Proposer([a, b], Aujourdhui, 60).ShouldHaveSingleItem().Obligations.Count.ShouldBe(2);
    }

    [Fact]
    public void Une_fenetre_nulle_ne_propose_que_les_obligations_deja_dues_sans_anticipation()
    {
        var personne = Guid.CreateVersion7();
        var due = Pour(personne, "A", new DateOnly(2026, 6, 10), limite: new DateOnly(2026, 6, 25));
        var dueAussi = Pour(personne, "B", new DateOnly(2026, 6, 12), limite: new DateOnly(2026, 6, 30));
        var future = Pour(personne, "C", new DateOnly(2026, 6, 20), limite: new DateOnly(2026, 6, 28));

        var proposition = Regroupement.Proposer([due, dueAussi, future], Aujourdhui, 0).ShouldHaveSingleItem();

        proposition.Obligations.Select(o => o.Cle).ShouldBe(["A", "B"]);
        proposition.DateProposee.ShouldBe(Aujourdhui);
        proposition.DateAuPlusTard.ShouldBe(new DateOnly(2026, 6, 25));
    }

    [Fact]
    public void Des_periodes_qui_ne_se_recoupent_pas_donnent_plusieurs_rendez_vous()
    {
        var personne = Guid.CreateVersion7();
        var a = Pour(personne, "A", new DateOnly(2026, 6, 16), TypeObligation.ExamenReprise, new DateOnly(2026, 6, 20));
        var c = Pour(personne, "C", new DateOnly(2026, 6, 27), TypeObligation.ExamenReprise, new DateOnly(2026, 6, 28));

        // Un examen d'événement n'est pas anticipé : C ne peut pas être couvert par le rendez-vous de A.
        Regroupement.Proposer([a, c], Aujourdhui, 30).Count.ShouldBe(2);
    }

    [Fact]
    public void Un_examen_declenche_par_un_evenement_n_est_pas_anticipe_mais_se_regroupe_avec_une_echeance_proche()
    {
        var personne = Guid.CreateVersion7();
        var reprise = Pour(personne, "R", Aujourdhui, TypeObligation.ExamenReprise, new DateOnly(2026, 6, 29));
        var periodique = Pour(personne, "P", new DateOnly(2026, 7, 5));

        var proposition = Regroupement.Proposer([reprise, periodique], Aujourdhui, 30).ShouldHaveSingleItem();

        proposition.Obligations.Count.ShouldBe(2);
        proposition.DateAuPlusTard.ShouldBe(new DateOnly(2026, 6, 29));
    }

    [Fact]
    public void Un_examen_d_evenement_futur_n_est_pas_avance_avant_sa_date_due()
    {
        var personne = Guid.CreateVersion7();
        var reprise = Pour(personne, "R", new DateOnly(2026, 7, 10), TypeObligation.ExamenReprise, new DateOnly(2026, 7, 24));
        var periodique = Pour(personne, "P", new DateOnly(2026, 6, 20));

        // La reprise ne peut pas avoir lieu avant le 10 juillet : la période du périodique (jusqu'au 20 juin) ne la rencontre pas.
        Regroupement.Proposer([reprise, periodique], Aujourdhui, 30).Count.ShouldBe(2);
    }

    [Fact]
    public void Un_report_retarde_le_debut_de_la_periode_de_realisation()
    {
        var personne = Guid.CreateVersion7();
        var a = Pour(personne, "A", new DateOnly(2026, 6, 20), limite: new DateOnly(2026, 8, 1));
        a.Reporter(new DateOnly(2026, 7, 10), Aujourdhui);
        var b = Pour(personne, "B", new DateOnly(2026, 6, 25), limite: new DateOnly(2026, 6, 30));

        // Reportée au 10 juillet, A ne peut plus être couverte par le rendez-vous de B.
        Regroupement.Proposer([a, b], Aujourdhui, 30).Count.ShouldBe(2);
    }

    [Fact]
    public void Les_obligations_deja_planifiees_ou_closes_ne_sont_pas_proposees()
    {
        var personne = Guid.CreateVersion7();
        var planifiee = Pour(personne, "A", new DateOnly(2026, 7, 1));
        planifiee.Planifier(Guid.CreateVersion7(), new DateTimeOffset(2026, 6, 30, 9, 0, 0, TimeSpan.Zero));
        var realisee = Pour(personne, "B", new DateOnly(2026, 7, 1));
        realisee.Realiser(new Realisation(Aujourdhui, Guid.CreateVersion7()));

        Regroupement.Proposer([planifiee, realisee], Aujourdhui, 30).ShouldBeEmpty();
    }

    [Fact]
    public void Une_obligation_sans_date_limite_est_regroupee_jusqu_a_sa_date_due_plus_la_fenetre()
    {
        var personne = Guid.CreateVersion7();
        var maternite = Pour(personne, "M", new DateOnly(2026, 6, 1), TypeObligation.ProtectionMaternite, sansLimite: true);

        var proposition = Regroupement.Proposer([maternite], Aujourdhui, 30).ShouldHaveSingleItem();

        proposition.DateAuPlusTard.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void Une_fenetre_hors_bornes_est_refusee(int fenetre) =>
        Should.Throw<DomainException>(() => Regroupement.Proposer([], Aujourdhui, fenetre));

    [Fact]
    public void Le_regroupement_est_deterministe_quel_que_soit_l_ordre_d_entree()
    {
        var personne = Guid.CreateVersion7();
        var obligations = Enumerable.Range(0, 6).Select(i => Pour(personne, $"K{i}", new DateOnly(2026, 7, 1).AddDays(i * 11))).ToList();

        var normal = Regroupement.Proposer(obligations, Aujourdhui, 30).Select(p => string.Join(',', p.Obligations.Select(o => o.Cle))).ToList();
        var inverse = Regroupement.Proposer(Enumerable.Reverse(obligations), Aujourdhui, 30).Select(p => string.Join(',', p.Obligations.Select(o => o.Cle))).ToList();

        inverse.ShouldBe(normal);
    }
}

/// <summary>SAN-04 : application d'un calcul aux obligations existantes.</summary>
public class ReconciliationTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private static ResultatCalcul Resultat(
        IEnumerable<EcheanceCalculee> echeances, bool sortiDeToutes = false, IEnumerable<Guid>? affiliesQuittes = null) =>
        new([.. echeances], [], [], sortiDeToutes, (affiliesQuittes ?? []).ToHashSet());

    [Fact]
    public void Une_echeance_attendue_sans_obligation_en_cree_une()
    {
        var personne = Guid.CreateVersion7();

        var nouvelles = ReconciliationObligations.Appliquer(personne, [], Resultat([Fabrique.Echeance()]), [], Maintenant);

        var obligation = nouvelles.ShouldHaveSingleItem();
        obligation.PersonneId.ShouldBe(personne);
        obligation.Cle.ShouldBe(Fabrique.Echeance().Cle);
    }

    [Fact]
    public void Recalculer_deux_fois_ne_cree_pas_de_doublon()
    {
        var personne = Guid.CreateVersion7();
        var resultat = Resultat([Fabrique.Echeance(), Fabrique.Echeance()]);

        var premiere = ReconciliationObligations.Appliquer(personne, [], resultat, [], Maintenant);
        var seconde = ReconciliationObligations.Appliquer(personne, premiere, resultat, [], Maintenant.AddHours(1));

        premiere.Count.ShouldBe(1);
        seconde.ShouldBeEmpty();
        premiere[0].Traces.Count.ShouldBe(1);
    }

    [Fact]
    public void Une_echeance_qui_change_met_a_jour_l_obligation_existante()
    {
        var existante = Fabrique.Ouverte();

        ReconciliationObligations.Appliquer(
            existante.PersonneId, [existante], Resultat([Fabrique.Echeance(due: new DateOnly(2026, 12, 1), explication: "Surcharge.")]), [], Maintenant);

        existante.DateDue.ShouldBe(new DateOnly(2026, 12, 1));
    }

    [Fact]
    public void Une_obligation_ouverte_qui_n_est_plus_attendue_est_annulee_par_le_recalcul()
    {
        var existante = Fabrique.Ouverte();

        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([]), [], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.Annule);
        existante.MotifAnnulation.ShouldBe(MotifAnnulation.Recalcul);
    }

    [Fact]
    public void Une_obligation_realisee_n_est_pas_annulee_meme_si_elle_n_est_plus_attendue()
    {
        var existante = Fabrique.AuStatut(StatutObligation.Realise);

        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([]), [], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.Realise);
    }

    [Fact]
    public void Une_obligation_ouverte_d_un_travailleur_qui_a_quitte_l_affilie_passe_a_sorti_de_l_entreprise()
    {
        var existante = Fabrique.Ouverte();

        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([], affiliesQuittes: [existante.AffilieId]), [], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.SortiEntreprise);
    }

    [Fact]
    public void Une_obligation_annulee_par_une_decision_reste_annulee()
    {
        var existante = Fabrique.AuStatut(StatutObligation.Annule);

        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.Annule);
        existante.MotifAnnulation.ShouldBe(MotifAnnulation.DecisionCpmt);
    }

    [Fact]
    public void Un_rendez_vous_actif_qui_couvre_l_obligation_la_planifie()
    {
        var existante = Fabrique.Ouverte();
        var rendezVous = new RendezVousLocal(Guid.CreateVersion7(), existante.PersonneId);
        var debut = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        rendezVous.Planifier(existante.AffilieId, debut, [existante.Id], Maintenant);

        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.Planifie);
        existante.RendezVousId.ShouldBe(rendezVous.RendezVousId);
        existante.DateRendezVous.ShouldBe(debut);
    }

    [Fact]
    public void Un_rendez_vous_annule_remet_l_obligation_a_planifier()
    {
        var existante = Fabrique.Ouverte();
        var rendezVous = new RendezVousLocal(Guid.CreateVersion7(), existante.PersonneId);
        rendezVous.Planifier(existante.AffilieId, Maintenant.AddDays(10), [existante.Id], Maintenant);
        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);

        rendezVous.Annuler();
        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.APlanifier);
        existante.RendezVousId.ShouldBeNull();
    }

    [Fact]
    public void Un_rendez_vous_reprogramme_met_a_jour_la_date_de_l_obligation()
    {
        var existante = Fabrique.Ouverte();
        var rendezVous = new RendezVousLocal(Guid.CreateVersion7(), existante.PersonneId);
        rendezVous.Planifier(existante.AffilieId, Maintenant.AddDays(10), [existante.Id], Maintenant);
        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);

        rendezVous.Planifier(existante.AffilieId, Maintenant.AddDays(20), [existante.Id], Maintenant.AddMinutes(5));
        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);

        existante.DateRendezVous.ShouldBe(Maintenant.AddDays(20));
    }

    [Fact]
    public void Une_absence_notee_n_est_pas_effacee_par_le_meme_rendez_vous()
    {
        var existante = Fabrique.Ouverte();
        var rendezVous = new RendezVousLocal(Guid.CreateVersion7(), existante.PersonneId);
        rendezVous.Planifier(existante.AffilieId, Maintenant.AddDays(10), [existante.Id], Maintenant);
        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);
        existante.MarquerAbsent();

        ReconciliationObligations.Appliquer(existante.PersonneId, [existante], Resultat([Fabrique.Echeance()]), [rendezVous], Maintenant);

        existante.Statut.ShouldBe(StatutObligation.Absent);
    }

    [Fact]
    public void Un_rendez_vous_annule_recu_avant_la_planification_reste_annule()
    {
        var rendezVous = new RendezVousLocal(Guid.CreateVersion7(), Guid.CreateVersion7());
        rendezVous.Annuler();

        rendezVous.Planifier(Guid.CreateVersion7(), Maintenant, [Guid.CreateVersion7()], Maintenant).ShouldBeTrue();

        rendezVous.EstActif.ShouldBeFalse();
    }
}
