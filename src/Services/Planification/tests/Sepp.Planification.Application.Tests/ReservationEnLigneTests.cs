using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Application.Reservations;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>SAN-12 : réservation en ligne par l'employeur (claim affilie_id) ou le travailleur (claim personne_id).</summary>
public class ReservationEnLigneTests
{
    private static ReserverEnLigneHandler Reserver(Harness h, IPerimetreUtilisateur perimetre, ICurrentUser user) =>
        new(h.Store, h.Store, h.Store, h.Prise, perimetre, h.Parametres, h.Store, h.Horloge, user);

    private static Task<Result<Guid>> Reserve(Harness h, IPerimetreUtilisateur perimetre, ICurrentUser user, Creneau creneau, Guid? personne = null, Guid? affilie = null,
        IReadOnlyList<Guid>? obligations = null) =>
        Reserver(h, perimetre, user).HandleAsync(new ReserverEnLigne(creneau.Id, personne ?? h.Personne, affilie ?? h.Affilie, obligations), CancellationToken.None);

    [Fact]
    public async Task L_employeur_reserve_pour_son_affilie_et_le_rendez_vous_couvre_toutes_les_obligations_dues()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        var o1 = h.AjouterObligation();
        var o2 = h.AjouterObligation();
        h.AjouterObligation("EXAMEN_REPRISE");

        var resultat = await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, creneau);

        resultat.IsSuccess.ShouldBeTrue();
        var rdv = h.Store.RendezVous.ShouldHaveSingleItem();
        rdv.Origine.ShouldBe(OrigineRendezVous.ReservationEnLigne);
        rdv.ObligationIds.ShouldBe([o1.ObligationId, o2.ObligationId], ignoreOrder: true);
        h.Store.Obligations.Count(o => !o.EstAPlanifier).ShouldBe(2);
        h.Store.Evenements<RendezVousPlanifie>().ShouldHaveSingleItem().ObligationIds.Count.ShouldBe(2);
        h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Le_travailleur_reserve_pour_lui_meme_mais_pas_pour_un_autre()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        h.AjouterObligation();

        var autre = await Reserve(h, FakePerimetre.Travailleur(Guid.CreateVersion7()), Harness.TravailleurUser, creneau);
        var lui = await Reserve(h, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser, creneau);

        autre.Error!.Code.ShouldBe("perimetre.interdit");
        lui.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_employeur_ne_reserve_pas_pour_un_affilie_hors_de_son_perimetre()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        h.AjouterObligation();

        var resultat = await Reserve(h, FakePerimetre.Employeur(Guid.CreateVersion7()), Harness.EmployeurUser, creneau);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        h.Store.RendezVous.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_utilisateur_interne_n_utilise_pas_la_reservation_en_ligne()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        h.AjouterObligation();

        var resultat = await Reserve(h, FakePerimetre.Interne, Harness.Planificateur, creneau);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Un_creneau_non_ouvert_en_ligne_ou_reserve_aux_urgences_reste_inconnu()
    {
        var h = new Harness();
        var interne = h.AjouterCreneau(Harness.Lundi, 9, enLigne: false);
        var urgence = h.AjouterCreneau(Harness.Lundi, 10, urgence: true, enLigne: true);
        h.AjouterObligation();

        (await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, interne)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, urgence)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Un_cabinet_installe_chez_un_autre_affilie_n_est_pas_reservable()
    {
        var h = new Harness();
        var cabinet = h.AjouterLieu("Cabinet Entreprise X", Guid.CreateVersion7());
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true, lieu: cabinet);
        h.AjouterObligation();

        var resultat = await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, creneau);

        resultat.Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Sans_obligation_a_planifier_pour_ce_type_d_acte_la_reservation_est_refusee()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true, typeActe: "VISITE_PERIODIQUE");
        h.AjouterObligation("EXAMEN_REPRISE");

        var resultat = await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, creneau);

        resultat.Error!.Code.ShouldBe("reservation.aucune-obligation");
    }

    [Fact]
    public async Task Le_second_a_reserver_le_meme_creneau_est_refuse()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        var autrePersonne = Guid.CreateVersion7();
        h.AjouterObligation();
        h.AjouterObligation(personne: autrePersonne);

        (await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, creneau)).IsSuccess.ShouldBeTrue();
        var second = await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, creneau, autrePersonne);

        second.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        h.Store.RendezVous.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Une_personne_ne_reserve_pas_deux_creneaux_qui_se_chevauchent()
    {
        var h = new Harness();
        var premier = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        var chevauchant = h.AjouterCreneau(Harness.Lundi, 9, 15, enLigne: true, ressource: h.AjouterRessource(Domain.Ressources.TypeRessource.Conseiller, "Dr B."));
        h.AjouterObligation();
        h.AjouterObligation("EXAMEN_REPRISE");
        h.Store.Creneaux.Last().TypeActe.ShouldBe("VISITE_PERIODIQUE");

        (await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, premier)).IsSuccess.ShouldBeTrue();
        h.AjouterObligation();
        var second = await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, chevauchant);

        second.Error!.Code.ShouldBe("rendez-vous.chevauchement");
    }

    [Fact]
    public async Task L_annulation_en_ligne_libere_le_creneau_jusqu_a_24_heures_avant()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        var obligation = h.AjouterObligation();
        var id = (await Reserve(h, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser, creneau)).Value;
        var handler = new AnnulerEnLigneHandler(h.Store, h.Annulation, FakePerimetre.Travailleur(h.Personne), h.Store, h.Horloge, Harness.TravailleurUser);

        var resultat = await handler.HandleAsync(new AnnulerEnLigne(id), CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        creneau.Statut.ShouldBe(StatutCreneau.Libre);
        obligation.EstAPlanifier.ShouldBeTrue();
        var evenement = h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem();
        evenement.Motif.ShouldBe("DemandeTravailleur");
    }

    [Fact]
    public async Task L_annulation_tardive_ou_par_un_tiers_est_refusee()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        h.AjouterObligation();
        var id = (await Reserve(h, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser, creneau)).Value;

        var tiers = new AnnulerEnLigneHandler(h.Store, h.Annulation, FakePerimetre.Employeur(Guid.CreateVersion7()), h.Store, h.Horloge, Harness.EmployeurUser);
        (await tiers.HandleAsync(new AnnulerEnLigne(id), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.NotFound);

        h.Horloge.Fixer(creneau.Debut.AddHours(-23));
        var tardif = new AnnulerEnLigneHandler(h.Store, h.Annulation, FakePerimetre.Employeur(h.Affilie), h.Store, h.Horloge, Harness.EmployeurUser);
        (await tardif.HandleAsync(new AnnulerEnLigne(id), CancellationToken.None)).Error!.Code.ShouldBe("rendez-vous.annulation-tardive");
        creneau.Statut.ShouldBe(StatutCreneau.Reserve);
    }

    [Fact]
    public async Task Les_creneaux_proposes_ne_montrent_que_les_creneaux_ouverts_du_perimetre()
    {
        var h = new Harness();
        var ouvert = h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        h.AjouterCreneau(Harness.Lundi, 10, enLigne: false);
        h.AjouterCreneau(Harness.Lundi, 11, urgence: true, enLigne: true);
        h.AjouterCreneau(Harness.Lundi, 12, enLigne: true, typeActe: "EXAMEN_REPRISE");
        h.AjouterCreneau(Harness.Lundi, 13, enLigne: true, lieu: h.AjouterLieu("Cabinet autre affilié", Guid.CreateVersion7()));
        var chezLui = h.AjouterCreneau(Harness.Lundi, 14, enLigne: true, lieu: h.AjouterLieu("Cabinet de l'affilié", h.Affilie));
        var handler = new ListerCreneauxOuvertsHandler(h.Store, h.Store, h.Store, FakePerimetre.Employeur(h.Affilie), h.Parametres, h.Horloge, Harness.EmployeurUser);

        var resultat = await handler.HandleAsync(
            new ListerCreneauxOuverts(h.Affilie, "visite-periodique", Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0), null), CancellationToken.None);

        resultat.Value.Select(c => c.Id).ShouldBe([ouvert.Id, chezLui.Id]);
    }

    [Fact]
    public async Task Un_travailleur_ne_consulte_les_creneaux_que_d_un_affilie_pour_lequel_il_a_une_obligation()
    {
        var h = new Harness();
        h.AjouterCreneau(Harness.Lundi, 9, enLigne: true);
        var periode = (Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0));
        var handler = new ListerCreneauxOuvertsHandler(h.Store, h.Store, h.Store, FakePerimetre.Travailleur(h.Personne), h.Parametres, h.Horloge, Harness.TravailleurUser);

        var sans = await handler.HandleAsync(new ListerCreneauxOuverts(h.Affilie, "VISITE_PERIODIQUE", periode.Item1, periode.Item2, null), CancellationToken.None);
        h.AjouterObligation();
        var avec = await handler.HandleAsync(new ListerCreneauxOuverts(h.Affilie, "VISITE_PERIODIQUE", periode.Item1, periode.Item2, null), CancellationToken.None);

        sans.Error!.Code.ShouldBe("perimetre.interdit");
        avec.Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Les_rendez_vous_visibles_sont_ceux_du_perimetre()
    {
        var h = new Harness();
        var autre = Guid.CreateVersion7();
        var o1 = h.AjouterObligation();
        var o2 = h.AjouterObligation(personne: autre);
        await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), o1);
        await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 10), o2);

        var travailleur = new ListerMesRendezVousHandler(h.Store, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser);
        var employeur = new ListerMesRendezVousHandler(h.Store, FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser);
        var intrus = new ListerMesRendezVousHandler(h.Store, FakePerimetre.Employeur(Guid.CreateVersion7()), Harness.EmployeurUser);

        (await travailleur.HandleAsync(new ListerMesRendezVous(null), CancellationToken.None)).Value.ShouldHaveSingleItem().PersonneId.ShouldBe(h.Personne);
        (await employeur.HandleAsync(new ListerMesRendezVous(h.Affilie), CancellationToken.None)).Value.Count.ShouldBe(2);
        (await employeur.HandleAsync(new ListerMesRendezVous(null), CancellationToken.None)).Error!.Code.ShouldBe("perimetre.affilie-obligatoire");
        (await intrus.HandleAsync(new ListerMesRendezVous(h.Affilie), CancellationToken.None)).Error!.Code.ShouldBe("perimetre.interdit");
    }

    [Fact]
    public async Task Un_rendez_vous_d_un_autre_perimetre_reste_introuvable()
    {
        var h = new Harness();
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);

        var autreTravailleur = new ObtenirRendezVousHandler(h.Store, FakePerimetre.Travailleur(Guid.CreateVersion7()), Harness.TravailleurUser);
        var lui = new ObtenirRendezVousHandler(h.Store, FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser);

        (await autreTravailleur.HandleAsync(new ObtenirRendezVous(rdv.Id), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await lui.HandleAsync(new ObtenirRendezVous(rdv.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }
}
