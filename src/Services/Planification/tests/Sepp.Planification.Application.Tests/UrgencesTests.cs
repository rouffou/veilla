using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Referentiels;
using Sepp.Planification.Application.Projections;
using Sepp.Planification.Application.Urgences;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>PLA-06, §14.6 : créneaux d'urgence dans le délai légal (10 jours ouvrables, jours fériés exclus).</summary>
public class UrgencesTests
{
    // Reprise annoncée le vendredi 18 décembre 2026 : Noël et le nouvel an repoussent l'échéance légale au 5 janvier 2027.
    private static readonly DateOnly Depart = new(2026, 12, 18);
    private static readonly DateOnly Echeance = new(2027, 1, 5);

    [Fact]
    public async Task Un_creneau_le_jour_de_l_echeance_legale_est_retenu_grace_aux_jours_feries()
    {
        var h = new Harness(Depart);
        var creneau = h.AjouterCreneau(Echeance, 9, typeActe: "EXAMEN_REPRISE");

        await h.ObligationCreee("EXAMEN_REPRISE", Depart);

        var rdv = h.Store.RendezVous.ShouldHaveSingleItem();
        rdv.CreneauId.ShouldBe(creneau.Id);
        rdv.Origine.ShouldBe(OrigineRendezVous.Urgence);
        rdv.Urgent.ShouldBeTrue();
        h.Store.Evenements<RendezVousPlanifie>().ShouldHaveSingleItem().RendezVousId.ShouldBe(rdv.Id);
        h.Store.Evenements<ConvocationEmise>().ShouldHaveSingleItem().TypeConvocation.ShouldBe("Convocation");
        h.Store.Evenements<UrgenceNonCouverte>().ShouldBeEmpty();
        h.Store.Obligations.Single().EstAPlanifier.ShouldBeFalse();
    }

    [Fact]
    public async Task Sans_creneau_avant_l_echeance_l_urgence_est_signalee_non_couverte()
    {
        var h = new Harness(Depart);
        h.AjouterCreneau(Echeance.AddDays(1), 9, typeActe: "EXAMEN_REPRISE");

        await h.ObligationCreee("EXAMEN_REPRISE", Depart);

        h.Store.RendezVous.ShouldBeEmpty();
        var alerte = h.Store.Evenements<UrgenceNonCouverte>().ShouldHaveSingleItem();
        alerte.DateLimite.ShouldBe(Echeance);
        alerte.TypeExamen.ShouldBe("EXAMEN_REPRISE");
        h.Store.Obligations.Single().UrgenceNonCouverte.ShouldBeTrue();
        h.Store.Obligations.Single().EstAPlanifier.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_creneau_reserve_aux_urgences_convient_si_la_ressource_a_la_competence()
    {
        var h = new Harness(Depart);
        var incompetent = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b", "VISITE_PERIODIQUE");
        h.AjouterCreneau(new DateOnly(2026, 12, 21), 9, typeActe: "VISITE_PERIODIQUE", urgence: true, ressource: incompetent);
        var competent = h.AjouterCreneau(new DateOnly(2026, 12, 22), 9, typeActe: "VISITE_PERIODIQUE", urgence: true);

        await h.ObligationCreee("EXAMEN_REPRISE", Depart);

        h.Store.RendezVous.ShouldHaveSingleItem().CreneauId.ShouldBe(competent.Id);
    }

    [Fact]
    public async Task Le_premier_creneau_disponible_est_retenu_et_l_urgence_prime_a_horaire_egal()
    {
        var h = new Harness(Depart);
        var autre = h.AjouterRessource(TypeRessource.Conseiller, "Dr B.", "kc-b", "EXAMEN_REPRISE");
        h.AjouterCreneau(new DateOnly(2026, 12, 23), 9, typeActe: "EXAMEN_REPRISE");
        h.AjouterCreneau(new DateOnly(2026, 12, 22), 9, typeActe: "EXAMEN_REPRISE");
        var urgence = h.AjouterCreneau(new DateOnly(2026, 12, 22), 9, typeActe: "EXAMEN_REPRISE", urgence: true, ressource: autre);

        await h.ObligationCreee("EXAMEN_REPRISE", Depart);

        h.Store.RendezVous.ShouldHaveSingleItem().CreneauId.ShouldBe(urgence.Id);
    }

    [Fact]
    public async Task La_date_limite_communiquee_par_les_obligations_prime_sur_le_delai_par_defaut()
    {
        var h = new Harness(Depart);
        h.AjouterCreneau(new DateOnly(2026, 12, 28), 9, typeActe: "EXAMEN_REPRISE");

        await h.ObligationCreee("EXAMEN_REPRISE", Depart, dateLimite: new DateOnly(2026, 12, 23));

        h.Store.RendezVous.ShouldBeEmpty();
        h.Store.Evenements<UrgenceNonCouverte>().ShouldHaveSingleItem().DateLimite.ShouldBe(new DateOnly(2026, 12, 23));
    }

    [Fact]
    public async Task Le_parametre_legal_recu_de_referentiels_remplace_le_delai_par_defaut()
    {
        var h = new Harness(Depart);
        var parametres = new ParametreLegalModifieHandler(h.Store, h.Options, h.Store);
        await parametres.HandleAsync(new ParametreLegalModifie("SANTE.REPRISE.DELAI", 5, "JoursOuvrables", new DateOnly(2026, 1, 1), null), CancellationToken.None);
        await parametres.HandleAsync(new ParametreLegalModifie("AUTRE.PARAMETRE", 1, "Jours", new DateOnly(2026, 1, 1), null), CancellationToken.None);
        h.Store.Parametres.ShouldHaveSingleItem();
        h.AjouterCreneau(Echeance, 9, typeActe: "EXAMEN_REPRISE");
        var cinquiemeJour = h.AjouterCreneau(new DateOnly(2026, 12, 28), 9, typeActe: "EXAMEN_REPRISE");

        await h.ObligationCreee("EXAMEN_REPRISE", Depart);

        h.Store.RendezVous.ShouldHaveSingleItem().CreneauId.ShouldBe(cinquiemeJour.Id);
    }

    [Fact]
    public async Task Une_obligation_ordinaire_est_memorisee_sans_rendez_vous()
    {
        var h = new Harness(Depart);
        h.AjouterCreneau(Echeance, 9);

        await h.ObligationCreee("VISITE_PERIODIQUE", Depart.AddMonths(2));

        h.Store.Obligations.ShouldHaveSingleItem().EstAPlanifier.ShouldBeTrue();
        h.Store.RendezVous.ShouldBeEmpty();
        h.Store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejouer_l_evenement_ne_cree_pas_un_second_rendez_vous()
    {
        var h = new Harness(Depart);
        h.AjouterCreneau(Echeance, 9, typeActe: "EXAMEN_REPRISE");
        h.AjouterCreneau(Echeance, 10, typeActe: "EXAMEN_REPRISE");
        var evenement = new ObligationCreee(Guid.CreateVersion7(), h.Personne, h.Affilie, "EXAMEN_REPRISE", Depart, null);

        await h.HandlerObligationCreee().HandleAsync(evenement, CancellationToken.None);
        await h.HandlerObligationCreee().HandleAsync(evenement, CancellationToken.None);

        h.Store.RendezVous.ShouldHaveSingleItem();
        h.Store.Obligations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Une_echeance_recue_avant_la_creation_est_conservee()
    {
        var h = new Harness(Depart);
        var obligationId = Guid.CreateVersion7();
        var echue = new ObligationEchueHandler(h.Store, h.Store);

        await echue.HandleAsync(new ObligationEchue(obligationId, h.Personne, h.Affilie, "VISITE_PERIODIQUE", Depart), CancellationToken.None);
        await h.ObligationCreee("VISITE_PERIODIQUE", Depart.AddDays(-30), null, obligationId);

        var obligation = h.Store.Obligations.ShouldHaveSingleItem();
        obligation.Echue.ShouldBeTrue();
        obligation.DateDue.ShouldBe(Depart.AddDays(-30));
    }

    [Fact]
    public async Task Le_planificateur_reserve_une_urgence_et_une_urgence_sans_creneau_est_un_conflit()
    {
        var h = new Harness(Depart);
        var obligation = h.AjouterObligation("EXAMEN_REPRISE", dateDue: Depart);
        var handler = new ReserverUrgenceHandler(h.Urgence, h.Store, h.Store, Harness.Planificateur);

        var sansCreneau = await handler.HandleAsync(new ReserverUrgence(h.Personne, h.Affilie, "EXAMEN_REPRISE", [obligation.ObligationId], Depart, null), CancellationToken.None);

        sansCreneau.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        sansCreneau.Error.Code.ShouldBe("urgence.non-couverte");
        h.Store.Evenements<UrgenceNonCouverte>().ShouldHaveSingleItem();

        var creneau = h.AjouterCreneau(Echeance, 9, typeActe: "EXAMEN_REPRISE");
        var reserve = await handler.HandleAsync(new ReserverUrgence(h.Personne, h.Affilie, "EXAMEN_REPRISE", [obligation.ObligationId], Depart, null), CancellationToken.None);

        reserve.IsSuccess.ShouldBeTrue();
        reserve.Value.Couverte.ShouldBeTrue();
        reserve.Value.Echeance.ShouldBe(Echeance);
        h.Store.RendezVous.ShouldHaveSingleItem().CreneauId.ShouldBe(creneau.Id);
    }

    [Fact]
    public async Task Un_type_d_acte_ordinaire_sans_date_limite_n_est_pas_une_urgence()
    {
        var h = new Harness(Depart);
        var handler = new ReserverUrgenceHandler(h.Urgence, h.Store, h.Store, Harness.Planificateur);

        var resultat = await handler.HandleAsync(new ReserverUrgence(h.Personne, h.Affilie, "VISITE_PERIODIQUE", null, Depart, null), CancellationToken.None);

        resultat.Error!.Code.ShouldBe("urgence.type-non-urgent");
    }

    [Fact]
    public async Task Seul_le_planificateur_reserve_une_urgence()
    {
        var h = new Harness(Depart);
        var handler = new ReserverUrgenceHandler(h.Urgence, h.Store, h.Store, Harness.AssistantMedical);

        var resultat = await handler.HandleAsync(new ReserverUrgence(h.Personne, h.Affilie, "EXAMEN_REPRISE", null, Depart, null), CancellationToken.None);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
}
