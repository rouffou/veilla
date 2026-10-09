using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Planification.Application.Agenda;
using Sepp.Planification.Application.Ressources;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>PLA-01, PLA-02, PLA-03 : ressources, lieux, modèles d'agenda, durées standard, congés RH.</summary>
public class AgendaEtRessourcesTests
{
    private static NouvellePlage Plage(string jour = "lundi", string debut = "09:00", string fin = "11:00", string type = "evaluation-periodique", int? duree = 30,
        bool urgence = false, bool enLigne = true) => new(jour, debut, fin, type, duree, urgence, enLigne, null);

    private static CreerModeleAgendaHandler Modeles(Harness h, ICurrentUser user) => new(h.Store, h.Store, h.Store, h.Store, h.Store, user);

    private static CreerModeleAgenda Modele(Harness h, DateOnly du, DateOnly? au = null, params NouvellePlage[] plages) =>
        new(h.Conseiller.Id, h.Lieu.Id, du, au, plages.Length > 0 ? plages : [Plage()]);

    [Fact]
    public async Task Un_nouveau_modele_cloture_le_precedent()
    {
        var h = new Harness();
        var handler = Modeles(h, Harness.Planificateur);

        var premier = await handler.HandleAsync(Modele(h, new DateOnly(2027, 1, 1)), CancellationToken.None);
        var second = await handler.HandleAsync(Modele(h, new DateOnly(2027, 6, 1), null, Plage("mardi")), CancellationToken.None);

        premier.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        var ancien = h.Store.Modeles.Single(m => m.Id == premier.Value);
        ancien.Validite.ValidTo.ShouldBe(new DateOnly(2027, 6, 1));
        h.Store.Modeles.Single(m => m.Id == second.Value).Validite.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_modele_qui_chevauche_un_modele_ulterieur_est_un_conflit()
    {
        var h = new Harness();
        var handler = Modeles(h, Harness.Planificateur);
        await handler.HandleAsync(Modele(h, new DateOnly(2027, 6, 1)), CancellationToken.None);

        var resultat = await handler.HandleAsync(Modele(h, new DateOnly(2027, 1, 1)), CancellationToken.None);

        resultat.Error!.Code.ShouldBe("modele-agenda.chevauchement");
    }

    [Fact]
    public async Task La_duree_d_une_plage_vient_de_la_duree_standard_du_cpmt_puis_de_la_duree_globale()
    {
        var h = new Harness();
        h.Store.Durees.Add(DureeStandard.Creer("EVALUATION_PERIODIQUE", null, 30));
        h.Store.Durees.Add(DureeStandard.Creer("EVALUATION_PERIODIQUE", h.Conseiller.Id, 20));
        var handler = Modeles(h, Harness.Planificateur);

        var id = (await handler.HandleAsync(Modele(h, new DateOnly(2027, 1, 1), null, Plage(duree: null)), CancellationToken.None)).Value;

        h.Store.Modeles.Single(m => m.Id == id).Plages.Single().DureeMinutes.ShouldBe(20);
    }

    [Fact]
    public async Task Sans_duree_standard_ni_duree_explicite_le_modele_est_refuse()
    {
        var h = new Harness();

        var resultat = await Modeles(h, Harness.Planificateur).HandleAsync(Modele(h, new DateOnly(2027, 1, 1), null, Plage(duree: null)), CancellationToken.None);

        resultat.Error!.Code.ShouldBe("modele-agenda.duree-inconnue");
        h.Store.Modeles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("funday", "09:00", "11:00")]
    [InlineData("lundi", "9h", "11:00")]
    [InlineData("lundi", "11:00", "09:00")]
    public async Task Une_plage_mal_formee_est_refusee(string jour, string debut, string fin)
    {
        var h = new Harness();

        var resultat = await Modeles(h, Harness.Planificateur).HandleAsync(Modele(h, new DateOnly(2027, 1, 1), null, Plage(jour, debut, fin)), CancellationToken.None);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Un_cpmt_ne_gere_que_les_modeles_et_durees_de_sa_propre_ressource()
    {
        var h = new Harness();
        var moi = new FakeUser(Roles.Cpmt) { UserId = "kc-a" };
        var autre = new FakeUser(Roles.Cpmt) { UserId = "kc-z" };

        (await Modeles(h, moi).HandleAsync(Modele(h, new DateOnly(2027, 1, 1)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await Modeles(h, autre).HandleAsync(Modele(h, new DateOnly(2027, 9, 1)), CancellationToken.None)).Error!.Code.ShouldBe("modele-agenda.ressource-d-autrui");

        var durees = new DefinirDureeStandardHandler(h.Store, h.Store, h.Store, moi);
        (await durees.HandleAsync(new DefinirDureeStandard("evaluation-periodique", 25, h.Conseiller.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await durees.HandleAsync(new DefinirDureeStandard("evaluation-periodique", 25, null), CancellationToken.None)).Error!.Code.ShouldBe("duree-standard.globale");
        (await new DefinirDureeStandardHandler(h.Store, h.Store, h.Store, autre).HandleAsync(new DefinirDureeStandard("evaluation-periodique", 25, h.Conseiller.Id), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Le_responsable_de_centre_definit_la_duree_globale_et_la_modifie_sans_doublon()
    {
        var h = new Harness();
        var durees = new DefinirDureeStandardHandler(h.Store, h.Store, h.Store, Harness.Planificateur);

        var premiere = await durees.HandleAsync(new DefinirDureeStandard("evaluation-periodique", 30, null), CancellationToken.None);
        var seconde = await durees.HandleAsync(new DefinirDureeStandard("EVALUATION_PERIODIQUE", 40, null), CancellationToken.None);

        seconde.Value.ShouldBe(premiere.Value);
        h.Store.Durees.ShouldHaveSingleItem().DureeMinutes.ShouldBe(40);
        (await durees.HandleAsync(new DefinirDureeStandard("EVALUATION_PERIODIQUE", 2, null), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    private static async Task<Guid> ModeleEnPlace(Harness h)
    {
        var resultat = await Modeles(h, Harness.Planificateur).HandleAsync(
            Modele(h, new DateOnly(2027, 1, 1), null, Plage("lundi", "09:00", "10:00", duree: 30), Plage("lundi", "14:00", "15:00", "EXAMEN_REPRISE", 20, urgence: true)),
            CancellationToken.None);
        return resultat.Value;
    }

    private static GenererCreneauxHandler Generateur(Harness h, ICurrentUser? user = null) =>
        new(h.Store, h.Store, h.Store, h.Store, h.Parametres, h.Store, user ?? Harness.Planificateur);

    [Fact]
    public async Task La_generation_cree_les_creneaux_du_modele_hors_jours_feries()
    {
        var h = new Harness();
        var modele = await ModeleEnPlace(h);

        // Trois lundis : 15, 22 et 29 mars 2027 ; le 29 est le lundi de Pâques (férié).
        var resultat = await Generateur(h).HandleAsync(new GenererCreneaux(modele, new DateOnly(2027, 3, 15), new DateOnly(2027, 3, 31)), CancellationToken.None);

        resultat.Value.Crees.ShouldBe(10);
        h.Store.Creneaux.Count.ShouldBe(10);
        h.Store.Creneaux.ShouldAllBe(c => c.ModeleAgendaId == modele && c.RessourceId == h.Conseiller.Id);
        h.Store.Creneaux.Count(c => c.ReserveUrgence).ShouldBe(6);
        h.Store.Creneaux.Min(c => c.Debut).ShouldBe(Harness.Instant(new DateOnly(2027, 3, 15), 9));
    }

    [Fact]
    public async Task La_generation_est_idempotente()
    {
        var h = new Harness();
        var modele = await ModeleEnPlace(h);
        var commande = new GenererCreneaux(modele, new DateOnly(2027, 3, 15), new DateOnly(2027, 3, 21));
        await Generateur(h).HandleAsync(commande, CancellationToken.None);

        var seconde = await Generateur(h).HandleAsync(commande, CancellationToken.None);

        seconde.Value.Crees.ShouldBe(0);
        seconde.Value.DejaExistants.ShouldBe(5);
        h.Store.Creneaux.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Un_conge_bloque_les_creneaux_generes_et_un_chevauchement_est_signale()
    {
        var h = new Harness();
        var modele = await ModeleEnPlace(h);
        h.Store.Absences.Add(Absence.Creer(h.Conseiller.Id, Harness.Instant(Harness.Lundi, 8), Harness.Instant(Harness.Lundi, 10), SourceAbsence.OutilRh, "RH-1"));
        var existant = h.AjouterCreneau(Harness.Lundi, 14, 10, "AUTRE", duree: 30);

        var resultat = await Generateur(h).HandleAsync(new GenererCreneaux(modele, Harness.Lundi, Harness.Lundi), CancellationToken.None);

        resultat.Value.Bloques.ShouldBe(2);
        resultat.Value.Conflits.Count.ShouldBe(2);
        h.Store.Creneaux.Count(c => c.Statut == StatutCreneau.Bloque).ShouldBe(2);
        h.Store.Creneaux.ShouldContain(existant);
    }

    [Fact]
    public async Task La_generation_est_limitee_a_92_jours_et_reservee_au_planificateur()
    {
        var h = new Harness();
        var modele = await ModeleEnPlace(h);

        (await Generateur(h).HandleAsync(new GenererCreneaux(modele, new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 1)), CancellationToken.None)).Error!.Code.ShouldBe("creneaux.periode-invalide");
        (await Generateur(h, Harness.TravailleurUser).HandleAsync(new GenererCreneaux(modele, Harness.Lundi, Harness.Lundi), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Generateur(h).HandleAsync(new GenererCreneaux(Guid.CreateVersion7(), Harness.Lundi, Harness.Lundi), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Un_creneau_reserve_ne_se_retire_pas_un_creneau_libre_oui()
    {
        var h = new Harness();
        var libre = h.AjouterCreneau(Harness.Lundi, 9);
        var reserve = h.AjouterCreneau(Harness.Lundi, 10);
        await h.PlanifierRendezVous(reserve);
        var handler = new RetirerCreneauHandler(h.Store, h.Store, Harness.Planificateur);

        (await handler.HandleAsync(new RetirerCreneau(reserve.Id), CancellationToken.None)).Error!.Code.ShouldBe("creneau.reserve");
        (await handler.HandleAsync(new RetirerCreneau(libre.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        h.Store.Creneaux.ShouldNotContain(libre);
        h.Store.Creneaux.ShouldContain(reserve);
    }

    // PLA-01, PLA-02 : ressources et lieux.
    [Fact]
    public async Task Le_responsable_de_centre_declare_lieux_et_ressources_avec_leurs_competences()
    {
        var h = new Harness();
        var user = Harness.ResponsableCentre;

        var lieu = await new CreerLieuHandler(h.Store, h.Store, user).HandleAsync(
            new CreerLieu("UniteMobile", "Car 1", null, "4000", 50.63, 5.57, h.Affilie, null), CancellationToken.None);
        var ressource = await new CreerRessourceHandler(h.Store, h.Store, h.Store, user).HandleAsync(
            new CreerRessource("appareil", "Audiomètre AU-12", "INV-12", ["audiometrie", "AUDIOMETRIE"], lieu.Value), CancellationToken.None);
        await new DefinirCompetencesHandler(h.Store, h.Store, user).HandleAsync(new DefinirCompetences(ressource.Value, ["spirometrie"]), CancellationToken.None);

        lieu.IsSuccess.ShouldBeTrue();
        h.Store.Lieux.Single(l => l.Id == lieu.Value).Type.ShouldBe(TypeLieu.UniteMobile);
        h.Store.Ressources.Single(r => r.Id == ressource.Value).Competences.ShouldBe(["SPIROMETRIE"]);
    }

    [Fact]
    public async Task Lieux_et_ressources_exigent_la_permission_ressources_et_des_donnees_valides()
    {
        var h = new Harness();

        (await new CreerLieuHandler(h.Store, h.Store, Harness.Planificateur).HandleAsync(new CreerLieu("CentreFixe", "X", null, null, null, null, null, null), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        var responsable = Harness.ResponsableCentre;
        (await new CreerLieuHandler(h.Store, h.Store, responsable).HandleAsync(new CreerLieu("Bateau", "X", null, null, null, null, null, null), CancellationToken.None))
            .Error!.Code.ShouldBe("lieu.type-inconnu");
        (await new CreerLieuHandler(h.Store, h.Store, responsable).HandleAsync(new CreerLieu("CentreFixe", "X", null, null, 50.0, null, null, null), CancellationToken.None))
            .Error!.Code.ShouldBe("lieu.position-incomplete");
        (await new CreerRessourceHandler(h.Store, h.Store, h.Store, responsable).HandleAsync(new CreerRessource("robot", "R", null, null, null), CancellationToken.None))
            .Error!.Code.ShouldBe("ressource.type-inconnu");
        (await new CreerRessourceHandler(h.Store, h.Store, h.Store, responsable).HandleAsync(new CreerRessource("Salle", "S", null, null, Guid.CreateVersion7()), CancellationToken.None))
            .Error!.Code.ShouldBe("lieu.inconnu");
    }

    // PLA-03 : congés RH en lecture seule.
    [Fact]
    public async Task L_import_des_conges_bloque_les_creneaux_libres_et_signale_les_creneaux_reserves()
    {
        var h = new Harness();
        var libre = h.AjouterCreneau(Harness.Lundi, 9);
        var reserve = h.AjouterCreneau(Harness.Lundi, 10);
        await h.PlanifierRendezVous(reserve);
        var rh = new FakeOutilRh(
            new CongeRh("RH-1", "kc-a", Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0)),
            new CongeRh("RH-2", "inconnu", Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0)));
        var handler = new ImporterCongesHandler(rh, h.Store, h.Store, h.Indisponibilites, h.Store, Harness.ResponsableCentre);

        var resultat = await handler.HandleAsync(new ImporterConges(Harness.Lundi, Harness.Lundi), CancellationToken.None);

        resultat.Value.Lus.ShouldBe(2);
        resultat.Value.Crees.ShouldBe(1);
        resultat.Value.RessourcesInconnues.ShouldBe(1);
        resultat.Value.CreneauxBloques.ShouldBe(1);
        resultat.Value.CreneauxReservesEnConflit.ShouldBe([reserve.Id]);
        libre.Statut.ShouldBe(StatutCreneau.Bloque);
        reserve.Statut.ShouldBe(StatutCreneau.Reserve);
        h.Store.Absences.ShouldHaveSingleItem().Source.ShouldBe(SourceAbsence.OutilRh);
    }

    [Fact]
    public async Task Reimporter_les_memes_conges_ne_change_rien_et_une_periode_modifiee_est_actualisee()
    {
        var h = new Harness();
        var debut = Harness.Instant(Harness.Lundi, 0);
        var handler = (FakeOutilRh rh) => new ImporterCongesHandler(rh, h.Store, h.Store, h.Indisponibilites, h.Store, Harness.ResponsableCentre);
        var commande = new ImporterConges(Harness.Lundi, Harness.Lundi.AddDays(10));

        await handler(new FakeOutilRh(new CongeRh("RH-1", "kc-a", debut, debut.AddDays(1)))).HandleAsync(commande, CancellationToken.None);
        var identique = await handler(new FakeOutilRh(new CongeRh("RH-1", "kc-a", debut, debut.AddDays(1)))).HandleAsync(commande, CancellationToken.None);
        var modifie = await handler(new FakeOutilRh(new CongeRh("RH-1", "KC-A", debut, debut.AddDays(3)))).HandleAsync(commande, CancellationToken.None);

        identique.Value.Crees.ShouldBe(0);
        identique.Value.Actualises.ShouldBe(0);
        modifie.Value.Actualises.ShouldBe(1);
        h.Store.Absences.ShouldHaveSingleItem().Fin.ShouldBe(debut.AddDays(3));
    }

    [Fact]
    public async Task L_import_des_conges_est_reserve_au_responsable_de_centre_et_borne_dans_le_temps()
    {
        var h = new Harness();
        var rh = new FakeOutilRh();

        (await new ImporterCongesHandler(rh, h.Store, h.Store, h.Indisponibilites, h.Store, Harness.Planificateur).HandleAsync(new ImporterConges(Harness.Lundi, Harness.Lundi), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ImporterCongesHandler(rh, h.Store, h.Store, h.Indisponibilites, h.Store, Harness.ResponsableCentre).HandleAsync(new ImporterConges(Harness.Lundi, Harness.Lundi.AddYears(2)), CancellationToken.None))
            .Error!.Code.ShouldBe("conges.periode-invalide");
    }

    [Fact]
    public async Task Les_lectures_exigent_la_permission_de_lecture()
    {
        var h = new Harness();

        (await new ListerRessourcesHandler(h.Store, Harness.TravailleurUser).HandleAsync(new ListerRessources(null), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ListerRessourcesHandler(h.Store, Harness.AssistantMedical).HandleAsync(new ListerRessources("conseiller"), CancellationToken.None)).Value.ShouldHaveSingleItem();
        (await new ListerRessourcesHandler(h.Store, Harness.AssistantMedical).HandleAsync(new ListerRessources("robot"), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await new ListerCreneauxHandler(h.Store, Harness.EmployeurUser).HandleAsync(
            new ListerCreneaux(Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0), null, null, null), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
}
