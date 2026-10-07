using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

internal static class Fabrique
{
    public static readonly Guid Affilie = Guid.CreateVersion7();

    public static EcheanceCalculee Echeance(
        string cle = "PERIODIQUE:a:R1:2026-01-20",
        TypeObligation type = TypeObligation.EvaluationPeriodique,
        DateOnly? due = null,
        DateOnly? limite = null,
        Realisation? realisation = null,
        string explication = "Évaluation de santé périodique pour le risque R1.",
        OrigineObligation origine = OrigineObligation.Regle,
        Guid? affilie = null) =>
        new(
            cle,
            affilie ?? Affilie,
            type,
            origine,
            ["R1"],
            due ?? new DateOnly(2026, 7, 1),
            limite ?? due ?? new DateOnly(2026, 7, 1),
            realisation,
            new Justification("regle-surveillance:R1", 1, explication, [new KeyValuePair<string, string>("frequence_mois", "12")]));

    public static Obligation Ouverte(Guid? personne = null, EcheanceCalculee? echeance = null) =>
        Obligation.Creer(personne ?? Guid.CreateVersion7(), echeance ?? Echeance(), Scenario.T0);

    public static Obligation AuStatut(StatutObligation statut, EcheanceCalculee? echeance = null)
    {
        var obligation = Ouverte(echeance: echeance);
        switch (statut)
        {
            case StatutObligation.Planifie:
                obligation.Planifier(Guid.CreateVersion7(), new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
                break;
            case StatutObligation.Convoque:
                obligation.Planifier(Guid.CreateVersion7(), new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
                obligation.Convoquer();
                break;
            case StatutObligation.Absent:
                obligation.Planifier(Guid.CreateVersion7(), new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
                obligation.MarquerAbsent();
                break;
            case StatutObligation.Reporte:
                obligation.Reporter(new DateOnly(2026, 9, 1), new DateOnly(2026, 6, 15));
                break;
            case StatutObligation.Excuse:
                obligation.Excuser();
                break;
            case StatutObligation.Realise:
                obligation.Realiser(new Realisation(new DateOnly(2026, 7, 2), Guid.CreateVersion7()));
                break;
            case StatutObligation.Annule:
                obligation.Annuler(MotifAnnulation.DecisionCpmt);
                break;
            case StatutObligation.SortiEntreprise:
                obligation.SortirDeLEntreprise();
                break;
        }

        return obligation;
    }
}

public class ObligationTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Une_obligation_creee_est_a_planifier_avec_sa_premiere_trace_de_calcul()
    {
        var personne = Guid.CreateVersion7();

        var obligation = Obligation.Creer(personne, Fabrique.Echeance(), Maintenant);

        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        obligation.PersonneId.ShouldBe(personne);
        obligation.AffilieId.ShouldBe(Fabrique.Affilie);
        obligation.EstOuverte.ShouldBeTrue();
        obligation.Echeance.ShouldBe(new DateOnly(2026, 7, 1));
        var trace = obligation.Traces.ShouldHaveSingleItem();
        trace.Regle.ShouldBe("regle-surveillance:R1");
        trace.RegleVersion.ShouldBe(1);
        trace.Entrees.ShouldBe(["frequence_mois=12"]);
        trace.DateCalcul.ShouldBe(Maintenant);
        var evenement = obligation.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ObligationOuverte>();
        evenement.Type.ShouldBe(TypeObligation.EvaluationPeriodique);
        evenement.DateDue.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void Une_obligation_deja_realisee_a_la_creation_est_realisee_sans_evenement_de_creation()
    {
        var examen = Guid.CreateVersion7();

        var obligation = Obligation.Creer(
            Guid.CreateVersion7(), Fabrique.Echeance(realisation: new Realisation(new DateOnly(2026, 5, 2), examen)), Maintenant);

        obligation.Statut.ShouldBe(StatutObligation.Realise);
        obligation.DateRealisation.ShouldBe(new DateOnly(2026, 5, 2));
        obligation.ExamenId.ShouldBe(examen);
        obligation.DomainEvents.ShouldBeEmpty();
        obligation.EstOuverte.ShouldBeFalse();
    }

    [Fact]
    public void Les_codes_de_risque_sont_normalises()
    {
        var echeance = Fabrique.Echeance() with { CodesRisques = [" r2 ", "R1", "r1"] };

        Fabrique.Ouverte(echeance: echeance).CodesRisques.ShouldBe(["R1", "R2"]);
    }

    [Fact]
    public void Une_obligation_sans_travailleur_ou_sans_cle_est_refusee()
    {
        Should.Throw<DomainException>(() => Obligation.Creer(Guid.Empty, Fabrique.Echeance(), Maintenant));
        Should.Throw<DomainException>(() => Obligation.Creer(Guid.CreateVersion7(), Fabrique.Echeance(cle: " "), Maintenant));
        Should.Throw<DomainException>(() => Obligation.Creer(Guid.CreateVersion7(), Fabrique.Echeance(cle: new string('x', 301)), Maintenant));
    }

    [Fact]
    public void Les_textes_de_la_trace_sont_tronques_a_leur_longueur_maximale()
    {
        var echeance = Fabrique.Echeance(explication: new string('e', 3000));

        Fabrique.Ouverte(echeance: echeance).Traces.Single().Explication.Length.ShouldBe(1000);
    }

    [Fact]
    public void Recalculer_le_meme_resultat_ne_change_rien_et_n_ajoute_pas_de_trace()
    {
        var obligation = Fabrique.Ouverte();
        obligation.ClearDomainEvents();

        obligation.Actualiser(Fabrique.Echeance(), Maintenant.AddDays(1)).ShouldBeFalse();

        obligation.Traces.Count.ShouldBe(1);
        obligation.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_changement_d_echeance_ajoute_une_trace_et_conserve_l_historique()
    {
        var obligation = Fabrique.Ouverte();

        obligation.Actualiser(Fabrique.Echeance(due: new DateOnly(2026, 9, 1), explication: "Fréquence surchargée."), Maintenant.AddDays(1)).ShouldBeTrue();

        obligation.DateDue.ShouldBe(new DateOnly(2026, 9, 1));
        obligation.Traces.Count.ShouldBe(2);
        obligation.Traces.OrderBy(t => t.DateCalcul).Last().Explication.ShouldBe("Fréquence surchargée.");
    }

    [Fact]
    public void Les_traces_sont_numerotees_et_un_recalcul_identique_a_date_egale_n_en_ajoute_pas()
    {
        var obligation = Fabrique.Ouverte();

        // Même instant de calcul (horloge figée) : l'ordre des traces repose sur leur numéro, pas sur leur date.
        obligation.Actualiser(Fabrique.Echeance(due: new DateOnly(2026, 9, 1), explication: "Surcharge."), Scenario.T0).ShouldBeTrue();
        obligation.Actualiser(Fabrique.Echeance(due: new DateOnly(2026, 9, 1), explication: "Surcharge."), Scenario.T0).ShouldBeFalse();

        obligation.Traces.Select(t => t.Numero).ShouldBe([1, 2]);
    }

    [Fact]
    public void Un_calcul_pour_une_autre_cle_est_refuse()
    {
        Should.Throw<DomainException>(() => Fabrique.Ouverte().Actualiser(Fabrique.Echeance(cle: "autre"), Maintenant));
    }

    [Fact]
    public void Une_obligation_realisee_n_est_plus_modifiee_par_un_recalcul()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.Realise);

        obligation.Actualiser(Fabrique.Echeance(due: new DateOnly(2027, 1, 1)), Maintenant).ShouldBeFalse();

        obligation.Statut.ShouldBe(StatutObligation.Realise);
        obligation.DateDue.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void Une_obligation_realisee_precise_l_examen_le_plus_ancien_decouvert_plus_tard()
    {
        var obligation = Fabrique.Ouverte();
        var tardif = Guid.CreateVersion7();
        var ancien = Guid.CreateVersion7();
        obligation.Actualiser(Fabrique.Echeance(realisation: new Realisation(new DateOnly(2026, 7, 3), tardif)), Maintenant).ShouldBeTrue();

        obligation.Actualiser(Fabrique.Echeance(realisation: new Realisation(new DateOnly(2026, 6, 20), ancien)), Maintenant).ShouldBeTrue();
        obligation.Actualiser(Fabrique.Echeance(realisation: new Realisation(new DateOnly(2026, 6, 20), ancien)), Maintenant).ShouldBeFalse();

        obligation.Statut.ShouldBe(StatutObligation.Realise);
        obligation.ExamenId.ShouldBe(ancien);
        obligation.DateRealisation.ShouldBe(new DateOnly(2026, 6, 20));
    }

    [Fact]
    public void Un_recalcul_qui_decouvre_l_examen_realise_l_obligation()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.Planifie);
        var examen = Guid.CreateVersion7();

        obligation.Actualiser(Fabrique.Echeance(realisation: new Realisation(new DateOnly(2026, 7, 3), examen)), Maintenant).ShouldBeTrue();

        obligation.Statut.ShouldBe(StatutObligation.Realise);
        obligation.ExamenId.ShouldBe(examen);
    }

    [Fact]
    public void Une_obligation_annulee_par_une_decision_n_est_pas_rouverte_par_un_recalcul()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.Annule);

        obligation.Actualiser(Fabrique.Echeance(), Maintenant).ShouldBeFalse();

        obligation.Statut.ShouldBe(StatutObligation.Annule);
    }

    [Fact]
    public void Une_obligation_annulee_par_un_recalcul_est_rouverte_quand_elle_redevient_due()
    {
        var obligation = Fabrique.Ouverte();
        obligation.Annuler(MotifAnnulation.Recalcul);
        obligation.ClearDomainEvents();

        obligation.Actualiser(Fabrique.Echeance(), Maintenant).ShouldBeTrue();

        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        obligation.MotifAnnulation.ShouldBeNull();
        obligation.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ObligationOuverte>();
    }

    [Fact]
    public void Une_obligation_d_un_travailleur_sorti_est_rouverte_s_il_revient()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.SortiEntreprise);

        obligation.Actualiser(Fabrique.Echeance(), Maintenant).ShouldBeTrue();

        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
    }

    [Fact]
    public void Un_rendez_vous_planifie_l_obligation_et_un_autre_le_remplace()
    {
        var obligation = Fabrique.Ouverte();
        var premier = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var debut = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

        obligation.Planifier(premier, debut);
        obligation.Planifier(premier, debut);
        obligation.Statut.ShouldBe(StatutObligation.Planifie);
        obligation.RendezVousId.ShouldBe(premier);

        obligation.Planifier(second, debut.AddDays(1));
        obligation.RendezVousId.ShouldBe(second);
        obligation.DateRendezVous.ShouldBe(debut.AddDays(1));
    }

    [Fact]
    public void L_annulation_du_rendez_vous_remet_l_obligation_a_planifier()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.Convoque);

        obligation.LibererRendezVous();

        obligation.Statut.ShouldBe(StatutObligation.APlanifier);
        obligation.RendezVousId.ShouldBeNull();
        obligation.DateRendezVous.ShouldBeNull();
    }

    [Fact]
    public void Convoquer_ou_noter_une_absence_exige_un_rendez_vous()
    {
        Should.Throw<DomainException>(() => Fabrique.Ouverte().Convoquer());
        Should.Throw<DomainException>(() => Fabrique.Ouverte().MarquerAbsent());
    }

    [Fact]
    public void Un_identifiant_de_rendez_vous_vide_est_refuse()
    {
        Should.Throw<DomainException>(() => Fabrique.Ouverte().Planifier(Guid.Empty, Maintenant));
    }

    [Fact]
    public void Un_report_garde_l_obligation_due_a_une_date_ulterieure()
    {
        var obligation = Fabrique.Ouverte();

        obligation.Reporter(new DateOnly(2026, 9, 1), new DateOnly(2026, 6, 15));

        obligation.Statut.ShouldBe(StatutObligation.Reporte);
        obligation.DateReport.ShouldBe(new DateOnly(2026, 9, 1));
        obligation.EstOuverte.ShouldBeTrue();
    }

    [Fact]
    public void Un_report_a_une_date_passee_est_refuse()
    {
        Should.Throw<DomainException>(() => Fabrique.Ouverte().Reporter(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void L_annulation_conserve_son_motif_et_libere_le_rendez_vous()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.Planifie);

        obligation.Annuler(MotifAnnulation.Doublon);

        obligation.Statut.ShouldBe(StatutObligation.Annule);
        obligation.MotifAnnulation.ShouldBe(MotifAnnulation.Doublon);
        obligation.RendezVousId.ShouldBeNull();
        obligation.EstOuverte.ShouldBeFalse();
    }

    [Fact]
    public void Une_obligation_realisee_ne_peut_plus_etre_annulee()
    {
        Should.Throw<DomainException>(() => Fabrique.AuStatut(StatutObligation.Realise).Annuler(MotifAnnulation.Autre));
    }

    [Fact]
    public void Le_depassement_de_la_date_limite_est_signale_une_seule_fois()
    {
        var obligation = Fabrique.Ouverte();
        obligation.ClearDomainEvents();
        var apres = new DateOnly(2026, 7, 2);

        obligation.SignalerEchue(new DateOnly(2026, 7, 1)).ShouldBeFalse();
        obligation.SignalerEchue(apres).ShouldBeTrue();
        obligation.SignalerEchue(apres.AddDays(1)).ShouldBeFalse();

        var evenement = obligation.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ObligationDevenueEchue>();
        evenement.DateLimite.ShouldBe(new DateOnly(2026, 7, 1));
        obligation.EchueSignaleeLe.ShouldBe(apres);
    }

    [Fact]
    public void Un_nouveau_delai_apres_recalcul_permet_un_nouveau_signalement()
    {
        var obligation = Fabrique.Ouverte();
        obligation.SignalerEchue(new DateOnly(2026, 7, 2)).ShouldBeTrue();

        obligation.Actualiser(Fabrique.Echeance(due: new DateOnly(2026, 8, 1), explication: "Nouvelle date."), Maintenant);

        obligation.EchueSignaleeLe.ShouldBeNull();
        obligation.SignalerEchue(new DateOnly(2026, 8, 2)).ShouldBeTrue();
    }

    [Fact]
    public void Une_obligation_close_ou_sans_date_limite_n_est_jamais_echue()
    {
        Fabrique.AuStatut(StatutObligation.Realise).SignalerEchue(new DateOnly(2030, 1, 1)).ShouldBeFalse();
        Fabrique.Ouverte(echeance: Fabrique.Echeance(type: TypeObligation.ProtectionMaternite) with { DateLimite = null })
            .SignalerEchue(new DateOnly(2030, 1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void La_categorie_du_tableau_de_bord_est_deduite_du_statut_et_des_dates()
    {
        var aujourdhui = new DateOnly(2026, 6, 15);

        Classement.Classer(Fabrique.Ouverte(echeance: Fabrique.Echeance(due: new DateOnly(2026, 6, 1))), aujourdhui, 90).ShouldBe(CategorieObligation.EnRetard);
        Classement.Classer(Fabrique.AuStatut(StatutObligation.Planifie, Fabrique.Echeance(due: new DateOnly(2026, 8, 1))), aujourdhui, 90).ShouldBe(CategorieObligation.Planifiee);
        Classement.Classer(Fabrique.Ouverte(echeance: Fabrique.Echeance(due: new DateOnly(2026, 8, 1))), aujourdhui, 90).ShouldBe(CategorieObligation.Due);
        Classement.Classer(Fabrique.Ouverte(echeance: Fabrique.Echeance(due: new DateOnly(2027, 3, 1))), aujourdhui, 90).ShouldBe(CategorieObligation.AVenir);
        Classement.Classer(Fabrique.AuStatut(StatutObligation.Realise), aujourdhui, 90).ShouldBeNull();
        Classement.Classer(Fabrique.AuStatut(StatutObligation.Absent, Fabrique.Echeance(due: new DateOnly(2026, 8, 1))), aujourdhui, 90).ShouldBe(CategorieObligation.Due);
    }

    [Fact]
    public void Une_obligation_sans_date_limite_depassee_n_est_jamais_en_retard_mais_peut_etre_due()
    {
        var obligation = Fabrique.Ouverte(echeance: Fabrique.Echeance(type: TypeObligation.ProtectionMaternite, due: new DateOnly(2026, 5, 10)) with { DateLimite = null });

        Classement.Classer(obligation, new DateOnly(2026, 6, 15), 90).ShouldBe(CategorieObligation.Due);
        obligation.EstEnRetardAu(new DateOnly(2026, 6, 15)).ShouldBeFalse();
    }
}
