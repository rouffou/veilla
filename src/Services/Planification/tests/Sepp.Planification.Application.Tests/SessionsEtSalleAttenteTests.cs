using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.SalleAttente;
using Sepp.Planification.Application.Sessions;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;
using Sepp.Planification.Domain.Sessions;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>PLA-04, PLA-05 : propositions de sessions et tournées ; PLA-08 : salle d'attente.</summary>
public class SessionsEtSalleAttenteTests
{
    private static ProposerSessionsHandler Proposeur(Harness h) => new(h.Store, h.Store, h.Parametres, Harness.Planificateur);

    private static Lieu CabinetDe(Harness h, Guid affilie, string nom, double latitude, double longitude)
    {
        var lieu = Lieu.Creer(TypeLieu.CabinetEntreprise, nom, null, "5000", new Coordonnees(latitude, longitude), affilie, null);
        h.Store.Lieux.Add(lieu);
        return lieu;
    }

    [Fact]
    public async Task Les_propositions_regroupent_les_obligations_par_personne_et_excluent_les_urgences()
    {
        var h = new Harness();
        CabinetDe(h, h.Affilie, "Cabinet X", 50.46, 4.87);
        var autre = Guid.CreateVersion7();
        h.AjouterObligation(dateDue: Harness.Lundi);
        h.AjouterObligation(dateDue: Harness.Lundi.AddDays(10));
        h.AjouterObligation(personne: autre, dateDue: Harness.Lundi);
        h.AjouterObligation("EXAMEN_REPRISE", personne: Guid.CreateVersion7(), dateDue: Harness.Lundi);

        var resultat = await Proposeur(h).HandleAsync(new ProposerSessions(h.Affilie, null, Harness.Lundi, Harness.Lundi.AddDays(90), 10, null), CancellationToken.None);

        var session = resultat.Value.Sessions.ShouldHaveSingleItem();
        session.PersonneIds.Count.ShouldBe(2);
        session.ObligationIds.Count.ShouldBe(3);
        session.Date.ShouldBe(Harness.Lundi);
        resultat.Value.Methode.ShouldContain("plus proche voisin");
    }

    [Fact]
    public async Task Les_propositions_par_zone_visitent_les_sites_du_plus_proche_au_plus_loin()
    {
        var h = new Harness();
        var proche = Guid.CreateVersion7();
        var loin = Guid.CreateVersion7();
        var depart = Lieu.Creer(TypeLieu.CentreFixe, "Centre de Namur", null, "5000", new Coordonnees(50.4674, 4.8720), null, null);
        h.Store.Lieux.Add(depart);
        var cabinetLoin = CabinetDe(h, loin, "Cabinet Liège", 50.6326, 5.5797);
        var cabinetProche = CabinetDe(h, proche, "Cabinet Charleroi", 50.4108, 4.4446);
        h.AjouterObligation(affilie: loin, personne: Guid.CreateVersion7());
        h.AjouterObligation(affilie: proche, personne: Guid.CreateVersion7());

        var resultat = await Proposeur(h).HandleAsync(new ProposerSessions(null, "50", Harness.Lundi, Harness.Lundi.AddDays(30), 10, depart.Id), CancellationToken.None);

        resultat.Value.Sessions.Select(s => s.LieuId).ShouldBe([cabinetProche.Id, cabinetLoin.Id]);
        resultat.Value.DistanceTotaleKm.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Les_propositions_exigent_un_critere_et_comptent_les_personnes_sans_lieu()
    {
        var h = new Harness();
        h.AjouterObligation();

        (await Proposeur(h).HandleAsync(new ProposerSessions(null, null, Harness.Lundi, Harness.Lundi, 10, null), CancellationToken.None)).Error!.Code.ShouldBe("propositions.critere");
        (await Proposeur(h).HandleAsync(new ProposerSessions(h.Affilie, null, Harness.Lundi, Harness.Lundi, 0, null), CancellationToken.None)).Error!.Code.ShouldBe("propositions.parametres");
        var sansLieu = await Proposeur(h).HandleAsync(new ProposerSessions(h.Affilie, null, Harness.Lundi, Harness.Lundi.AddDays(30), 10, null), CancellationToken.None);
        sansLieu.Value.Sessions.ShouldBeEmpty();
        sansLieu.Value.PersonnesSansLieu.ShouldBe(1);
    }

    private static CreerSessionHandler Createur(Harness h) => new(h.Store, h.Store, h.Store, h.Store, h.Store, h.Parametres, h.Store, Harness.Planificateur);

    private static (Ressource Unite, Ressource Chauffeur, Lieu Lieu) Tournee(Harness h)
    {
        var unite = h.AjouterRessource(TypeRessource.UniteMobile, "Car 1");
        var chauffeur = h.AjouterRessource(TypeRessource.Chauffeur, "Chauffeur 1", "kc-chauffeur");
        var lieu = Lieu.Creer(TypeLieu.UniteMobile, "Car chez X", null, "5000", null, h.Affilie, null);
        h.Store.Lieux.Add(lieu);
        return (unite, chauffeur, lieu);
    }

    [Fact]
    public async Task Une_tournee_cree_ses_creneaux_et_mobilise_conseiller_unite_et_chauffeur()
    {
        var h = new Harness();
        var (unite, chauffeur, lieu) = Tournee(h);
        h.Store.Durees.Add(DureeStandard.Creer("EVALUATION_PERIODIQUE", null, 30));

        var resultat = await Createur(h).HandleAsync(
            new CreerSession(lieu.Id, h.Affilie, Harness.Lundi, h.Conseiller.Id, unite.Id, chauffeur.Id, 4, "evaluation-periodique", "08:00", null,
                [new NouvelleEtape("Parking visiteurs", "07:30", true, false, true)]),
            CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        var session = h.Store.Sessions.ShouldHaveSingleItem();
        session.EstTournee.ShouldBeTrue();
        session.Itineraire.ShouldHaveSingleItem().RaccordementReseau.ShouldBeTrue();
        h.Store.Creneaux.Count.ShouldBe(4);
        h.Store.Creneaux.ShouldAllBe(c => c.SessionId == session.Id && c.Mobilise(unite.Id) && c.Mobilise(chauffeur.Id) && c.Mobilise(h.Conseiller.Id));
        h.Store.Creneaux.Min(c => c.Debut).ShouldBe(Harness.Instant(Harness.Lundi, 8));
        h.Store.Creneaux.Max(c => c.Fin).ShouldBe(Harness.Instant(Harness.Lundi, 10));
    }

    [Fact]
    public async Task Une_tournee_en_conflit_avec_une_ressource_deja_occupee_est_refusee()
    {
        var h = new Harness();
        var (unite, chauffeur, lieu) = Tournee(h);
        h.AjouterCreneau(Harness.Lundi, 9, ressource: h.AjouterRessource(TypeRessource.Conseiller, "Dr B."), associees: [unite.Id]);

        var resultat = await Createur(h).HandleAsync(
            new CreerSession(lieu.Id, h.Affilie, Harness.Lundi, h.Conseiller.Id, unite.Id, chauffeur.Id, 4, "EVALUATION_PERIODIQUE", "08:00", 30, null), CancellationToken.None);

        resultat.Error!.Code.ShouldBe("session.conflit-ressources");
        h.Store.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_tournee_exige_unite_et_chauffeur_un_jour_ouvrable_et_une_duree_connue()
    {
        var h = new Harness();
        var (unite, chauffeur, lieu) = Tournee(h);

        (await Createur(h).HandleAsync(new CreerSession(lieu.Id, h.Affilie, Harness.Lundi, h.Conseiller.Id, null, null, 4, "EVALUATION_PERIODIQUE", "08:00", 30, null), CancellationToken.None))
            .Error!.Code.ShouldBe("session.unite-mobile");
        (await Createur(h).HandleAsync(new CreerSession(lieu.Id, h.Affilie, Harness.Lundi, h.Conseiller.Id, unite.Id, null, 4, "EVALUATION_PERIODIQUE", "08:00", 30, null), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await Createur(h).HandleAsync(new CreerSession(lieu.Id, h.Affilie, new DateOnly(2027, 3, 14), h.Conseiller.Id, unite.Id, chauffeur.Id, 4, "EVALUATION_PERIODIQUE", "08:00", 30, null), CancellationToken.None))
            .Error!.Code.ShouldBe("session.jour-non-ouvrable");
        (await Createur(h).HandleAsync(new CreerSession(lieu.Id, h.Affilie, Harness.Lundi, h.Conseiller.Id, unite.Id, chauffeur.Id, 4, "EVALUATION_PERIODIQUE", "08:00", null, null), CancellationToken.None))
            .Error!.Code.ShouldBe("session.duree-inconnue");
        (await Createur(h).HandleAsync(new CreerSession(lieu.Id, h.Affilie, Harness.Lundi, unite.Id, unite.Id, chauffeur.Id, 4, "EVALUATION_PERIODIQUE", "08:00", 30, null), CancellationToken.None))
            .Error!.Code.ShouldBe("session.ressource-invalide");
    }

    [Fact]
    public async Task La_planification_d_une_session_place_les_personnes_et_convoque_par_lot()
    {
        var h = new Harness();
        var lieu = h.AjouterLieu("Cabinet X", h.Affilie);
        var session = Session.Creer(lieu.Id, h.Affilie, Harness.Lundi, h.Conseiller.Id, null, null, 2, "EVALUATION_PERIODIQUE", new TimeOnly(9, 0), 30);
        h.Store.Sessions.Add(session);
        foreach (var (debut, fin) in session.Horaires())
        {
            h.Store.Creneaux.Add(Creneau.Creer(h.Conseiller.Id, lieu.Id, debut, fin, "EVALUATION_PERIODIQUE", false, false, null, null, session.Id));
        }

        var p1 = Guid.CreateVersion7();
        var p2 = Guid.CreateVersion7();
        var p3 = Guid.CreateVersion7();
        var deux1 = h.AjouterObligation(personne: p1, dateDue: Harness.Lundi.AddDays(5));
        var deux2 = h.AjouterObligation(personne: p1, dateDue: Harness.Lundi.AddDays(6));
        h.AjouterObligation(personne: p2, dateDue: Harness.Lundi.AddDays(1));
        h.AjouterObligation(personne: p3, dateDue: Harness.Lundi.AddDays(20));
        h.AjouterObligation("EXAMEN_REPRISE", personne: Guid.CreateVersion7(), dateDue: Harness.Lundi);
        var handler = new PlanifierSessionHandler(h.Store, h.Store, h.Store, h.Prise, h.Parametres, h.Store, Harness.Planificateur);

        var resultat = await handler.HandleAsync(new PlanifierSession(session.Id, true, "Email"), CancellationToken.None);

        resultat.Value.RendezVous.ShouldBe(2);
        resultat.Value.PersonnesNonPlacees.ShouldBe(1);
        resultat.Value.CreneauxRestants.ShouldBe(0);
        resultat.Value.LotId.ShouldNotBeNull();
        h.Store.RendezVous.Select(r => r.PersonneId).ShouldBe([p2, p1], ignoreOrder: true);
        h.Store.RendezVous.Single(r => r.PersonneId == p1).ObligationIds.ShouldBe([deux1.ObligationId, deux2.ObligationId], ignoreOrder: true);
        h.Store.RendezVous.ShouldAllBe(r => r.Origine == OrigineRendezVous.Session);
        h.Store.Evenements<ConvocationEmise>().ShouldAllBe(c => c.LotId == resultat.Value.LotId && c.Canal == "Email");
        h.Store.Evenements<ConvocationEmise>().Count().ShouldBe(2);
    }

    // PLA-08 : salle d'attente.
    private static async Task<(RendezVous Premier, RendezVous Second)> DeuxRendezVous(Harness h, Lieu lieu)
    {
        var o1 = h.AjouterObligation(personne: Guid.CreateVersion7());
        var o2 = h.AjouterObligation(personne: Guid.CreateVersion7());
        var premier = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9, lieu: lieu), o1);
        var second = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9, 30, lieu: lieu, ressource: h.AjouterRessource(TypeRessource.Conseiller, "Dr B.")), o2);
        return (premier, second);
    }

    [Fact]
    public async Task La_file_d_attente_suit_l_heure_prevue_puis_l_heure_d_arrivee()
    {
        var h = new Harness();
        var (premier, second) = await DeuxRendezVous(h, h.Lieu);
        h.Horloge.Fixer(Harness.Instant(Harness.Lundi, 8, 40));
        var arrivee = new EnregistrerArriveeHandler(h.Store, h.Store, h.Horloge, Harness.AssistantMedical);

        // Le second rendez-vous (9 h 30) arrive avant le premier (9 h) : il ne passe pas devant.
        (await arrivee.HandleAsync(new EnregistrerArrivee(second.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Horloge.Avancer(TimeSpan.FromMinutes(5));
        (await arrivee.HandleAsync(new EnregistrerArrivee(premier.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var salle = await new ConsulterSalleAttenteHandler(h.Store, h.Horloge, Harness.AssistantMedical).HandleAsync(new ConsulterSalleAttente(h.Lieu.Id, null), CancellationToken.None);

        salle.Value.EnAttente.Select(e => e.RendezVousId).ShouldBe([premier.Id, second.Id]);
        salle.Value.EnAttente.Select(e => e.Position).ShouldBe([1, 2]);
        salle.Value.Attendus.ShouldBe(0);
    }

    [Fact]
    public async Task L_appel_en_salle_puis_la_fin_font_sortir_la_personne_de_la_file()
    {
        var h = new Harness();
        var (premier, second) = await DeuxRendezVous(h, h.Lieu);
        h.Horloge.Fixer(Harness.Instant(Harness.Lundi, 8, 50));
        var cabine = h.AjouterRessource(TypeRessource.Cabine, "Cabine 1");
        await new EnregistrerArriveeHandler(h.Store, h.Store, h.Horloge, Harness.AssistantMedical).HandleAsync(new EnregistrerArrivee(premier.Id), CancellationToken.None);

        var appel = new AppelerEnSalleHandler(h.Store, h.Store, h.Store, h.Horloge, Harness.AssistantMedical);
        (await appel.HandleAsync(new AppelerEnSalle(premier.Id, h.Conseiller.Id), CancellationToken.None)).Error!.Code.ShouldBe("salle.inconnue");
        (await appel.HandleAsync(new AppelerEnSalle(premier.Id, cabine.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var consultation = new ConsulterSalleAttenteHandler(h.Store, h.Horloge, Harness.AssistantMedical);
        var enSalle = (await consultation.HandleAsync(new ConsulterSalleAttente(h.Lieu.Id, null), CancellationToken.None)).Value;
        enSalle.EnAttente.ShouldBeEmpty();
        enSalle.Appeles.ShouldHaveSingleItem().SalleId.ShouldBe(cabine.Id);
        enSalle.Attendus.ShouldBe(1);

        (await new TerminerRendezVousHandler(h.Store, h.Store, Harness.AssistantMedical).HandleAsync(new TerminerRendezVous(premier.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        premier.Statut.ShouldBe(StatutRendezVous.Termine);
        (await new TerminerRendezVousHandler(h.Store, h.Store, Harness.AssistantMedical).HandleAsync(new TerminerRendezVous(second.Id), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task L_arrivee_ne_s_enregistre_que_le_jour_du_rendez_vous_et_par_le_personnel_d_accueil()
    {
        var h = new Harness();
        var (premier, _) = await DeuxRendezVous(h, h.Lieu);

        (await new EnregistrerArriveeHandler(h.Store, h.Store, h.Horloge, Harness.AssistantMedical).HandleAsync(new EnregistrerArrivee(premier.Id), CancellationToken.None))
            .Error!.Code.ShouldBe("rendez-vous.statut");
        h.Horloge.Fixer(Harness.Instant(Harness.Lundi, 8, 45));
        (await new EnregistrerArriveeHandler(h.Store, h.Store, h.Horloge, Harness.EmployeurUser).HandleAsync(new EnregistrerArrivee(premier.Id), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ConsulterSalleAttenteHandler(h.Store, h.Horloge, Harness.TravailleurUser).HandleAsync(new ConsulterSalleAttente(h.Lieu.Id, null), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
}
