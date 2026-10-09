using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Sessions;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

/// <summary>PLA-04, PLA-05 : heuristique de proposition de sessions et tournées.</summary>
public class SessionsTests
{
    private static readonly BusinessCalendar Calendrier = BusinessCalendar.Belgian(2027);

    private static BesoinPersonne Personne(DateOnly? limite = null) =>
        new(Guid.CreateVersion7(), Fabrique.Affilie, [Guid.CreateVersion7()], limite);

    private static BesoinSite Site(Coordonnees? position, params BesoinPersonne[] personnes) => new(Guid.CreateVersion7(), position, personnes);

    // Namur, Liège (80 km de Namur), Charleroi (35 km de Namur), Bruxelles.
    private static readonly Coordonnees Namur = new(50.4674, 4.8720);
    private static readonly Coordonnees Charleroi = new(50.4108, 4.4446);
    private static readonly Coordonnees Liege = new(50.6326, 5.5797);

    [Fact]
    public void Les_sites_sont_visites_du_plus_proche_voisin()
    {
        var charleroi = Site(Charleroi, Personne());
        var liege = Site(Liege, Personne());

        var propositions = PlanificateurSessions.Proposer([liege, charleroi], Namur, Fabrique.Lundi, 10, Calendrier.IsBusinessDay);

        propositions.Select(p => p.LieuId).ShouldBe([charleroi.LieuId, liege.LieuId]);
        propositions[0].DistanceDepuisPrecedentKm.ShouldNotBeNull().ShouldBeLessThan(propositions[1].DistanceDepuisPrecedentKm!.Value);
        PlanificateurSessions.DistanceTotaleKm(propositions).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Une_visite_circulaire_ne_revient_pas_sur_ses_pas()
    {
        // Charleroi (ouest), Liège (est) : depuis Liège, Namur puis Charleroi plutôt que l'inverse.
        var namur = Site(Namur, Personne());
        var charleroi = Site(Charleroi, Personne());
        var liege = Site(Liege, Personne());

        var propositions = PlanificateurSessions.Proposer([charleroi, namur, liege], Liege, Fabrique.Lundi, 10, Calendrier.IsBusinessDay);

        propositions.Select(p => p.LieuId).ShouldBe([liege.LieuId, namur.LieuId, charleroi.LieuId]);
    }

    [Fact]
    public void Un_site_est_decoupe_en_sessions_de_la_capacite_donnee()
    {
        var site = Site(Namur, Personne(), Personne(), Personne(), Personne(), Personne());

        var propositions = PlanificateurSessions.Proposer([site], null, Fabrique.Lundi, 2, Calendrier.IsBusinessDay);

        propositions.Select(p => p.Personnes.Count).ShouldBe([2, 2, 1]);
        propositions.Select(p => p.Date).ShouldBe([Fabrique.Lundi, Fabrique.Lundi.AddDays(1), Fabrique.Lundi.AddDays(2)]);
    }

    [Fact]
    public void Les_sessions_tombent_les_jours_ouvrables()
    {
        // Vendredi 26 mars 2027 puis lundi de Pâques (29 mars) : la session suivante est le mardi 30.
        var site = Site(Namur, Personne(), Personne(), Personne());

        var propositions = PlanificateurSessions.Proposer([site], null, new DateOnly(2027, 3, 26), 1, Calendrier.IsBusinessDay);

        propositions.Select(p => p.Date).ShouldBe([new DateOnly(2027, 3, 26), new DateOnly(2027, 3, 30), new DateOnly(2027, 3, 31)]);
    }

    [Fact]
    public void Les_echeances_les_plus_proches_passent_en_premier_et_un_depassement_est_signale()
    {
        var urgent = Personne(Fabrique.Lundi.AddDays(1));
        var tranquille = Personne(Fabrique.Lundi.AddDays(60));
        var site = Site(Namur, tranquille, urgent);

        var propositions = PlanificateurSessions.Proposer([site], null, Fabrique.Lundi, 1, Calendrier.IsBusinessDay);

        propositions[0].Personnes[0].ShouldBe(urgent);
        propositions[0].HorsDelai.ShouldBeFalse();

        var enRetard = PlanificateurSessions.Proposer([Site(Namur, Personne(Fabrique.Lundi.AddDays(-1)))], null, Fabrique.Lundi, 1, Calendrier.IsBusinessDay);
        enRetard[0].HorsDelai.ShouldBeTrue();
    }

    [Fact]
    public void Les_sites_sans_position_sont_places_en_dernier()
    {
        var inconnu = Site(null, Personne());
        var charleroi = Site(Charleroi, Personne());

        var propositions = PlanificateurSessions.Proposer([inconnu, charleroi], Namur, Fabrique.Lundi, 10, Calendrier.IsBusinessDay);

        propositions.Select(p => p.LieuId).ShouldBe([charleroi.LieuId, inconnu.LieuId]);
        propositions[1].DistanceDepuisPrecedentKm.ShouldBeNull();
    }

    [Fact]
    public void Le_resultat_est_deterministe()
    {
        BesoinSite[] sites = [Site(Charleroi, Personne(), Personne()), Site(Liege, Personne()), Site(null, Personne())];

        var a = PlanificateurSessions.Proposer(sites, Namur, Fabrique.Lundi, 1, Calendrier.IsBusinessDay);
        var b = PlanificateurSessions.Proposer(sites.Reverse(), Namur, Fabrique.Lundi, 1, Calendrier.IsBusinessDay);

        a.Select(p => (p.Ordre, p.Date, p.LieuId)).ShouldBe(b.Select(p => (p.Ordre, p.Date, p.LieuId)));
    }

    [Fact]
    public void Une_session_exige_unite_et_chauffeur_ensemble_et_une_journee_realiste()
    {
        var conseiller = Guid.CreateVersion7();

        Should.Throw<DomainException>(() => Session.Creer(Fabrique.Lieu, Fabrique.Affilie, Fabrique.Lundi, conseiller, Guid.CreateVersion7(), null, 10, "EVALUATION_PERIODIQUE", new TimeOnly(8, 0), 30));
        Should.Throw<DomainException>(() => Session.Creer(Fabrique.Lieu, Fabrique.Affilie, Fabrique.Lundi, conseiller, null, null, 0, "EVALUATION_PERIODIQUE", new TimeOnly(8, 0), 30));
        Should.Throw<DomainException>(() => Session.Creer(Fabrique.Lieu, Fabrique.Affilie, Fabrique.Lundi, conseiller, null, null, 40, "EVALUATION_PERIODIQUE", new TimeOnly(8, 0), 30));
    }

    [Fact]
    public void Les_creneaux_d_une_session_se_suivent_en_heure_belge()
    {
        var session = Session.Creer(Fabrique.Lieu, Fabrique.Affilie, Fabrique.Lundi, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 3, "evaluation-periodique",
            new TimeOnly(8, 0), 30);
        session.DefinirItineraire([("Parking visiteurs, rue de l'Usine 1", new TimeOnly(7, 30), true, false, true)]);

        var horaires = session.Horaires().ToList();

        horaires.Count.ShouldBe(3);
        horaires[0].Debut.ShouldBe(Fabrique.Instant(Fabrique.Lundi, 8));
        horaires[2].Fin.ShouldBe(Fabrique.Instant(Fabrique.Lundi, 9, 30));
        session.EstTournee.ShouldBeTrue();
        session.Itineraire.ShouldHaveSingleItem().RaccordementElectrique.ShouldBeTrue();
    }

    [Fact]
    public void La_projection_d_obligation_ignore_un_evenement_plus_ancien()
    {
        var obligation = new ObligationAPlanifier(Guid.CreateVersion7(), Fabrique.Personne, Fabrique.Affilie, "EVALUATION_PERIODIQUE", Fabrique.Lundi, null,
            Fabrique.Maintenant);

        obligation.Appliquer(Fabrique.Personne, Fabrique.Affilie, "EVALUATION_PERIODIQUE", Fabrique.Lundi.AddDays(10), null, Fabrique.Maintenant.AddMinutes(-1)).ShouldBeFalse();
        obligation.DateDue.ShouldBe(Fabrique.Lundi);
        obligation.Appliquer(Fabrique.Personne, Fabrique.Affilie, "EVALUATION_PERIODIQUE", Fabrique.Lundi.AddDays(10), null, Fabrique.Maintenant.AddMinutes(1)).ShouldBeTrue();
        obligation.DateDue.ShouldBe(Fabrique.Lundi.AddDays(10));
    }

    [Fact]
    public void Une_obligation_redevient_a_planifier_quand_son_rendez_vous_tombe()
    {
        var obligation = new ObligationAPlanifier(Guid.CreateVersion7(), Fabrique.Personne, Fabrique.Affilie, "EVALUATION_PERIODIQUE", Fabrique.Lundi, null,
            Fabrique.Maintenant);
        var rdv = Guid.CreateVersion7();

        obligation.Couvrir(rdv);
        obligation.EstAPlanifier.ShouldBeFalse();
        obligation.Decouvrir(Guid.CreateVersion7());
        obligation.EstAPlanifier.ShouldBeFalse();
        obligation.Decouvrir(rdv);
        obligation.EstAPlanifier.ShouldBeTrue();
    }
}
