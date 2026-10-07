using Sepp.Planification.Application.Synchronisation;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>PLA-09 : synchronisation M365 / Google, intitulés sans donnée médicale.</summary>
public class SynchronisationAgendasTests
{
    private static SynchronisationAgendas Synchro(Harness h, FakeAgendaExterne agenda) => new(h.Store, h.Store, h.Store, h.Store, agenda, h.Indisponibilites, h.Store);

    private static (DateOnly Du, DateOnly Au) Periode => (Harness.Lundi, Harness.Lundi.AddDays(7));

    private static void Rattacher(Harness h, Ressource ressource, string compte = "dr.a@sepp.test") =>
        ressource.RattacherAgendaExterne(FournisseurAgenda.Microsoft365, compte);

    [Fact]
    public async Task Les_rendez_vous_sont_ecrits_sous_un_intitule_sans_donnee_medicale()
    {
        var h = new Harness();
        Rattacher(h, h.Conseiller);
        var agenda = new FakeAgendaExterne();
        var obligation = h.AjouterObligation("EXAMEN_REPRISE");
        var rdv = await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9, typeActe: "EXAMEN_REPRISE"), obligation);

        var rapport = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        rapport.EvenementsEcrits.ShouldBe(1);
        var evenement = agenda.Evenements.Values.ShouldHaveSingleItem();
        evenement.Titre.ShouldBe("Rendez-vous SEPP");
        var contenu = $"{evenement.Titre} {evenement.Description} {evenement.Lieu}";
        contenu.ShouldNotContain("REPRISE", Case.Insensitive);
        contenu.ShouldNotContain("EXAMEN", Case.Insensitive);
        contenu.ShouldNotContain(rdv.PersonneId.ToString());
        contenu.ShouldNotContain(rdv.AffilieId.ToString());
        contenu.ShouldNotContain(obligation.ObligationId.ToString());
        evenement.Lieu.ShouldBe(h.Lieu.Nom);
        rdv.ReferenceAgendaExterne.ShouldBe(agenda.Evenements.Keys.Single());
    }

    [Fact]
    public async Task Une_seconde_synchronisation_met_a_jour_sans_dupliquer()
    {
        var h = new Harness();
        Rattacher(h, h.Conseiller);
        var agenda = new FakeAgendaExterne();
        await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), h.AjouterObligation());
        var synchro = Synchro(h, agenda);

        await synchro.ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);
        var seconde = await synchro.ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        agenda.Evenements.Count.ShouldBe(1);
        seconde.OccupationsImportees.ShouldBe(0);
        h.Store.Absences.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_rendez_vous_annule_disparait_de_l_agenda_externe()
    {
        var h = new Harness();
        Rattacher(h, h.Conseiller);
        var agenda = new FakeAgendaExterne();
        var creneau = h.AjouterCreneau(Harness.Lundi, 9);
        var rdv = await h.PlanifierRendezVous(creneau, h.AjouterObligation());
        await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);
        agenda.Evenements.Count.ShouldBe(1);

        rdv.Annuler(MotifAnnulation.Autre, creneau, h.Horloge.GetUtcNow());
        var rapport = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        rapport.EvenementsSupprimes.ShouldBe(1);
        agenda.Evenements.ShouldBeEmpty();
        rdv.ReferenceAgendaExterne.ShouldBeNull();
    }

    [Fact]
    public async Task Les_occupations_de_l_agenda_externe_bloquent_les_creneaux_libres()
    {
        var h = new Harness();
        Rattacher(h, h.Conseiller);
        var agenda = new FakeAgendaExterne();
        agenda.Occupations.Add(new OccupationExterne("reunion-1", Harness.Instant(Harness.Lundi, 9), Harness.Instant(Harness.Lundi, 11)));
        var pris = h.AjouterCreneau(Harness.Lundi, 10);
        var libre = h.AjouterCreneau(Harness.Lundi, 14);

        var rapport = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);
        var seconde = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        rapport.OccupationsImportees.ShouldBe(1);
        rapport.CreneauxBloques.ShouldBe(1);
        pris.Statut.ShouldBe(StatutCreneau.Bloque);
        libre.Statut.ShouldBe(StatutCreneau.Libre);
        var absence = h.Store.Absences.ShouldHaveSingleItem();
        absence.Source.ShouldBe(SourceAbsence.AgendaExterne);
        seconde.OccupationsImportees.ShouldBe(0);
    }

    [Fact]
    public async Task Les_evenements_ecrits_par_le_sepp_ne_deviennent_pas_des_indisponibilites()
    {
        var h = new Harness();
        Rattacher(h, h.Conseiller);
        var agenda = new FakeAgendaExterne();
        await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9), h.AjouterObligation());

        var rapport = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        rapport.OccupationsImportees.ShouldBe(0);
        h.Store.Absences.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_agenda_injoignable_n_empeche_pas_la_synchronisation_des_autres()
    {
        var h = new Harness();
        var autre = h.AjouterRessource(TypeRessource.Infirmier, "Infirmière B.", "kc-b");
        Rattacher(h, h.Conseiller, "dr.a@sepp.test");
        Rattacher(h, autre, "inf.b@sepp.test");
        var agenda = new FakeAgendaExterne();
        agenda.ComptesInjoignables.Add("dr.a@sepp.test");
        await h.PlanifierRendezVous(h.AjouterCreneau(Harness.Lundi, 9, ressource: autre), h.AjouterObligation());

        var rapport = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        rapport.Ressources.ShouldBe(2);
        rapport.RessourcesEnErreur.ShouldBe(1);
        rapport.EvenementsEcrits.ShouldBe(1);
    }

    [Fact]
    public async Task Seules_les_ressources_humaines_actives_avec_un_agenda_sont_synchronisees()
    {
        var h = new Harness();
        var inactive = h.AjouterRessource(TypeRessource.Conseiller, "Dr C.", "kc-c");
        Rattacher(h, inactive, "dr.c@sepp.test");
        inactive.Desactiver();
        var agenda = new FakeAgendaExterne();

        var rapport = await Synchro(h, agenda).ExecuterAsync(Periode.Du, Periode.Au, CancellationToken.None);

        rapport.Ressources.ShouldBe(0);
    }
}
