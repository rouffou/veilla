using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.Replanification;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>PLA-07 : replanification en masse après l'absence d'un conseiller, avec notification des personnes.</summary>
public class ReplanificationTests
{
    private static ReplanifierAbsenceHandler Handler(Harness h, Sepp.BuildingBlocks.Application.Security.ICurrentUser? user = null) =>
        new(h.Store, h.Store, h.Store, h.Store, h.Store, h.Prise, h.Annulation, h.Indisponibilites, h.Parametres, h.Store, h.Store, h.Horloge, user ?? Harness.Planificateur);

    private static ReplanifierAbsence Absence(Harness h, DateOnly jour) =>
        new(h.Conseiller.Id, Harness.Instant(jour, 0), Harness.Instant(jour.AddDays(1), 0));

    [Fact]
    public async Task Les_rendez_vous_sont_deplaces_chez_un_collegue_et_les_personnes_notifiees()
    {
        var h = new Harness();
        var collegue = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b", "EVALUATION_PERIODIQUE");
        var obligation = h.AjouterObligation();
        var ancien = h.AjouterCreneau(Harness.Lundi, 9);
        var remplacement = h.AjouterCreneau(Harness.Lundi.AddDays(1), 10, ressource: collegue);
        var rdv = await h.PlanifierRendezVous(ancien, obligation);
        var ancienDebut = rdv.Debut;
        h.Store.Published.Clear();

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.RendezVousDeplaces.ShouldBe(1);
        resultat.Value.RendezVousAnnules.ShouldBe(0);
        rdv.CreneauId.ShouldBe(remplacement.Id);
        rdv.RessourceId.ShouldBe(collegue.Id);
        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);
        remplacement.Statut.ShouldBe(StatutCreneau.Reserve);
        ancien.Statut.ShouldBe(StatutCreneau.Bloque);
        var evenement = h.Store.Evenements<RendezVousReplanifie>().ShouldHaveSingleItem();
        evenement.RendezVousId.ShouldBe(rdv.Id);
        evenement.AncienDebut.ShouldBe(ancienDebut);
        evenement.NouveauDebut.ShouldBe(remplacement.Debut);
        evenement.Motif.ShouldBe("AbsenceRessource");
        h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem().TypeConvocation.ShouldBe("Replanification");
        h.Store.Absences.ShouldHaveSingleItem().Source.ShouldBe(SourceAbsence.Saisie);
        obligation.RendezVousId.ShouldBe(rdv.Id);
    }

    [Fact]
    public async Task Sans_creneau_de_remplacement_le_rendez_vous_est_annule_et_l_obligation_redevient_a_planifier()
    {
        var h = new Harness();
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.RendezVousAnnules.ShouldBe(1);
        resultat.Value.RendezVousDeplaces.ShouldBe(0);
        rdv.Statut.ShouldBe(StatutRendezVous.Annule);
        rdv.MotifAnnulation.ShouldBe(MotifAnnulation.AbsenceRessource);
        obligation.EstAPlanifier.ShouldBeTrue();
        h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem().Motif.ShouldBe("AbsenceRessource");
    }

    [Fact]
    public async Task Un_remplacement_apres_la_date_limite_est_signale_hors_delai()
    {
        var h = new Harness();
        var collegue = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b");
        var obligation = h.AjouterObligation(dateLimite: Harness.Lundi.AddDays(3));
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);
        h.AjouterCreneau(Harness.Lundi.AddDays(14), 9, ressource: collegue);

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.RendezVousDeplaces.ShouldBe(1);
        resultat.Value.RendezVousHorsDelai.ShouldBe(1);
        rdv.Debut.ShouldBe(Harness.Instant(Harness.Lundi.AddDays(14), 9));
    }

    [Fact]
    public async Task Un_remplacement_avant_la_date_limite_est_prefere_a_un_creneau_plus_proche_apres_elle()
    {
        var h = new Harness();
        var collegue = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b");
        var obligation = h.AjouterObligation(dateLimite: Harness.Lundi.AddDays(10));
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);
        var dansLeDelai = h.AjouterCreneau(Harness.Lundi.AddDays(7), 9, ressource: collegue);

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.RendezVousHorsDelai.ShouldBe(0);
        rdv.CreneauId.ShouldBe(dansLeDelai.Id);
    }

    [Fact]
    public async Task Le_remplacement_reste_dans_le_meme_lieu()
    {
        var h = new Harness();
        var collegue = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b");
        var autreLieu = h.AjouterLieu("Centre de Liège");
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), obligation);
        h.AjouterCreneau(Harness.Lundi.AddDays(1), 9, ressource: collegue, lieu: autreLieu);
        var apres = h.AjouterCreneau(Harness.Lundi.AddDays(2), 9, ressource: collegue);

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.RendezVousDeplaces.ShouldBe(1);
        rdv.CreneauId.ShouldBe(apres.Id);
        rdv.LieuId.ShouldBe(h.Lieu.Id);
    }

    [Fact]
    public async Task Seuls_les_rendez_vous_de_la_ressource_absente_sur_la_periode_sont_touches()
    {
        var h = new Harness();
        var collegue = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b");
        var o1 = h.AjouterObligation();
        var o2 = h.AjouterObligation(personne: Guid.CreateVersion7());
        var o3 = h.AjouterObligation(personne: Guid.CreateVersion7());
        var touche = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), o1);
        var autreJour = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi.AddDays(1), 9), o2);
        var autreRessource = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9, ressource: collegue), o3);
        h.AjouterCreneau(Harness.Lundi.AddDays(3), 9, ressource: collegue);

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.RendezVousDeplaces.ShouldBe(1);
        touche.Debut.ShouldBe(Harness.Instant(Harness.Lundi.AddDays(3), 9));
        autreJour.Debut.ShouldBe(Harness.Instant(Harness.Lundi.AddDays(1), 9));
        autreRessource.Debut.ShouldBe(Harness.Instant(Harness.Lundi, 9));
    }

    [Fact]
    public async Task L_absence_bloque_les_creneaux_libres_de_la_ressource()
    {
        var h = new Harness();
        var libre = h.AjouterCreneau(Harness.Lundi, 14);
        var hors = h.AjouterCreneau(Harness.Lundi.AddDays(1), 14);

        var resultat = await Handler(h).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None);

        resultat.Value.CreneauxBloques.ShouldBe(1);
        libre.Statut.ShouldBe(StatutCreneau.Bloque);
        hors.Statut.ShouldBe(StatutCreneau.Libre);
    }

    [Fact]
    public async Task L_absence_d_une_salle_replanifie_aussi_les_rendez_vous_qui_l_utilisent()
    {
        var h = new Harness();
        var salle = h.AjouterRessource(TypeRessource.Salle, "Salle 1");
        var autreSalle = h.AjouterRessource(TypeRessource.Salle, "Salle 2");
        var obligation = h.AjouterObligation();
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9, associees: [salle.Id]), obligation);
        var remplacement = h.AjouterCreneau(Harness.Lundi.AddDays(1), 9, associees: [autreSalle.Id]);

        var resultat = await Handler(h).HandleAsync(new ReplanifierAbsence(salle.Id, Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0)),
            CancellationToken.None);

        resultat.Value.RendezVousDeplaces.ShouldBe(1);
        rdv.CreneauId.ShouldBe(remplacement.Id);
    }

    [Fact]
    public async Task Seul_le_planificateur_replanifie_et_la_periode_doit_etre_valide()
    {
        var h = new Harness();

        (await Handler(h, Harness.AssistantMedical).HandleAsync(Absence(h, Harness.Lundi), CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Handler(h).HandleAsync(new ReplanifierAbsence(h.Conseiller.Id, Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi, 0)), CancellationToken.None))
            .Error!.Code.ShouldBe("absence.periode-invalide");
        (await Handler(h).HandleAsync(new ReplanifierAbsence(Guid.CreateVersion7(), Harness.Instant(Harness.Lundi, 0), Harness.Instant(Harness.Lundi.AddDays(1), 0)), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
