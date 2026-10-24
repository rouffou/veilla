using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

/// <summary>POR-11 : déplacement en ligne, confirmation de présence et raison codée d'annulation.</summary>
public class RendezVousEnLigneTests
{
    [Fact]
    public void Un_deplacement_en_ligne_est_compte_et_efface_la_confirmation()
    {
        var ancien = Fabrique.Creneau();
        var nouveau = Fabrique.Creneau(Fabrique.Lundi.AddDays(1));
        var rdv = Fabrique.RendezVous(ancien);
        rdv.ConfirmerPresence(Fabrique.Maintenant);

        rdv.DeplacerEnLigne(ancien, nouveau, Fabrique.Maintenant, 2);

        rdv.NombreDeplacements.ShouldBe(1);
        rdv.PresenceConfirmeeLe.ShouldBeNull();
        rdv.CreneauId.ShouldBe(nouveau.Id);
        ancien.Statut.ShouldBe(StatutCreneau.Libre);
        nouveau.Statut.ShouldBe(StatutCreneau.Reserve);
    }

    [Fact]
    public void Le_nombre_maximal_de_deplacements_en_ligne_est_respecte()
    {
        var courant = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(courant);
        for (var i = 1; i <= 2; i++)
        {
            var suivant = Fabrique.Creneau(Fabrique.Lundi.AddDays(i));
            rdv.DeplacerEnLigne(courant, suivant, Fabrique.Maintenant, 2);
            courant = suivant;
        }

        var dernier = Fabrique.Creneau(Fabrique.Lundi.AddDays(3));
        Should.Throw<DomainException>(() => rdv.DeplacerEnLigne(courant, dernier, Fabrique.Maintenant, 2));

        dernier.Statut.ShouldBe(StatutCreneau.Libre);
        rdv.NombreDeplacements.ShouldBe(2);
    }

    [Fact]
    public void Un_deplacement_vers_un_creneau_deja_pris_ne_change_rien()
    {
        var ancien = Fabrique.Creneau();
        var pris = Fabrique.Creneau(Fabrique.Lundi.AddDays(1));
        pris.Reserver();
        var rdv = Fabrique.RendezVous(ancien);

        Should.Throw<DomainException>(() => rdv.DeplacerEnLigne(ancien, pris, Fabrique.Maintenant, 2));

        rdv.CreneauId.ShouldBe(ancien.Id);
        rdv.NombreDeplacements.ShouldBe(0);
        ancien.Statut.ShouldBe(StatutCreneau.Reserve);
    }

    [Fact]
    public void La_confirmation_est_idempotente_et_garde_la_premiere_date()
    {
        var rdv = Fabrique.RendezVous();

        rdv.ConfirmerPresence(Fabrique.Maintenant);
        rdv.ConfirmerPresence(Fabrique.Maintenant.AddHours(1));

        rdv.PresenceConfirmeeLe.ShouldBe(Fabrique.Maintenant);
    }

    [Fact]
    public void La_confirmation_est_refusee_pour_un_rendez_vous_passe_ou_annule()
    {
        var creneau = Fabrique.Creneau();
        var passe = Fabrique.RendezVous(creneau);
        Should.Throw<DomainException>(() => passe.ConfirmerPresence(creneau.Debut.AddMinutes(1)));

        var annule = Fabrique.RendezVous();
        annule.Annuler(MotifAnnulation.DemandeTravailleur, Fabrique.Creneau(), Fabrique.Maintenant);
        Should.Throw<DomainException>(() => annule.ConfirmerPresence(Fabrique.Maintenant));
    }

    [Fact]
    public void L_annulation_garde_la_raison_codee_et_la_precision_nettoyee()
    {
        var creneau = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(creneau);

        rdv.Annuler(MotifAnnulation.DemandeTravailleur, creneau, Fabrique.Maintenant, RaisonAnnulation.ProblemeTransport, "  Grève des trains.  ");

        rdv.RaisonAnnulation.ShouldBe(RaisonAnnulation.ProblemeTransport);
        rdv.CommentaireAnnulation.ShouldBe("Grève des trains.");
    }

    [Fact]
    public void Une_precision_trop_longue_est_refusee_et_n_annule_pas()
    {
        var creneau = Fabrique.Creneau();
        var rdv = Fabrique.RendezVous(creneau);

        Should.Throw<DomainException>(() =>
            rdv.Annuler(MotifAnnulation.DemandeTravailleur, creneau, Fabrique.Maintenant, RaisonAnnulation.Autre, new string('x', RendezVous.LongueurMaxCommentaire + 1)));

        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);
        creneau.Statut.ShouldBe(StatutCreneau.Reserve);
    }
}
