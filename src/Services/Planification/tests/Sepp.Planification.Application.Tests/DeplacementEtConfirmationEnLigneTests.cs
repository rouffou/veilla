using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.Reservations;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>POR-11 : déplacement atomique, confirmation de présence et raison codée d'annulation.</summary>
public class DeplacementEtConfirmationEnLigneTests
{
    private static DeplacerEnLigneHandler Deplacer(Harness h, IPerimetreUtilisateur perimetre, ICurrentUser user) =>
        new(h.Store, h.Store, h.Store, h.Prise, h.Store, perimetre, h.Parametres, h.Store, h.Horloge, user);

    private static ConfirmerPresenceHandler Confirmer(Harness h, IPerimetreUtilisateur perimetre, ICurrentUser user) =>
        new(h.Store, perimetre, h.Store, h.Horloge, user);

    private static async Task<(Harness H, RendezVous Rdv, Creneau Ancien)> UnRendezVousAsync(int heure = 9)
    {
        var h = new Harness();
        var ancien = h.AjouterCreneau(Harness.Lundi, heure, enLigne: true);
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(ancien, obligation);
        return (h, rdv, ancien);
    }

    [Fact]
    public async Task Le_travailleur_deplace_son_rendez_vous_en_un_appel_et_l_evenement_est_publie()
    {
        var (h, rdv, ancien) = await UnRendezVousAsync();
        var nouveau = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);
        h.Store.Evenements<RendezVousReplanifie>().ShouldBeEmpty();

        var resultat = await Deplacer(h, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser)
            .HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        resultat.Value.CreneauId.ShouldBe(nouveau.Id);
        resultat.Value.NombreDeplacements.ShouldBe(1);
        ancien.Statut.ShouldBe(StatutCreneau.Libre);
        nouveau.Statut.ShouldBe(StatutCreneau.Reserve);
        var evenement = h.Store.Evenements<RendezVousReplanifie>().ShouldHaveSingleItem();
        evenement.RendezVousId.ShouldBe(rdv.Id);
        evenement.Motif.ShouldBe("DemandeTravailleur");
        evenement.NouveauDebut.ShouldBe(nouveau.Debut);
        h.Store.Evenements<ConvocationEmise>().Last().TypeConvocation.ShouldBe("Replanification");
        h.Store.Obligations.ShouldAllBe(o => !o.EstAPlanifier);
    }

    [Fact]
    public async Task L_employeur_deplace_dans_son_perimetre_et_pas_hors_perimetre()
    {
        var (h, rdv, _) = await UnRendezVousAsync();
        var nouveau = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);

        var autre = await Deplacer(h, FakePerimetre.Employeur(Guid.CreateVersion7()), Harness.EmployeurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);
        var sien = await Deplacer(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);

        autre.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        sien.IsSuccess.ShouldBeTrue();
        h.Store.Evenements<RendezVousReplanifie>().ShouldHaveSingleItem().Motif.ShouldBe("DemandeEmployeur");
    }

    [Fact]
    public async Task Un_autre_travailleur_ou_un_interne_ne_deplace_pas_ce_rendez_vous()
    {
        var (h, rdv, ancien) = await UnRendezVousAsync();
        var nouveau = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);

        var tiers = await Deplacer(h, FakePerimetre.Travailleur(Guid.CreateVersion7()), Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);
        var interne = await Deplacer(h, FakePerimetre.Interne, Harness.Planificateur).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);

        tiers.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        interne.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        ancien.Statut.ShouldBe(StatutCreneau.Reserve);
        nouveau.Statut.ShouldBe(StatutCreneau.Libre);
    }

    [Fact]
    public async Task Le_deplacement_tardif_est_refuse_selon_le_delai_configure()
    {
        var (h, rdv, ancien) = await UnRendezVousAsync();
        var nouveau = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);
        h.Horloge.Fixer(ancien.Debut.AddHours(-23));
        var travailleur = FakePerimetre.Travailleur(h.Personne);

        var tardif = await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);
        h.Options.DelaiMinimalDeplacement = TimeSpan.FromHours(12);
        var assoupli = await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);

        tardif.Error!.Code.ShouldBe("rendez-vous.deplacement-tardif");
        assoupli.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Le_nombre_maximal_de_deplacements_vient_de_la_configuration()
    {
        var (h, rdv, _) = await UnRendezVousAsync();
        h.Options.NombreMaximalDeplacements = 1;
        var travailleur = FakePerimetre.Travailleur(h.Personne);
        var premier = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);
        var second = h.AjouterCreneau(Harness.Lundi.AddDays(2), 10, enLigne: true);

        (await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, premier.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var refus = await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, second.Id), CancellationToken.None);

        refus.Error!.Code.ShouldBe("rendez-vous.deplacements-epuises");
        second.Statut.ShouldBe(StatutCreneau.Libre);
        h.Store.Evenements<RendezVousReplanifie>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Un_creneau_deja_pris_donne_un_conflit_et_laisse_le_rendez_vous_intact()
    {
        var (h, rdv, ancien) = await UnRendezVousAsync();
        var pris = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);
        pris.Reserver();

        var resultat = await Deplacer(h, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, pris.Id), CancellationToken.None);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        rdv.CreneauId.ShouldBe(ancien.Id);
        ancien.Statut.ShouldBe(StatutCreneau.Reserve);
        h.Store.Evenements<RendezVousReplanifie>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_creneau_cible_doit_etre_ouvert_en_ligne_du_meme_type_et_different()
    {
        var (h, rdv, ancien) = await UnRendezVousAsync();
        var travailleur = FakePerimetre.Travailleur(h.Personne);
        var ferme = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: false);
        var autreType = h.AjouterCreneau(Harness.Lundi.AddDays(1), 11, enLigne: true, typeActe: "EXAMEN_REPRISE");
        var chezUnAutre = h.AjouterCreneau(Harness.Lundi.AddDays(1), 12, enLigne: true, lieu: h.AjouterLieu("Cabinet d'un autre", Guid.CreateVersion7()));

        (await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, ferme.Id), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, chezUnAutre.Id), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, autreType.Id), CancellationToken.None)).Error!.Code.ShouldBe("deplacement.creneau-invalide");
        (await Deplacer(h, travailleur, Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, ancien.Id), CancellationToken.None)).Error!.Code.ShouldBe("deplacement.creneau-invalide");
    }

    [Fact]
    public async Task Un_rendez_vous_annule_ne_se_deplace_pas()
    {
        var (h, rdv, ancien) = await UnRendezVousAsync();
        var nouveau = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, enLigne: true);
        rdv.Annuler(MotifAnnulation.Autre, ancien, h.Horloge.GetUtcNow());

        var resultat = await Deplacer(h, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser).HandleAsync(new DeplacerEnLigne(rdv.Id, nouveau.Id), CancellationToken.None);

        resultat.Error!.Code.ShouldBe("rendez-vous.statut");
    }

    [Fact]
    public async Task Le_travailleur_confirme_sa_presence_et_le_planificateur_la_voit()
    {
        var (h, rdv, _) = await UnRendezVousAsync();

        var resultat = await Confirmer(h, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser).HandleAsync(new ConfirmerPresence(rdv.Id), CancellationToken.None);
        var vuParLePlanificateur = await new Application.PriseRendezVous.ObtenirRendezVousHandler(h.Store, FakePerimetre.Interne, Harness.Planificateur)
            .HandleAsync(new Application.PriseRendezVous.ObtenirRendezVous(rdv.Id), CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        vuParLePlanificateur.Value.PresenceConfirmeeLe.ShouldBe(h.Horloge.GetUtcNow());
        h.Store.Evenements<RendezVousReplanifie>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Seul_le_travailleur_concerne_confirme_sa_presence()
    {
        var (h, rdv, _) = await UnRendezVousAsync();

        var employeur = await Confirmer(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser).HandleAsync(new ConfirmerPresence(rdv.Id), CancellationToken.None);
        var tiers = await Confirmer(h, FakePerimetre.Travailleur(Guid.CreateVersion7()), Harness.TravailleurUser).HandleAsync(new ConfirmerPresence(rdv.Id), CancellationToken.None);
        var interne = await Confirmer(h, FakePerimetre.Interne, Harness.Planificateur).HandleAsync(new ConfirmerPresence(rdv.Id), CancellationToken.None);

        employeur.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        tiers.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        interne.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        rdv.PresenceConfirmeeLe.ShouldBeNull();
    }

    [Fact]
    public async Task L_annulation_en_ligne_enregistre_la_raison_codee_et_ne_publie_pas_le_texte_libre()
    {
        var (h, rdv, _) = await UnRendezVousAsync();
        var handler = new AnnulerEnLigneHandler(h.Store, h.Annulation, FakePerimetre.Travailleur(h.Personne), h.Store, h.Horloge, Harness.TravailleurUser);

        var resultat = await handler.HandleAsync(new AnnulerEnLigne(rdv.Id, "conflit-horaire", "Réunion déplacée"), CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        rdv.RaisonAnnulation.ShouldBe(RaisonAnnulation.ConflitHoraire);
        rdv.CommentaireAnnulation.ShouldBe("Réunion déplacée");
        var evenement = h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem();
        evenement.Motif.ShouldBe("DemandeTravailleur");
        evenement.Raison.ShouldBe("ConflitHoraire");
        System.Text.Json.JsonSerializer.Serialize(evenement).ShouldNotContain("Réunion");
    }

    [Fact]
    public async Task L_annulation_sans_raison_reste_possible_et_une_raison_inconnue_ou_un_texte_trop_long_est_refuse()
    {
        var (h, rdv, _) = await UnRendezVousAsync();
        var handler = new AnnulerEnLigneHandler(h.Store, h.Annulation, FakePerimetre.Travailleur(h.Personne), h.Store, h.Horloge, Harness.TravailleurUser);

        (await handler.HandleAsync(new AnnulerEnLigne(rdv.Id, "Inconnue"), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new AnnulerEnLigne(rdv.Id, "Autre", new string('x', 201)), CancellationToken.None)).Error!.Code.ShouldBe("rendez-vous.commentaire-trop-long");
        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);

        (await handler.HandleAsync(new AnnulerEnLigne(rdv.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem().Raison.ShouldBeNull();
    }

    [Fact]
    public async Task Le_texte_libre_d_annulation_est_masque_aux_externes()
    {
        var (h, rdv, _) = await UnRendezVousAsync();
        await new AnnulerEnLigneHandler(h.Store, h.Annulation, FakePerimetre.Travailleur(h.Personne), h.Store, h.Horloge, Harness.TravailleurUser)
            .HandleAsync(new AnnulerEnLigne(rdv.Id, "Autre", "Précision"), CancellationToken.None);

        var employeur = await new Application.PriseRendezVous.ObtenirRendezVousHandler(h.Store, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser)
            .HandleAsync(new Application.PriseRendezVous.ObtenirRendezVous(rdv.Id), CancellationToken.None);
        var interne = await new Application.PriseRendezVous.ObtenirRendezVousHandler(h.Store, FakePerimetre.Interne, Harness.Planificateur)
            .HandleAsync(new Application.PriseRendezVous.ObtenirRendezVous(rdv.Id), CancellationToken.None);

        employeur.Value.CommentaireAnnulation.ShouldBeNull();
        employeur.Value.RaisonAnnulation.ShouldBe(RaisonAnnulation.Autre);
        interne.Value.CommentaireAnnulation.ShouldBe("Précision");
    }
}
