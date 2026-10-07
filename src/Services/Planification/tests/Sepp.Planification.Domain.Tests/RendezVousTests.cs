using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

public class RendezVousTests
{
    [Fact]
    public void Planifier_reserve_le_creneau_et_recopie_ses_horaires()
    {
        var creneau = Fabrique.Creneau();
        var obligations = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty };

        var rdv = Fabrique.RendezVous(creneau, obligations: obligations);

        creneau.Statut.ShouldBe(StatutCreneau.Reserve);
        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);
        rdv.Debut.ShouldBe(creneau.Debut);
        rdv.RessourceId.ShouldBe(creneau.RessourceId);
        rdv.ObligationIds.Count.ShouldBe(3);
    }

    [Fact]
    public void Un_rendez_vous_peut_couvrir_plusieurs_obligations()
    {
        var obligations = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };

        Fabrique.RendezVous(obligations: obligations).ObligationIds.ShouldBe(obligations);
    }

    [Fact]
    public void Planifier_dans_le_passe_est_refuse_et_ne_reserve_pas_le_creneau()
    {
        var creneau = Fabrique.Creneau();

        Should.Throw<DomainException>(() => Fabrique.RendezVous(creneau, creneau.Debut.AddMinutes(1)));
        creneau.Statut.ShouldBe(StatutCreneau.Libre);
    }

    [Fact]
    public void Un_creneau_deja_reserve_ne_donne_pas_un_second_rendez_vous()
    {
        var creneau = Fabrique.Creneau();
        Fabrique.RendezVous(creneau);

        Should.Throw<DomainException>(() => Fabrique.RendezVous(creneau));
    }

    [Fact]
    public void Annuler_libere_le_creneau()
    {
        var creneau = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(creneau);

        rdv.Annuler(MotifAnnulation.DemandeTravailleur, creneau, Fabrique.Maintenant);

        rdv.Statut.ShouldBe(StatutRendezVous.Annule);
        rdv.MotifAnnulation.ShouldBe(MotifAnnulation.DemandeTravailleur);
        creneau.Statut.ShouldBe(StatutCreneau.Libre);
        Should.Throw<DomainException>(() => rdv.Annuler(MotifAnnulation.Autre, creneau, Fabrique.Maintenant));
    }

    [Fact]
    public void Deplacer_libere_l_ancien_creneau_et_reserve_le_nouveau()
    {
        var ancien = Fabrique.Creneau();
        var nouveau = Fabrique.Creneau(Fabrique.Lundi.AddDays(1));
        var rdv = Fabrique.RendezVous(ancien);
        var ancienDebut = rdv.Debut;

        var retour = rdv.Deplacer(ancien, nouveau, Fabrique.Maintenant);

        retour.ShouldBe(ancienDebut);
        rdv.CreneauId.ShouldBe(nouveau.Id);
        rdv.Debut.ShouldBe(nouveau.Debut);
        ancien.Statut.ShouldBe(StatutCreneau.Libre);
        nouveau.Statut.ShouldBe(StatutCreneau.Reserve);
    }

    [Fact]
    public void Deplacer_reinitialise_les_rappels()
    {
        var ancien = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(ancien);
        rdv.MarquerRappel(1, Fabrique.Maintenant);

        rdv.Deplacer(ancien, Fabrique.Creneau(Fabrique.Lundi.AddDays(7)), Fabrique.Maintenant);

        rdv.Rappel1EmisLe.ShouldBeNull();
    }

    [Fact]
    public void La_salle_d_attente_suit_arrivee_appel_fin()
    {
        var creneau = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(creneau);
        var salle = Guid.CreateVersion7();

        Should.Throw<DomainException>(() => rdv.Appeler(salle, creneau.Debut));
        Should.Throw<DomainException>(() => rdv.EnregistrerArrivee(creneau.Debut.AddDays(-1)));

        rdv.EnregistrerArrivee(creneau.Debut.AddMinutes(-10));
        rdv.Statut.ShouldBe(StatutRendezVous.Arrive);
        rdv.Appeler(salle, creneau.Debut);
        rdv.Statut.ShouldBe(StatutRendezVous.EnSalle);
        rdv.SalleId.ShouldBe(salle);
        rdv.Terminer();
        rdv.Statut.ShouldBe(StatutRendezVous.Termine);
        rdv.EstActif.ShouldBeFalse();
    }

    [Fact]
    public void L_absence_ne_se_constate_qu_apres_l_heure_du_rendez_vous()
    {
        var creneau = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(creneau);

        Should.Throw<DomainException>(() => rdv.ConstaterAbsence(creneau.Debut.AddMinutes(-1)));

        rdv.ConstaterAbsence(creneau.Debut.AddMinutes(45));
        rdv.Statut.ShouldBe(StatutRendezVous.Absent);
    }

    // SAN-13 : rappels J-7 et J-1 (jours calendrier, heure belge).
    private static RendezVous RendezVousPlanifieLe(DateOnly jourPlanification) =>
        Fabrique.RendezVous(maintenant: Fabrique.Instant(jourPlanification, 10));

    [Fact]
    public void Le_premier_rappel_est_du_sept_jours_avant()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-30));

        rdv.RappelDu(Fabrique.Lundi.AddDays(-8), 7, 1).ShouldBeNull();
        rdv.RappelDu(Fabrique.Lundi.AddDays(-7), 7, 1).ShouldBe(1);
    }

    [Fact]
    public void Le_second_rappel_est_du_la_veille()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-30));
        rdv.MarquerRappel(1, Fabrique.Maintenant);

        rdv.RappelDu(Fabrique.Lundi.AddDays(-2), 7, 1).ShouldBeNull();
        rdv.RappelDu(Fabrique.Lundi.AddDays(-1), 7, 1).ShouldBe(2);
    }

    [Fact]
    public void Un_rappel_emis_n_est_jamais_reemis()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-30));
        var jour = Fabrique.Lundi.AddDays(-7);

        rdv.RappelDu(jour, 7, 1).ShouldBe(1);
        rdv.MarquerRappel(1, Fabrique.Maintenant);

        rdv.RappelDu(jour, 7, 1).ShouldBeNull();
        rdv.RappelDu(jour.AddDays(1), 7, 1).ShouldBeNull();
    }

    [Fact]
    public void Un_rappel_manque_est_rattrape_tant_que_le_second_n_est_pas_du()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-30));

        rdv.RappelDu(Fabrique.Lundi.AddDays(-5), 7, 1).ShouldBe(1);
        // Le jour du second rappel, le premier est sans objet : un seul message part.
        rdv.RappelDu(Fabrique.Lundi.AddDays(-1), 7, 1).ShouldBe(2);
    }

    [Fact]
    public void Un_rendez_vous_pris_apres_la_date_du_premier_rappel_n_en_recoit_pas()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-3));

        rdv.RappelDu(Fabrique.Lundi.AddDays(-3), 7, 1).ShouldBeNull();
        rdv.RappelDu(Fabrique.Lundi.AddDays(-2), 7, 1).ShouldBeNull();
        rdv.RappelDu(Fabrique.Lundi.AddDays(-1), 7, 1).ShouldBe(2);
    }

    [Fact]
    public void Un_rendez_vous_pris_la_veille_ne_donne_aucun_rappel()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-1));

        rdv.RappelDu(Fabrique.Lundi.AddDays(-1), 7, 1).ShouldBeNull();
        rdv.RappelDu(Fabrique.Lundi, 7, 1).ShouldBeNull();
    }

    [Fact]
    public void Un_rendez_vous_annule_ou_passe_ne_donne_plus_de_rappel()
    {
        var creneau = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(creneau);

        rdv.RappelDu(Fabrique.Lundi, 7, 1).ShouldBeNull();
        rdv.Annuler(MotifAnnulation.Autre, creneau, Fabrique.Maintenant);
        rdv.RappelDu(Fabrique.Lundi.AddDays(-7), 7, 1).ShouldBeNull();
    }

    [Fact]
    public void Les_delais_de_rappel_suivent_les_parametres()
    {
        var rdv = RendezVousPlanifieLe(Fabrique.Lundi.AddDays(-30));

        rdv.RappelDu(Fabrique.Lundi.AddDays(-14), 14, 2).ShouldBe(1);
        rdv.RappelDu(Fabrique.Lundi.AddDays(-2), 14, 2).ShouldBe(2);
        Should.Throw<DomainException>(() => rdv.MarquerRappel(3, Fabrique.Maintenant));
    }
}
