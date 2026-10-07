using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

public class CreneauTests
{
    [Fact]
    public void Un_creneau_occupe_sa_ressource_principale_et_ses_ressources_associees()
    {
        var salle = Guid.CreateVersion7();
        var appareil = Guid.CreateVersion7();

        var creneau = Fabrique.Creneau(associees: [salle, appareil, salle]);

        creneau.RessourcesMobilisees.ShouldBe([Fabrique.Conseiller, salle, appareil], ignoreOrder: true);
        creneau.Occupations.ShouldAllBe(o => o.Debut == creneau.Debut && o.Fin == creneau.Fin);
        creneau.Mobilise(salle).ShouldBeTrue();
    }

    [Fact]
    public void La_fin_doit_suivre_le_debut()
    {
        var debut = Fabrique.Instant(Fabrique.Lundi, 9);

        Should.Throw<DomainException>(() => Creneau.Creer(Fabrique.Conseiller, Fabrique.Lieu, debut, debut, "VISITE_PERIODIQUE", false, false, null));
    }

    [Fact]
    public void Un_creneau_ne_se_reserve_qu_une_fois()
    {
        var creneau = Fabrique.Creneau();
        creneau.Reserver();

        Should.Throw<DomainException>(creneau.Reserver).Message.ShouldContain("plus disponible");
    }

    [Fact]
    public void Un_creneau_reserve_aux_urgences_n_est_pas_ouvert_en_ligne()
    {
        Fabrique.Creneau(urgence: true, enLigne: true).OuvertEnLigne.ShouldBeFalse();
        Fabrique.Creneau(urgence: false, enLigne: true).OuvertEnLigne.ShouldBeTrue();
    }

    [Fact]
    public void Un_creneau_d_urgence_se_libere_a_l_approche_de_son_horaire()
    {
        var creneau = Fabrique.Creneau(urgence: true);
        var liberation = TimeSpan.FromHours(24);

        creneau.EstOuvertAuxReservations(creneau.Debut.AddDays(-5), liberation).ShouldBeFalse();
        creneau.EstOuvertAuxReservations(creneau.Debut.AddHours(-23), liberation).ShouldBeTrue();
        creneau.EstOuvertAuxReservations(creneau.Debut.AddMinutes(1), liberation).ShouldBeFalse();
    }

    [Fact]
    public void Un_creneau_reserve_ne_peut_etre_ni_bloque_ni_retire()
    {
        var creneau = Fabrique.Creneau();
        creneau.Reserver();

        Should.Throw<DomainException>(creneau.Bloquer);
        Should.Throw<DomainException>(creneau.Retirer);
    }

    [Fact]
    public void Retirer_un_creneau_libere_ses_occupations()
    {
        var creneau = Fabrique.Creneau(associees: [Guid.CreateVersion7()]);

        creneau.Retirer();

        creneau.Occupations.ShouldBeEmpty();
    }

    [Fact]
    public void Un_creneau_bloque_n_est_pas_reservable_jusqu_au_deblocage()
    {
        var creneau = Fabrique.Creneau();
        creneau.Bloquer();

        creneau.EstOuvertAuxReservations(Fabrique.Maintenant, TimeSpan.Zero).ShouldBeFalse();
        creneau.Debloquer();
        creneau.EstOuvertAuxReservations(Fabrique.Maintenant, TimeSpan.Zero).ShouldBeTrue();
    }
}
