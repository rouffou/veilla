using Sepp.Contracts.Communications;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Referentiels;
using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.Projections;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;

using Shouldly;

namespace Sepp.Planification.Application.Tests;

/// <summary>
/// ARC-33, §14.6 : envoi effectif des convocations, compensation sur clôture d'obligation, replanification urgente et
/// jours fériés reçus de Référentiels.
/// </summary>
public class SagaRepriseTests
{
    // Reprise annoncée le vendredi 18 décembre 2026 : échéance légale le 5 janvier 2027 (jours fériés exclus).
    private static readonly DateOnly Depart = new(2026, 12, 18);
    private static readonly DateOnly Echeance = new(2027, 1, 5);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Harness _h = new(Depart);

    private MessageEnvoyeHandler Envoye() => new(_h.Store, _h.Store, _h.Store, _h.Store);

    private MessageAbandonneHandler Abandonne() => new(_h.Store, _h.Store, _h.Store, _h.Store);

    private ObligationClotureeHandler Cloturee() => new(_h.Store, _h.Store, _h.Annulation, _h.Store, _h.Horloge);

    private PlanificationUrgenteDemandeeHandler Urgente() => new(_h.Store, _h.Store, _h.Store, _h.Urgence, _h.Prise, _h.Store);

    /// <summary>Obligation de reprise créée avec un créneau d'urgence : rendez-vous planifié et convocation émise.</summary>
    private async Task<(Guid ObligationId, RendezVous Rdv, ConvocationEmise Convocation)> Reprise(DateOnly? jour = null)
    {
        _h.AjouterCreneau(jour ?? Echeance, 9, typeActe: TypesExamen.ExamenReprise);
        var obligationId = Guid.CreateVersion7();
        await _h.ObligationCreee(TypesExamen.ExamenReprise, Depart, obligationId: obligationId);
        return (obligationId, _h.Store.RendezVous.Last(), _h.Store.Evenements<ConvocationEmise>().Last());
    }

    private static MessageEnvoye Message(Guid? reference, string type = "ConvocationRendezVous", bool recommande = false) =>
        new(Guid.CreateVersion7(), "rendez-vous", Guid.CreateVersion7(), reference, type, "Email", recommande, new DateTimeOffset(2026, 12, 19, 9, 0, 0, TimeSpan.Zero));

    private static MessageAbandonne Abandon(Guid? reference, string type = "ConvocationRendezVous") =>
        new(Guid.CreateVersion7(), "rendez-vous", Guid.CreateVersion7(), reference, type, "Email", false, "adresse-email-invalide",
            new DateTimeOffset(2026, 12, 19, 9, 0, 0, TimeSpan.Zero));

    // ---- Envoi effectif --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_message_envoye_enregistre_l_envoi_et_publie_convocation_envoyee_avec_les_obligations()
    {
        var (obligationId, rdv, convocation) = await Reprise();
        var message = Message(convocation.ConvocationId);

        await Envoye().HandleAsync(message, _ct);

        var envoyee = _h.Store.Evenements<ConvocationEnvoyee>().ShouldHaveSingleItem();
        envoyee.ConvocationId.ShouldBe(convocation.ConvocationId);
        envoyee.RendezVousId.ShouldBe(rdv.Id);
        envoyee.ObligationIds.ShouldBe([obligationId]);
        envoyee.DateEnvoi.ShouldBe(message.EnvoyeLe);
        var enregistree = _h.Store.Convocations.Single();
        enregistree.DateEnvoi.ShouldBe(message.EnvoyeLe);
        enregistree.MessageId.ShouldBe(message.MessageId.ToString("D"));
    }

    [Fact]
    public async Task Un_message_envoye_rejoue_ou_double_ne_publie_qu_une_fois()
    {
        var (_, _, convocation) = await Reprise();
        var message = Message(convocation.ConvocationId);

        await Envoye().HandleAsync(message, _ct);
        await Envoye().HandleAsync(message, _ct);
        await Envoye().HandleAsync(Message(convocation.ConvocationId, recommande: true), _ct);

        _h.Store.Evenements<ConvocationEnvoyee>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Les_messages_hors_convocation_ou_sans_reference_connue_sont_ignores()
    {
        var (_, _, convocation) = await Reprise();

        await Envoye().HandleAsync(Message(convocation.ConvocationId, "NotificationDocument"), _ct);
        await Envoye().HandleAsync(Message(null), _ct);
        await Envoye().HandleAsync(Message(Guid.CreateVersion7()), _ct);
        await Abandonne().HandleAsync(Abandon(convocation.ConvocationId, "RappelRendezVous"), _ct);
        await Abandonne().HandleAsync(Abandon(null), _ct);
        await Abandonne().HandleAsync(Abandon(Guid.CreateVersion7()), _ct);

        _h.Store.Evenements<ConvocationEnvoyee>().ShouldBeEmpty();
        _h.Store.Evenements<ConvocationNonRemise>().ShouldBeEmpty();
        _h.Store.Convocations.Single().DateEnvoi.ShouldBeNull();
    }

    [Fact]
    public async Task Un_message_abandonne_publie_convocation_non_remise_une_seule_fois()
    {
        var (obligationId, rdv, convocation) = await Reprise();
        var abandon = Abandon(convocation.ConvocationId);

        await Abandonne().HandleAsync(abandon, _ct);
        await Abandonne().HandleAsync(abandon, _ct);

        var nonRemise = _h.Store.Evenements<ConvocationNonRemise>().ShouldHaveSingleItem();
        nonRemise.RendezVousId.ShouldBe(rdv.Id);
        nonRemise.ObligationIds.ShouldBe([obligationId]);
        nonRemise.Date.ShouldBe(abandon.Date);
        _h.Store.Convocations.Single().DateNonRemise.ShouldBe(abandon.Date);
    }

    [Fact]
    public async Task Une_convocation_deja_partie_n_est_pas_declaree_non_remise()
    {
        var (_, _, convocation) = await Reprise();
        await Envoye().HandleAsync(Message(convocation.ConvocationId), _ct);

        await Abandonne().HandleAsync(Abandon(convocation.ConvocationId), _ct);

        _h.Store.Evenements<ConvocationNonRemise>().ShouldBeEmpty();
    }

    // ---- Compensation : obligation close -----------------------------------------------------------------------------

    private static ObligationCloturee Cloture(Guid obligationId, Guid personne, Guid affilie, string statut = "Annule") =>
        new(obligationId, personne, affilie, TypesExamen.ExamenReprise, statut, "Recalcul", new DateOnly(2026, 12, 20));

    [Fact]
    public async Task Une_obligation_close_annule_le_rendez_vous_a_venir_avec_le_motif_obligation_levee()
    {
        var (obligationId, rdv, _) = await Reprise();

        await Cloturee().HandleAsync(Cloture(obligationId, _h.Personne, _h.Affilie), _ct);

        rdv.Statut.ShouldBe(StatutRendezVous.Annule);
        rdv.MotifAnnulation.ShouldBe(MotifAnnulation.ObligationLevee);
        var annule = _h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem();
        annule.RendezVousId.ShouldBe(rdv.Id);
        annule.Motif.ShouldBe("ObligationLevee");
        _h.Store.Creneaux.Single().Statut.ShouldBe(StatutCreneau.Libre);
        _h.Store.Obligations.Single().Cloturee.ShouldBeTrue();
        _h.Store.Obligations.Single().EstAPlanifier.ShouldBeFalse();
    }

    [Fact]
    public async Task La_cloture_rejouee_n_annule_qu_une_fois()
    {
        var (obligationId, _, _) = await Reprise();
        var cloture = Cloture(obligationId, _h.Personne, _h.Affilie);

        await Cloturee().HandleAsync(cloture, _ct);
        await Cloturee().HandleAsync(cloture, _ct);

        _h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Un_rendez_vous_qui_couvre_encore_une_obligation_ouverte_est_conserve()
    {
        var premiere = _h.AjouterObligation(TypesExamen.ExamenReprise);
        var seconde = _h.AjouterObligation(TypesExamen.EvaluationPeriodique);
        var rdv = await _h.PlanifierRendezVous(_h.AjouterCreneau(Echeance, 9, typeActe: TypesExamen.ExamenReprise), premiere, seconde);

        await Cloturee().HandleAsync(Cloture(premiere.ObligationId, _h.Personne, _h.Affilie), _ct);

        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);
        _h.Store.Evenements<RendezVousAnnule>().ShouldBeEmpty();

        await Cloturee().HandleAsync(Cloture(seconde.ObligationId, _h.Personne, _h.Affilie), _ct);

        rdv.Statut.ShouldBe(StatutRendezVous.Annule);
        _h.Store.Evenements<RendezVousAnnule>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Un_rendez_vous_deja_passe_ou_termine_n_est_pas_annule_par_la_cloture()
    {
        var (obligationId, rdv, _) = await Reprise();
        _h.Horloge.Fixer(Harness.Instant(Echeance, 12));

        await Cloturee().HandleAsync(Cloture(obligationId, _h.Personne, _h.Affilie, "Realise"), _ct);

        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);
        _h.Store.Evenements<RendezVousAnnule>().ShouldBeEmpty();
        _h.Store.Obligations.Single().Cloturee.ShouldBeTrue();
    }

    [Fact]
    public async Task Une_cloture_recue_avant_la_creation_ferme_la_projection_et_la_creation_tardive_est_ignoree()
    {
        var obligationId = Guid.CreateVersion7();
        _h.AjouterCreneau(Echeance, 9, typeActe: TypesExamen.ExamenReprise);

        await Cloturee().HandleAsync(Cloture(obligationId, _h.Personne, _h.Affilie), _ct);
        await _h.ObligationCreee(TypesExamen.ExamenReprise, Depart, obligationId: obligationId);

        _h.Store.Obligations.Single().Cloturee.ShouldBeTrue();
        _h.Store.RendezVous.ShouldBeEmpty();
        _h.Store.Evenements<RendezVousPlanifie>().ShouldBeEmpty();
    }

    // ---- Planification urgente demandée ------------------------------------------------------------------------------

    private static PlanificationUrgenteDemandee Demande(Guid obligationId, Guid personne, Guid affilie, Guid? rdv, string motif) =>
        new(obligationId, rdv, personne, affilie, TypesExamen.ExamenReprise, Depart, Echeance, motif);

    [Fact]
    public async Task Un_rendez_vous_annule_est_replanifie_dans_un_creneau_d_urgence()
    {
        var (obligationId, rdv, _) = await Reprise();
        var ancienCreneau = _h.Store.Creneaux.Single();
        _h.AjouterCreneau(Echeance, 11, typeActe: TypesExamen.ExamenReprise);
        (await _h.Annulation.AnnulerAsync(rdv, MotifAnnulation.DemandeEmployeur, _ct)).ShouldBeNull();
        ancienCreneau.Statut.ShouldBe(StatutCreneau.Libre);

        await Urgente().HandleAsync(Demande(obligationId, _h.Personne, _h.Affilie, rdv.Id, "AnnulationRendezVous"), _ct);

        var nouveau = _h.Store.RendezVous.Single(r => r.Statut == StatutRendezVous.Planifie);
        nouveau.Id.ShouldNotBe(rdv.Id);
        nouveau.Origine.ShouldBe(OrigineRendezVous.Urgence);
        _h.Store.Obligations.Single().RendezVousId.ShouldBe(nouveau.Id);
    }

    [Fact]
    public async Task Une_absence_est_suivie_d_une_reconvocation_du_rendez_vous_manque()
    {
        var (obligationId, rdv, _) = await Reprise();
        _h.AjouterCreneau(Echeance, 14, typeActe: TypesExamen.ExamenReprise);
        _h.Horloge.Fixer(rdv.Debut.AddMinutes(10));
        rdv.ConstaterAbsence(_h.Horloge.GetUtcNow());
        _h.Store.Obligations.Single().Decouvrir(rdv.Id);
        _h.Horloge.Fixer(Harness.Instant(Depart.AddDays(1), 8));
        // Le créneau du rendez-vous manqué reste réservé : seul le créneau de 14 h est libre.

        await Urgente().HandleAsync(Demande(obligationId, _h.Personne, _h.Affilie, rdv.Id, "Absence"), _ct);

        var nouveau = _h.Store.RendezVous.Single(r => r.Statut == StatutRendezVous.Planifie);
        nouveau.Origine.ShouldBe(OrigineRendezVous.Reconvocation);
        nouveau.ReconvocationDeId.ShouldBe(rdv.Id);
        _h.Store.Evenements<ConvocationEmise>().Last().TypeConvocation.ShouldBe("Reconvocation");
    }

    [Fact]
    public async Task Sans_creneau_la_demande_urgente_signale_une_urgence_non_couverte()
    {
        var obligation = _h.AjouterObligation(TypesExamen.ExamenReprise, dateDue: Depart, dateLimite: Echeance);

        await Urgente().HandleAsync(Demande(obligation.ObligationId, _h.Personne, _h.Affilie, null, "Absence"), _ct);

        _h.Store.Evenements<UrgenceNonCouverte>().ShouldHaveSingleItem().ObligationId.ShouldBe(obligation.ObligationId);
        obligation.UrgenceNonCouverte.ShouldBeTrue();
    }

    [Fact]
    public async Task Une_demande_pour_une_obligation_close_ou_deja_couverte_est_ignoree()
    {
        var (obligationId, _, _) = await Reprise();
        var avant = _h.Store.RendezVous.Count;

        await Urgente().HandleAsync(Demande(obligationId, _h.Personne, _h.Affilie, null, "AnnulationRendezVous"), _ct);
        await Cloturee().HandleAsync(Cloture(obligationId, _h.Personne, _h.Affilie), _ct);
        await Urgente().HandleAsync(Demande(obligationId, _h.Personne, _h.Affilie, null, "AnnulationRendezVous"), _ct);
        await Urgente().HandleAsync(Demande(obligationId, _h.Personne, _h.Affilie, null, "MotifInconnu"), _ct);

        _h.Store.RendezVous.Count.ShouldBe(avant);
    }

    [Fact]
    public async Task Une_convocation_non_remise_est_reconvoquee_une_seule_fois()
    {
        var (obligationId, rdv, convocation) = await Reprise();
        await Abandonne().HandleAsync(Abandon(convocation.ConvocationId), _ct);
        var demande = Demande(obligationId, _h.Personne, _h.Affilie, rdv.Id, "ConvocationNonRemise");

        await Urgente().HandleAsync(demande, _ct);
        await Urgente().HandleAsync(demande, _ct);

        _h.Store.Convocations.Count(c => c.Type == TypeConvocation.Reconvocation).ShouldBe(1);
        _h.Store.Evenements<ConvocationEmise>().Count(c => c.TypeConvocation == "Reconvocation").ShouldBe(1);
        rdv.Statut.ShouldBe(StatutRendezVous.Planifie);
    }

    // ---- Jours fériés ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_jour_ferie_recu_de_referentiels_repousse_l_echeance_legale()
    {
        var jourFerie = new DateOnly(2026, 12, 21);
        _h.AjouterCreneau(Echeance.AddDays(1), 9, typeActe: TypesExamen.ExamenReprise);

        await new JoursFeriesModifiesHandler(_h.Store, _h.Store).HandleAsync(new JoursFeriesModifies(2026, [jourFerie]), _ct);
        await _h.ObligationCreee(TypesExamen.ExamenReprise, Depart);

        _h.Store.RendezVous.ShouldHaveSingleItem().Debut.ShouldBe(Harness.Instant(Echeance.AddDays(1), 9));
        _h.Store.Evenements<UrgenceNonCouverte>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_calendrier_recu_remplace_la_configuration_qui_reste_la_valeur_initiale()
    {
        _h.Options.JoursFeriesSupplementaires.Add(new DateOnly(2026, 12, 21));
        var avecConfiguration = await _h.Parametres.CalendrierAsync(Depart, Echeance, _ct);
        avecConfiguration.IsBusinessDay(new DateOnly(2026, 12, 21)).ShouldBeFalse();

        await new JoursFeriesModifiesHandler(_h.Store, _h.Store).HandleAsync(new JoursFeriesModifies(2026, []), _ct);

        var apres = await _h.Parametres.CalendrierAsync(Depart, Echeance, _ct);
        apres.IsBusinessDay(new DateOnly(2026, 12, 21)).ShouldBeTrue();
    }

    [Fact]
    public async Task Un_evenement_jours_feries_ancien_ou_sans_liste_est_ignore()
    {
        var handler = new JoursFeriesModifiesHandler(_h.Store, _h.Store);
        var recent = new JoursFeriesModifies(2026, [new DateOnly(2026, 12, 21)]);

        await handler.HandleAsync(recent, _ct);
        await handler.HandleAsync(new JoursFeriesModifies(2026, []) { OccurredAt = recent.OccurredAt.AddMinutes(-5) }, _ct);
        await handler.HandleAsync(new JoursFeriesModifies(2027), _ct);

        _h.Store.Calendriers.ShouldHaveSingleItem().JoursSupplementaires.ShouldBe([new DateOnly(2026, 12, 21)]);
    }

    [Fact]
    public void Les_types_d_urgence_viennent_des_constantes_partagees()
    {
        _h.Options.TypesUrgence.Keys.ShouldBe([TypesExamen.ExamenReprise, TypesExamen.ConsultationSpontanee, TypesExamen.VisitePreReprise], ignoreOrder: true);
        _h.Options.TypesUrgence.Keys.ShouldAllBe(k => TypesExamen.Connus.Contains(k));
    }
}
