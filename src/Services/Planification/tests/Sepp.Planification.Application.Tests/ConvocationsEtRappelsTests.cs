using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>SAN-10, SAN-11, SAN-13 : convocations, recommandé, rappels J-7 / J-1, absences et reconvocation.</summary>
public class ConvocationsEtRappelsTests
{
    private static PlanifierRendezVousHandler Planifier(Harness h, ICurrentUser? user = null) =>
        new(h.Store, h.Store, h.Prise, h.Store, user ?? Harness.Planificateur);

    private static async Task<RendezVous> Rdv(Harness h, DateOnly jour, int heure = 9, Guid? personne = null)
    {
        var obligation = h.AjouterObligation(personne: personne);
        return await h.PlanifierRendezVous(h.AjouterCreneau(jour, heure), obligation);
    }

    [Fact]
    public async Task Le_rendez_vous_publie_son_evenement_et_une_convocation_au_canal_par_defaut()
    {
        var h = new Harness();

        var rdv = await Rdv(h, Harness.Lundi);

        var planifie = h.Store.Evenements<RendezVousPlanifie>().ShouldHaveSingleItem();
        planifie.RendezVousId.ShouldBe(rdv.Id);
        planifie.ObligationIds.ShouldBe(rdv.ObligationIds);
        var convocation = h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem();
        convocation.Canal.ShouldBe("Courrier");
        convocation.Recommande.ShouldBeFalse();
        convocation.TypeActe.ShouldBe("VISITE_PERIODIQUE");
        convocation.Debut.ShouldBe(rdv.Debut);
    }

    [Fact]
    public async Task Le_canal_prefere_de_l_affilie_est_utilise()
    {
        var h = new Harness();
        h.Store.Preferences.Add(PreferenceConvocation.Creer(h.Affilie, CanalConvocation.Email));

        await Rdv(h, Harness.Lundi);

        h.Store.Evenements<ConvocationEmise>().Single().Canal.ShouldBe("Email");
    }

    [Fact]
    public async Task Un_recommande_est_envoye_par_courrier_quand_le_canal_prefere_ne_le_permet_pas()
    {
        var h = new Harness();
        h.Store.Preferences.Add(PreferenceConvocation.Creer(h.Affilie, CanalConvocation.Sms));
        var obligation = h.AjouterObligation();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9);

        var resultat = await Planifier(h).HandleAsync(
            new PlanifierRendezVous(creneau.Id, h.Personne, h.Affilie, [obligation.ObligationId], null, true), CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        var convocation = h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem();
        convocation.Recommande.ShouldBeTrue();
        convocation.Canal.ShouldBe("Courrier");
    }

    [Fact]
    public async Task Un_type_d_acte_configure_comme_recommande_part_en_recommande()
    {
        var h = new Harness();
        h.Options.TypesRecommandes.Add("EXAMEN_REPRISE");
        var creneau = h.AjouterCreneau(Harness.Lundi, 9, typeActe: "EXAMEN_REPRISE");
        var obligation = h.AjouterObligation("EXAMEN_REPRISE");

        await Planifier(h).HandleAsync(new PlanifierRendezVous(creneau.Id, h.Personne, h.Affilie, [obligation.ObligationId], "Email", null), CancellationToken.None);

        var convocation = h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem();
        convocation.Recommande.ShouldBeTrue();
        convocation.Canal.ShouldBe("Email");
    }

    [Fact]
    public async Task Une_planification_refuse_les_obligations_d_une_autre_personne_ou_deja_couvertes()
    {
        var h = new Harness();
        var autre = h.AjouterObligation(personne: Guid.CreateVersion7());
        var couverte = h.AjouterObligation();
        await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), couverte);
        var creneau = h.AjouterCreneau(Harness.Lundi, 11);

        var etrangere = await Planifier(h).HandleAsync(new PlanifierRendezVous(creneau.Id, h.Personne, h.Affilie, [autre.ObligationId], null, null), CancellationToken.None);
        var dejaCouverte = await Planifier(h).HandleAsync(new PlanifierRendezVous(creneau.Id, h.Personne, h.Affilie, [couverte.ObligationId], null, null), CancellationToken.None);

        etrangere.Error!.Code.ShouldBe("obligation.inconnue");
        dejaCouverte.Error!.Code.ShouldBe("obligation.deja-couverte");
        creneau.Statut.ShouldBe(StatutCreneau.Libre);
    }

    [Fact]
    public async Task Un_rendez_vous_peut_couvrir_plusieurs_obligations_d_une_personne()
    {
        var h = new Harness();
        var o1 = h.AjouterObligation();
        var o2 = h.AjouterObligation("VISITE_PERIODIQUE");

        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), o1, o2);

        rdv.ObligationIds.ShouldBe([o1.ObligationId, o2.ObligationId]);
        h.Store.Obligations.ShouldAllBe(o => o.RendezVousId == rdv.Id);
    }

    [Fact]
    public async Task Seul_le_planificateur_planifie_un_rendez_vous()
    {
        var h = new Harness();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9);

        foreach (var user in new[] { Harness.TravailleurUser, Harness.EmployeurUser, Harness.AssistantMedical })
        {
            var resultat = await Planifier(h, user).HandleAsync(new PlanifierRendezVous(creneau.Id, h.Personne, h.Affilie, [], null, null), CancellationToken.None);
            resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        }
    }

    [Fact]
    public async Task L_annulation_par_le_planificateur_libere_le_creneau_et_publie_le_motif()
    {
        var h = new Harness();
        var obligation = h.AjouterObligation();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9);
        var rdv = await h.PlanifierRendezVous(creneau, obligation);
        var handler = new AnnulerRendezVousHandler(h.Annulation, h.Store, h.Store, Harness.Planificateur);

        (await handler.HandleAsync(new AnnulerRendezVous(rdv.Id, "motif-inconnu"), CancellationToken.None)).Error!.Code.ShouldBe("rendez-vous.motif-inconnu");
        (await handler.HandleAsync(new AnnulerRendezVous(rdv.Id, "demande_employeur"), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        creneau.Statut.ShouldBe(StatutCreneau.Libre);
        obligation.EstAPlanifier.ShouldBeTrue();
        h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem().Motif.ShouldBe("DemandeEmployeur");
        (await handler.HandleAsync(new AnnulerRendezVous(rdv.Id, "Autre"), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    // SAN-10 : convocation par lot.
    [Fact]
    public async Task La_convocation_par_lot_partage_un_identifiant_et_ignore_les_rendez_vous_deja_convoques()
    {
        var h = new Harness();
        var sansConvocation = new List<RendezVous>();
        for (var i = 0; i < 2; i++)
        {
            var obligation = h.AjouterObligation(personne: Guid.CreateVersion7());
            var creneau = h.AjouterCreneau(Harness.Lundi, 9 + i);
            var handler = Planifier(h);
            var id = (await handler.HandleAsync(new PlanifierRendezVous(creneau.Id, obligation.PersonneId, h.Affilie, [obligation.ObligationId], null, null, Convoquer: false),
                CancellationToken.None)).Value;
            sansConvocation.Add(h.Store.RendezVous.Single(r => r.Id == id));
        }

        var dejaConvoque = await Rdv(h, Harness.Lundi, 12, Guid.CreateVersion7());
        h.Store.Published.Clear();
        var lot = new ConvoquerParLotHandler(h.Store, h.Store, h.Prise, h.Store, Harness.Planificateur);

        var resultat = await lot.HandleAsync(new ConvoquerParLot([.. sansConvocation.Select(r => r.Id), dejaConvoque.Id, Guid.CreateVersion7()], "Email"), CancellationToken.None);

        resultat.Value.Emises.ShouldBe(2);
        resultat.Value.Ignores.Count.ShouldBe(2);
        var evenements = h.Store.Evenements<ConvocationEmise>().ToList();
        evenements.Count.ShouldBe(2);
        evenements.ShouldAllBe(e => e.LotId == resultat.Value.LotId && e.Canal == "Email");
    }

    [Fact]
    public async Task Un_lot_est_limite_a_mille_rendez_vous()
    {
        var h = new Harness();
        var lot = new ConvoquerParLotHandler(h.Store, h.Store, h.Prise, h.Store, Harness.Planificateur);

        (await lot.HandleAsync(new ConvoquerParLot([], null), CancellationToken.None)).Error!.Code.ShouldBe("convocation.lot-invalide");
        (await lot.HandleAsync(new ConvoquerParLot(Enumerable.Range(0, 1001).Select(_ => Guid.CreateVersion7()).ToList(), null), CancellationToken.None))
            .Error!.Code.ShouldBe("convocation.lot-invalide");
    }

    [Fact]
    public async Task Le_canal_de_l_affilie_est_defini_par_le_planificateur_ou_son_employeur_seulement()
    {
        var h = new Harness();
        DefinirPreferenceConvocationHandler Handler(IPerimetreUtilisateur p, ICurrentUser u) => new(h.Store, p, h.Store, u);

        (await Handler(FakePerimetre.Employeur(h.Affilie), Harness.EmployeurUser).HandleAsync(new DefinirPreferenceConvocation(h.Affilie, "sms"), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        h.Store.Preferences.ShouldHaveSingleItem().Canal.ShouldBe(CanalConvocation.Sms);
        (await Handler(FakePerimetre.Interne, Harness.Planificateur).HandleAsync(new DefinirPreferenceConvocation(h.Affilie, "Email"), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        h.Store.Preferences.ShouldHaveSingleItem().Canal.ShouldBe(CanalConvocation.Email);

        (await Handler(FakePerimetre.Employeur(Guid.CreateVersion7()), Harness.EmployeurUser).HandleAsync(new DefinirPreferenceConvocation(h.Affilie, "Sms"), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Handler(FakePerimetre.Travailleur(h.Personne), Harness.TravailleurUser).HandleAsync(new DefinirPreferenceConvocation(h.Affilie, "Sms"), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Handler(FakePerimetre.Interne, Harness.Planificateur).HandleAsync(new DefinirPreferenceConvocation(h.Affilie, "pigeon"), CancellationToken.None))
            .Error!.Code.ShouldBe("convocation.canal-inconnu");
    }

    // SAN-13 : rappels.
    private static async Task<(int Premiers, int Seconds)> Rappels(Harness h, DateOnly jour)
    {
        var rapport = await h.Rappels().ExecuterAsync(jour, CancellationToken.None);
        return (rapport.PremiersRappels, rapport.SecondsRappels);
    }

    [Fact]
    public async Task Les_rappels_partent_sept_jours_puis_un_jour_avant_et_une_seule_fois()
    {
        var h = new Harness();
        var rdv = await Rdv(h, Harness.Lundi);
        h.Store.Published.Clear();

        (await Rappels(h, Harness.Lundi.AddDays(-8))).ShouldBe((0, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-7))).ShouldBe((1, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-7))).ShouldBe((0, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-2))).ShouldBe((0, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-1))).ShouldBe((0, 1));
        (await Rappels(h, Harness.Lundi.AddDays(-1))).ShouldBe((0, 0));
        (await Rappels(h, Harness.Lundi)).ShouldBe((0, 0));

        var rappels = h.Store.Evenements<RappelRendezVousDu>().ToList();
        rappels.Select(r => r.NumeroRappel).ShouldBe([1, 2]);
        rappels.ShouldAllBe(r => r.RendezVousId == rdv.Id && r.Debut == rdv.Debut && r.Canal == "Courrier");
    }

    [Fact]
    public async Task Les_delais_de_rappel_viennent_des_parametres_legaux_recus()
    {
        var h = new Harness();
        h.Store.Parametres.Add(new ParametreLegalLocal("CONVOCATION.RAPPEL_1", new DateOnly(2027, 1, 1), null, 10, "JoursCalendrier"));
        h.Store.Parametres.Add(new ParametreLegalLocal("CONVOCATION.RAPPEL_2", new DateOnly(2027, 1, 1), null, 3, "JoursCalendrier"));
        await Rdv(h, Harness.Lundi);

        (await Rappels(h, Harness.Lundi.AddDays(-11))).ShouldBe((0, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-10))).ShouldBe((1, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-3))).ShouldBe((0, 1));
    }

    [Fact]
    public async Task Un_rappel_manque_est_rattrape_et_un_rendez_vous_annule_n_en_recoit_pas()
    {
        var h = new Harness();
        var rdv = await Rdv(h, Harness.Lundi);
        var annule = await Rdv(h, Harness.Lundi, 10, Guid.CreateVersion7());
        await new AnnulerRendezVousHandler(h.Annulation, h.Store, h.Store, Harness.Planificateur).HandleAsync(new AnnulerRendezVous(annule.Id, "Autre"), CancellationToken.None);

        (await Rappels(h, Harness.Lundi.AddDays(-5))).ShouldBe((1, 0));
        h.Store.Evenements<RappelRendezVousDu>().ShouldHaveSingleItem().RendezVousId.ShouldBe(rdv.Id);
    }

    [Fact]
    public async Task Un_rendez_vous_pris_a_trois_jours_n_a_pas_de_premier_rappel()
    {
        var h = new Harness(Harness.Lundi.AddDays(-3));
        await Rdv(h, Harness.Lundi);

        (await Rappels(h, Harness.Lundi.AddDays(-3))).ShouldBe((0, 0));
        (await Rappels(h, Harness.Lundi.AddDays(-1))).ShouldBe((0, 1));
    }

    [Fact]
    public async Task Le_rappel_reprend_le_canal_de_la_derniere_convocation()
    {
        var h = new Harness();
        h.Store.Preferences.Add(PreferenceConvocation.Creer(h.Affilie, CanalConvocation.Sms));
        await Rdv(h, Harness.Lundi);
        h.Store.Published.Clear();

        await Rappels(h, Harness.Lundi.AddDays(-7));

        h.Store.Evenements<RappelRendezVousDu>().Single().Canal.ShouldBe("Sms");
    }

    [Fact]
    public async Task Seul_le_planificateur_declenche_les_rappels_a_la_demande()
    {
        var h = new Harness();

        (await new EmettreRappelsHandler(h.Rappels(), Harness.Planificateur).HandleAsync(new EmettreRappels(Harness.Lundi), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new EmettreRappelsHandler(h.Rappels(), Harness.EmployeurUser).HandleAsync(new EmettreRappels(null), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    // SAN-13 : absence et reconvocation.
    private static ConstaterAbsenceHandler Absence(Harness h, ICurrentUser? user = null) => new(h.Store, h.Store, h.Store, h.Store, h.Horloge, user ?? Harness.Planificateur);

    private static ReconvoquerHandler Reconvoquer(Harness h) => new(h.Store, h.Store, h.Prise, h.Parametres, h.Store, h.Horloge, Harness.Planificateur);

    [Fact]
    public async Task L_absence_remet_les_obligations_a_planifier_et_publie_l_evenement()
    {
        var h = new Harness();
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);

        (await Absence(h).HandleAsync(new ConstaterAbsence(rdv.Id), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Conflict);

        h.Horloge.Fixer(rdv.Debut.AddMinutes(30));
        (await Absence(h, Harness.AssistantMedical).HandleAsync(new ConstaterAbsence(rdv.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        rdv.Statut.ShouldBe(StatutRendezVous.Absent);
        obligation.EstAPlanifier.ShouldBeTrue();
        var evenement = h.Store.Evenements<AbsenceRendezVousConstatee>().ShouldHaveSingleItem();
        evenement.ObligationIds.ShouldBe([obligation.ObligationId]);
        evenement.Debut.ShouldBe(rdv.Debut);
    }

    [Fact]
    public async Task La_reconvocation_recouvre_les_memes_obligations_dans_le_premier_creneau_libre()
    {
        var h = new Harness();
        var o1 = h.AjouterObligation();
        var o2 = h.AjouterObligation();
        var manque = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), o1, o2);
        var prochain = h.AjouterCreneau(Harness.Lundi.AddDays(7), 9);
        h.AjouterCreneau(Harness.Lundi.AddDays(8), 9);
        h.Horloge.Fixer(manque.Debut.AddMinutes(30));
        await Absence(h).HandleAsync(new ConstaterAbsence(manque.Id), CancellationToken.None);
        h.Store.Published.Clear();

        var resultat = await Reconvoquer(h).HandleAsync(new Reconvoquer(manque.Id, null, null), CancellationToken.None);

        resultat.IsSuccess.ShouldBeTrue();
        var nouveau = h.Store.RendezVous.Single(r => r.Id == resultat.Value);
        nouveau.CreneauId.ShouldBe(prochain.Id);
        nouveau.ReconvocationDeId.ShouldBe(manque.Id);
        nouveau.Origine.ShouldBe(OrigineRendezVous.Reconvocation);
        nouveau.ObligationIds.ShouldBe([o1.ObligationId, o2.ObligationId]);
        h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem().TypeConvocation.ShouldBe("Reconvocation");
        h.Store.Evenements<RendezVousPlanifie>().ShouldHaveSingleItem();
        o1.RendezVousId.ShouldBe(nouveau.Id);
    }

    [Fact]
    public async Task La_reconvocation_n_a_lieu_qu_une_fois_et_seulement_apres_une_absence()
    {
        var h = new Harness();
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);
        h.AjouterCreneau(Harness.Lundi.AddDays(7), 9);
        h.AjouterCreneau(Harness.Lundi.AddDays(8), 9);

        (await Reconvoquer(h).HandleAsync(new Reconvoquer(rdv.Id, null, null), CancellationToken.None)).Error!.Code.ShouldBe("rendez-vous.non-manque");

        h.Horloge.Fixer(rdv.Debut.AddMinutes(30));
        await Absence(h).HandleAsync(new ConstaterAbsence(rdv.Id), CancellationToken.None);
        (await Reconvoquer(h).HandleAsync(new Reconvoquer(rdv.Id, null, null), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await Reconvoquer(h).HandleAsync(new Reconvoquer(rdv.Id, null, null), CancellationToken.None)).Error!.Code.ShouldBe("rendez-vous.deja-reconvoque");
    }

    [Fact]
    public async Task Sans_creneau_libre_la_reconvocation_est_un_conflit()
    {
        var h = new Harness();
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);
        h.Horloge.Fixer(rdv.Debut.AddMinutes(30));
        await Absence(h).HandleAsync(new ConstaterAbsence(rdv.Id), CancellationToken.None);

        (await Reconvoquer(h).HandleAsync(new Reconvoquer(rdv.Id, null, null), CancellationToken.None)).Error!.Code.ShouldBe("creneau.aucun");
    }
}
