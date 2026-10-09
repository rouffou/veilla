using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;
using Sepp.Obligations.Domain.Reprises;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>ARC-33, POR-04 : processus de reprise, jalons idempotents dans un ordre quelconque, branches, minuteries.</summary>
public class ProcessusRepriseTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DateReprise = new(2026, 6, 29);
    private static readonly DateOnly DateLimite = new(2026, 7, 10);
    private static readonly BusinessCalendar Calendrier = BusinessCalendar.Belgian(2026);
    private static readonly PolitiqueSuiviReprise Politique = new(AlerteAvantEcheanceJoursOuvrables: 2, RendezVousSansClotureJoursOuvrables: 1, DelaiDecisionJoursOuvrables: 5);

    private static readonly Guid ObligationId = Guid.CreateVersion7();
    private static readonly Guid RendezVousId = Guid.CreateVersion7();
    private static readonly Guid ExamenId = Guid.CreateVersion7();
    private static readonly Guid DecisionId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Debut = new(2026, 7, 2, 8, 0, 0, TimeSpan.Zero);

    private static ProcessusReprise Nouveau() =>
        ProcessusReprise.Enregistrer(Guid.CreateVersion7(), Guid.CreateVersion7(), DateReprise, new DateOnly(2026, 5, 1), OrigineReprise.Interne, Maintenant);

    private static readonly Func<ProcessusReprise, bool>[] Jalons =
    [
        p => p.LierObligation(ObligationId, DateLimite),
        p => p.PlanifierRendezVous(RendezVousId, Debut),
        p => p.EnregistrerConvocation(RendezVousId, Debut.AddDays(-5)),
        p => p.EnregistrerExamen(ExamenId, new DateOnly(2026, 7, 2)),
        p => p.EnregistrerDecision(DecisionId, Maintenant.AddDays(20)),
    ];

    private static IEnumerable<int[]> Permutations(int[] elements)
    {
        if (elements.Length <= 1)
        {
            yield return elements;
            yield break;
        }

        for (var i = 0; i < elements.Length; i++)
        {
            var reste = elements.Where((_, index) => index != i).ToArray();
            foreach (var suite in Permutations(reste))
            {
                yield return [elements[i], .. suite];
            }
        }
    }

    [Fact]
    public void Une_nouvelle_reprise_est_annoncee_et_publie_RepriseEnregistree()
    {
        var p = Nouveau();

        p.Statut.ShouldBe(StatutReprise.Annoncee);
        var evenement = p.DomainEvents.OfType<RepriseEnregistreeDomaine>().ShouldHaveSingleItem();
        evenement.Statut.ShouldBe(StatutPublicationReprise.Enregistree);
        evenement.RepriseId.ShouldBe(p.Id);
    }

    [Fact]
    public void Le_debut_de_l_absence_ne_peut_pas_suivre_la_reprise() =>
        Should.Throw<DomainException>(() =>
            ProcessusReprise.Enregistrer(Guid.CreateVersion7(), Guid.CreateVersion7(), DateReprise, DateReprise.AddDays(1), OrigineReprise.Interne, Maintenant));

    [Fact]
    public void Les_cinq_jalons_donnent_le_meme_etat_final_dans_les_cent_vingt_ordres_d_arrivee()
    {
        var attendu = Nouveau();
        foreach (var jalon in Jalons)
        {
            jalon(attendu);
        }

        attendu.Statut.ShouldBe(StatutReprise.Terminee);
        var nombre = 0;
        foreach (var ordre in Permutations([0, 1, 2, 3, 4]))
        {
            var p = Nouveau();
            foreach (var index in ordre)
            {
                Jalons[index](p);
            }

            var libelle = string.Join(',', ordre);
            p.Statut.ShouldBe(StatutReprise.Terminee, libelle);
            p.ObligationId.ShouldBe(ObligationId, libelle);
            p.DateLimite.ShouldBe(DateLimite, libelle);
            p.RendezVousId.ShouldBe(RendezVousId, libelle);
            p.ConvocationEnvoyeeLe.ShouldBe(attendu.ConvocationEnvoyeeLe, libelle);
            p.ExamenId.ShouldBe(ExamenId, libelle);
            p.DecisionId.ShouldBe(DecisionId, libelle);
            p.HorsDelai.ShouldBeFalse(libelle);
            nombre++;
        }

        nombre.ShouldBe(120);
    }

    [Fact]
    public void Chaque_jalon_rejoue_est_sans_effet()
    {
        var p = Nouveau();
        foreach (var jalon in Jalons)
        {
            jalon(p).ShouldBeTrue();
        }

        foreach (var jalon in Jalons)
        {
            jalon(p).ShouldBeFalse();
        }

        p.Statut.ShouldBe(StatutReprise.Terminee);
    }

    [Fact]
    public void Le_statut_suit_la_progression_nominale()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.Statut.ShouldBe(StatutReprise.ObligationOuverte);
        p.PlanifierRendezVous(RendezVousId, Debut);
        p.Statut.ShouldBe(StatutReprise.Planifiee);
        p.EnregistrerConvocation(RendezVousId, Debut.AddDays(-5));
        p.Statut.ShouldBe(StatutReprise.Convoquee);
        p.EnregistrerExamen(ExamenId, new DateOnly(2026, 7, 2));
        p.Statut.ShouldBe(StatutReprise.ExamenRealise);
        p.EnregistrerDecision(DecisionId, Maintenant);
        p.Statut.ShouldBe(StatutReprise.Terminee);
    }

    [Fact]
    public void Une_decision_avant_l_examen_donne_le_statut_DecisionEmise()
    {
        var p = Nouveau();
        p.EnregistrerDecision(DecisionId, Maintenant);

        p.Statut.ShouldBe(StatutReprise.DecisionEmise);
    }

    [Fact]
    public void La_decision_la_plus_recente_remplace_la_precedente_et_une_plus_ancienne_est_ignoree()
    {
        var p = Nouveau();
        var autre = Guid.CreateVersion7();
        p.EnregistrerDecision(DecisionId, Maintenant);

        p.EnregistrerDecision(autre, Maintenant.AddMinutes(1)).ShouldBeTrue();
        p.EnregistrerDecision(DecisionId, Maintenant).ShouldBeFalse();

        p.DecisionId.ShouldBe(autre);
    }

    [Fact]
    public void Une_urgence_non_couverte_donne_le_statut_NonCouverte_sauf_si_un_rendez_vous_existe()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.SignalerUrgenceNonCouverte();
        p.Statut.ShouldBe(StatutReprise.NonCouverte);
        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseUrgenceNonCouverte);

        p.PlanifierRendezVous(RendezVousId, Debut);

        p.Statut.ShouldBe(StatutReprise.Planifiee);
        p.Alertes().ShouldNotContain(a => a.Type == TypeAlerte.RepriseUrgenceNonCouverte);
    }

    [Fact]
    public void Une_convocation_non_remise_leve_une_alerte_jusqu_a_un_nouvel_envoi()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.PlanifierRendezVous(RendezVousId, Debut);
        p.EnregistrerConvocationNonRemise(RendezVousId, Maintenant, Maintenant);
        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseConvocationNonRemise);
        p.DomainEvents.OfType<ReplanificationUrgenteRequise>().ShouldHaveSingleItem().Motif.ShouldBe(MotifUrgence.ConvocationNonRemise);

        p.EnregistrerConvocation(RendezVousId, Maintenant.AddHours(1));

        p.Alertes().ShouldNotContain(a => a.Type == TypeAlerte.RepriseConvocationNonRemise);
    }

    [Fact]
    public void Une_absence_abandonne_le_rendez_vous_demande_une_replanification_et_se_compte_une_fois_par_rendez_vous()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.PlanifierRendezVous(RendezVousId, Debut);

        p.EnregistrerAbsence(RendezVousId, Maintenant).ShouldBeTrue();
        p.EnregistrerAbsence(RendezVousId, Maintenant).ShouldBeFalse();

        p.NombreAbsences.ShouldBe(1);
        p.RendezVousId.ShouldBeNull();
        p.ReplanificationRequise.ShouldBeTrue();
        p.DomainEvents.OfType<ReplanificationUrgenteRequise>().ShouldHaveSingleItem().Motif.ShouldBe(MotifUrgence.Absence);
        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseReplanificationRequise);

        // Le rendez-vous manqué reçu en retard ne ramène pas le processus à l'ancien rendez-vous.
        p.PlanifierRendezVous(RendezVousId, Debut).ShouldBeFalse();
        var nouveau = Guid.CreateVersion7();
        p.PlanifierRendezVous(nouveau, Debut.AddDays(3)).ShouldBeTrue();
        p.ReplanificationRequise.ShouldBeFalse();
        p.Statut.ShouldBe(StatutReprise.Planifiee);
    }

    [Fact]
    public void Un_rendez_vous_annule_demande_une_replanification_sauf_pour_une_obligation_levee()
    {
        var annule = Nouveau();
        annule.LierObligation(ObligationId, DateLimite);
        annule.PlanifierRendezVous(RendezVousId, Debut);
        annule.RendezVousAnnule(RendezVousId, "Demande", Maintenant).ShouldBeTrue();
        annule.ReplanificationRequise.ShouldBeTrue();
        annule.DomainEvents.OfType<ReplanificationUrgenteRequise>().ShouldHaveSingleItem().Motif.ShouldBe(MotifUrgence.AnnulationRendezVous);

        var levee = Nouveau();
        levee.LierObligation(ObligationId, DateLimite);
        levee.PlanifierRendezVous(RendezVousId, Debut);
        levee.RendezVousAnnule(RendezVousId, ProcessusReprise.MotifObligationLevee, Maintenant);
        levee.ReplanificationRequise.ShouldBeFalse();
        levee.DomainEvents.OfType<ReplanificationUrgenteRequise>().ShouldBeEmpty();
    }

    [Fact]
    public void Une_annulation_avant_l_examen_est_idempotente_et_publie_RepriseEnregistree_Annulee()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.ClearDomainEvents();

        p.Annuler("ErreurDeSaisie", Maintenant).ShouldBeTrue();
        p.Annuler("ErreurDeSaisie", Maintenant).ShouldBeFalse();

        p.Statut.ShouldBe(StatutReprise.Annulee);
        p.ProchaineEcheance.ShouldBeNull();
        p.DomainEvents.OfType<RepriseEnregistreeDomaine>().ShouldHaveSingleItem().Statut.ShouldBe(StatutPublicationReprise.Annulee);
        Should.Throw<DomainException>(() => p.Modifier(new DateOnly(2026, 5, 2), Maintenant));
    }

    [Fact]
    public void Une_annulation_apres_l_examen_est_refusee()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.EnregistrerExamen(ExamenId, new DateOnly(2026, 7, 2));

        Should.Throw<DomainException>(() => p.Annuler("Autre", Maintenant));

        p.Statut.ShouldBe(StatutReprise.ExamenRealise);
    }

    [Fact]
    public void Une_modification_du_debut_d_absence_publie_Modifiee_et_n_est_plus_possible_apres_l_examen()
    {
        var p = Nouveau();
        p.ClearDomainEvents();

        p.Modifier(new DateOnly(2026, 5, 4), Maintenant).ShouldBeTrue();
        p.Modifier(new DateOnly(2026, 5, 4), Maintenant).ShouldBeFalse();

        p.DomainEvents.OfType<RepriseEnregistreeDomaine>().ShouldHaveSingleItem().Statut.ShouldBe(StatutPublicationReprise.Modifiee);
        p.EnregistrerExamen(ExamenId, new DateOnly(2026, 7, 2));
        Should.Throw<DomainException>(() => p.Modifier(new DateOnly(2026, 5, 5), Maintenant));
    }

    [Fact]
    public void Une_absence_de_moins_de_quatre_semaines_donne_ExamenNonRequis_et_aucune_echeance()
    {
        // 22 juin → 29 juin : une semaine d'absence seulement.
        var scenario = new Scenario().Reprise(DateReprise, new DateOnly(2026, 6, 22));
        var resultat = scenario.Calculer(new DateOnly(2026, 6, 29));
        var moteur = scenario.Moteur();
        moteur.ExamenRepriseRequis(new DateOnly(2026, 6, 22), DateReprise).ShouldBeFalse();
        resultat.Echeances.ShouldBeEmpty();

        var p = ProcessusReprise.Enregistrer(scenario.Personne, scenario.Affilie, DateReprise, new DateOnly(2026, 6, 22), OrigineReprise.Interne, Maintenant);
        p.ClearDomainEvents();

        p.MarquerExamenNonRequis(Maintenant).ShouldBeTrue();
        p.MarquerExamenNonRequis(Maintenant).ShouldBeFalse();

        p.Statut.ShouldBe(StatutReprise.ExamenNonRequis);
        p.DomainEvents.OfType<RepriseEnregistreeDomaine>().ShouldHaveSingleItem().Statut.ShouldBe(StatutPublicationReprise.NonRequise);
        p.Reprogrammer(Politique, Calendrier);
        p.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public void Une_absence_de_quatre_semaines_ou_plus_requiert_l_examen()
    {
        var scenario = new Scenario().Reprise(DateReprise, new DateOnly(2026, 5, 1));

        scenario.Moteur().ExamenRepriseRequis(new DateOnly(2026, 5, 1), DateReprise).ShouldBeTrue();
        scenario.Calculer(DateReprise).Echeances.ShouldHaveSingleItem().Cle.ShouldBe(MoteurEcheances.CleReprise(scenario.Affilie, DateReprise));
    }

    [Fact]
    public void Le_moteur_ignore_une_reprise_annulee()
    {
        var scenario = new Scenario().Reprise(DateReprise, new DateOnly(2026, 5, 1));
        scenario.Reprises[0].MarquerAnnulee(Maintenant.AddDays(1));

        scenario.Calculer(DateReprise).Echeances.ShouldBeEmpty();
    }

    [Fact]
    public void Le_sans_objet_est_leve_quand_l_obligation_reapparait()
    {
        var p = Nouveau();
        p.MarquerSansObjet().ShouldBeTrue();
        p.Statut.ShouldBe(StatutReprise.SansObjet);

        p.LierObligation(ObligationId, DateLimite);

        p.Statut.ShouldBe(StatutReprise.ObligationOuverte);
    }

    [Fact]
    public void La_premiere_minuterie_est_l_alerte_a_deux_jours_ouvrables_de_la_date_limite()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);

        p.Reprogrammer(Politique, Calendrier).ShouldBeTrue();

        p.TypeMinuterie.ShouldBe(TypeMinuterie.EcheanceMenacee);
        p.ProchaineEcheance.ShouldBe(new DateOnly(2026, 7, 8));
        p.Reprogrammer(Politique, Calendrier).ShouldBeFalse();
    }

    [Fact]
    public void Les_minuteries_se_declenchent_une_fois_chacune_dans_l_ordre_des_dates()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.Reprogrammer(Politique, Calendrier);

        p.DeclencherMinuteries(new DateOnly(2026, 7, 7), Politique, Calendrier).ShouldBeEmpty();

        p.DeclencherMinuteries(new DateOnly(2026, 7, 8), Politique, Calendrier).ShouldBe([TypeMinuterie.EcheanceMenacee]);
        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseEcheanceMenacee);
        p.HorsDelai.ShouldBeFalse();
        p.TypeMinuterie.ShouldBe(TypeMinuterie.HorsDelai);
        p.ProchaineEcheance.ShouldBe(new DateOnly(2026, 7, 11));

        p.DeclencherMinuteries(new DateOnly(2026, 7, 8), Politique, Calendrier).ShouldBeEmpty();
        p.DeclencherMinuteries(new DateOnly(2026, 7, 11), Politique, Calendrier).ShouldBe([TypeMinuterie.HorsDelai]);

        p.HorsDelai.ShouldBeTrue();
        p.EnRetardAu(new DateOnly(2026, 7, 11)).ShouldBeTrue();
        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseHorsDelai);
        p.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public void Un_examen_apres_la_date_limite_marque_le_processus_hors_delai()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);

        p.EnregistrerExamen(ExamenId, DateLimite.AddDays(3));

        p.HorsDelai.ShouldBeTrue();
        p.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public void Un_rendez_vous_passe_d_un_jour_ouvrable_sans_cloture_leve_une_alerte()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.PlanifierRendezVous(RendezVousId, Debut);

        // Rendez-vous le jeudi 2 juillet : alerte le vendredi 3.
        p.DeclencherMinuteries(new DateOnly(2026, 7, 3), Politique, Calendrier).ShouldContain(TypeMinuterie.RendezVousSansCloture);

        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseRendezVousSansCloture);
    }

    [Fact]
    public void Un_rendez_vous_au_dela_de_la_date_limite_leve_une_alerte()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.PlanifierRendezVous(RendezVousId, new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero));

        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseRendezVousApresDateLimite);
    }

    [Fact]
    public void Un_examen_sans_decision_apres_le_delai_leve_une_alerte_si_le_delai_est_configure()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.EnregistrerExamen(ExamenId, new DateOnly(2026, 7, 1));
        p.Reprogrammer(Politique, Calendrier);

        p.TypeMinuterie.ShouldBe(TypeMinuterie.DecisionEnAttente);
        p.ProchaineEcheance.ShouldBe(new DateOnly(2026, 7, 8));
        p.DeclencherMinuteries(new DateOnly(2026, 7, 8), Politique, Calendrier);

        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseDecisionEnAttente);

        var sansDelai = Nouveau();
        sansDelai.LierObligation(ObligationId, DateLimite);
        sansDelai.EnregistrerExamen(ExamenId, new DateOnly(2026, 7, 1));
        sansDelai.Reprogrammer(PolitiqueSuiviReprise.ParDefaut, Calendrier);
        sansDelai.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public void Une_nouvelle_date_limite_rearme_les_minuteries_de_delai()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.DeclencherMinuteries(new DateOnly(2026, 7, 11), Politique, Calendrier);
        p.HorsDelai.ShouldBeTrue();

        p.LierObligation(ObligationId, DateLimite.AddDays(7));
        p.Reprogrammer(Politique, Calendrier);

        p.HorsDelai.ShouldBeFalse();
        p.TypeMinuterie.ShouldBe(TypeMinuterie.EcheanceMenacee);
    }

    [Fact]
    public void Une_expiration_configuree_leve_une_alerte_sans_changer_le_statut()
    {
        var politique = Politique with { ExpirationJours = 60 };
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);

        p.DeclencherMinuteries(DateReprise.AddDays(60), politique, Calendrier);

        p.Alertes().ShouldContain(a => a.Type == TypeAlerte.RepriseExpiree);
        p.Statut.ShouldBe(StatutReprise.ObligationOuverte);
    }

    [Fact]
    public void Le_processus_annule_n_a_plus_de_minuterie_ni_d_alerte()
    {
        var p = Nouveau();
        p.LierObligation(ObligationId, DateLimite);
        p.Reprogrammer(Politique, Calendrier);
        p.Annuler("Autre", Maintenant);

        p.Reprogrammer(Politique, Calendrier);

        p.ProchaineEcheance.ShouldBeNull();
        p.DeclencherMinuteries(new DateOnly(2026, 8, 1), Politique, Calendrier).ShouldBeEmpty();
        p.Alertes().ShouldBeEmpty();
    }
}

/// <summary>SAN-04, SAN-13, ARC-33 : clôture d'obligation publiée, absence et convocation reportées sur l'obligation.</summary>
public class ReconciliationRepriseTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private static ResultatCalcul Resultat(params EcheanceCalculee[] echeances) => new([.. echeances], [], [], false, new HashSet<Guid>());

    private static (Obligation Obligation, RendezVousLocal RendezVous) Planifiee()
    {
        var obligation = Fabrique.Ouverte();
        var rendezVous = new RendezVousLocal(Guid.CreateVersion7(), obligation.PersonneId);
        rendezVous.Planifier(obligation.AffilieId, Maintenant.AddDays(10), [obligation.Id], Maintenant);
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);
        return (obligation, rendezVous);
    }

    [Fact]
    public void Un_rendez_vous_marque_absent_passe_l_obligation_a_absent()
    {
        var (obligation, rendezVous) = Planifiee();
        rendezVous.MarquerAbsent().ShouldBeTrue();
        rendezVous.MarquerAbsent().ShouldBeFalse();

        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);

        obligation.Statut.ShouldBe(StatutObligation.Absent);
    }

    [Fact]
    public void Un_nouveau_rendez_vous_apres_l_absence_replanifie_l_obligation()
    {
        var (obligation, absent) = Planifiee();
        absent.MarquerAbsent();
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [absent], Maintenant);
        var suivant = new RendezVousLocal(Guid.CreateVersion7(), obligation.PersonneId);
        suivant.Planifier(obligation.AffilieId, Maintenant.AddDays(20), [obligation.Id], Maintenant.AddHours(1));

        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [absent, suivant], Maintenant);

        obligation.Statut.ShouldBe(StatutObligation.Planifie);
        obligation.RendezVousId.ShouldBe(suivant.RendezVousId);
    }

    [Fact]
    public void Une_convocation_envoyee_apres_la_planification_passe_l_obligation_a_convoque()
    {
        var (obligation, rendezVous) = Planifiee();
        rendezVous.EnregistrerConvocation(Maintenant.AddMinutes(5));

        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);

        obligation.Statut.ShouldBe(StatutObligation.Convoque);
    }

    [Fact]
    public void Une_convocation_anterieure_a_la_replanification_ne_convoque_pas_pour_la_nouvelle_date()
    {
        var (obligation, rendezVous) = Planifiee();
        rendezVous.EnregistrerConvocation(Maintenant.AddMinutes(5));
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);

        rendezVous.Replanifier(Maintenant.AddDays(15), Maintenant.AddHours(1)).ShouldBeTrue();
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);

        obligation.Statut.ShouldBe(StatutObligation.Planifie);
        obligation.DateRendezVous.ShouldBe(Maintenant.AddDays(15));

        rendezVous.EnregistrerConvocation(Maintenant.AddHours(2));
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(Fabrique.Echeance()), [rendezVous], Maintenant);
        obligation.Statut.ShouldBe(StatutObligation.Convoque);
    }

    [Fact]
    public void Un_evenement_de_replanification_plus_ancien_est_ignore()
    {
        var (_, rendezVous) = Planifiee();
        rendezVous.Replanifier(Maintenant.AddDays(15), Maintenant.AddHours(2)).ShouldBeTrue();

        rendezVous.Replanifier(Maintenant.AddDays(30), Maintenant.AddHours(1)).ShouldBeFalse();

        rendezVous.Debut.ShouldBe(Maintenant.AddDays(15));
    }

    [Fact]
    public void La_realisation_la_sortie_et_l_annulation_d_une_obligation_ouverte_levent_un_evenement_de_cloture()
    {
        var realisee = Fabrique.Ouverte();
        realisee.ClearDomainEvents();
        realisee.Realiser(new Realisation(new DateOnly(2026, 7, 2), Guid.CreateVersion7()), Maintenant);
        var cloture = realisee.DomainEvents.OfType<ObligationDevenueCloturee>().ShouldHaveSingleItem();
        cloture.Statut.ShouldBe(StatutCloture.Realise);
        cloture.Date.ShouldBe(new DateOnly(2026, 7, 2));

        var sortie = Fabrique.Ouverte();
        sortie.ClearDomainEvents();
        sortie.SortirDeLEntreprise(Maintenant);
        sortie.DomainEvents.OfType<ObligationDevenueCloturee>().ShouldHaveSingleItem().Statut.ShouldBe(StatutCloture.SortiEntreprise);

        var annulee = Fabrique.Ouverte();
        annulee.ClearDomainEvents();
        annulee.Annuler(MotifAnnulation.Recalcul, Maintenant);
        var evenement = annulee.DomainEvents.OfType<ObligationDevenueCloturee>().ShouldHaveSingleItem();
        evenement.Statut.ShouldBe(StatutCloture.Annule);
        evenement.Motif.ShouldBe("Recalcul");
        evenement.Date.ShouldBe(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public void Une_annulation_par_recalcul_publie_la_cloture_une_seule_fois()
    {
        var obligation = Fabrique.Ouverte();
        obligation.ClearDomainEvents();

        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(), [], Maintenant);
        ReconciliationObligations.Appliquer(obligation.PersonneId, [obligation], Resultat(), [], Maintenant);

        obligation.DomainEvents.OfType<ObligationDevenueCloturee>().ShouldHaveSingleItem();
    }
}
