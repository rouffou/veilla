using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Trajets;

using Shouldly;

namespace Sepp.Reintegration.Domain.Tests;

internal static class Politiques
{
    public static readonly DateTimeOffset Maintenant = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Valeurs du §2.1 (catalogue de Référentiels) ; alerte 5 jours ouvrables avant chaque échéance.</summary>
    public static PolitiqueReintegration Defaut(int alerte = 5, DureeLegale? planEmployeur = null) => new(
        new(4, UniteDelai.Semaines), new(8, UniteDelai.Semaines), new(6, UniteDelai.Mois), 20, 2,
        new(49, UniteDelai.JoursCalendrier), new(14, UniteDelai.JoursCalendrier), planEmployeur, alerte, BusinessCalendar.Belgian(2025, 2026, 2027));
}

public sealed class IncapaciteTests
{
    private static readonly DateOnly Debut = new(2026, 3, 2);

    private static Incapacite Nouvelle(int? effectif = null)
    {
        var incapacite = Incapacite.Notifier(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Debut, new DateOnly(2026, 4, 30), SourceIncapacite.Employeur, Politiques.Maintenant);
        if (effectif is { } e)
        {
            incapacite.DefinirEffectifEmployeur(e);
        }

        return incapacite;
    }

    [Fact]
    public void Les_echeances_legales_suivent_le_debut_de_l_incapacite()
    {
        var echeances = Nouvelle().Echeances(Politiques.Defaut());

        echeances.Single(e => e.Type == TypeEcheance.InformationTravailleur).Date.ShouldBe(new DateOnly(2026, 3, 30));
        echeances.Single(e => e.Type == TypeEcheance.EstimationPotentiel).Date.ShouldBe(new DateOnly(2026, 4, 27));
        echeances.Single(e => e.Type == TypeEcheance.TrajetObligatoire).Date.ShouldBe(new DateOnly(2026, 9, 2));
    }

    [Fact]
    public void Le_trajet_obligatoire_n_est_suivi_qu_a_partir_du_seuil_de_travailleurs()
    {
        Nouvelle(effectif: 19).Echeances(Politiques.Defaut()).ShouldNotContain(e => e.Type == TypeEcheance.TrajetObligatoire);
        Nouvelle(effectif: 20).Echeances(Politiques.Defaut()).ShouldContain(e => e.Type == TypeEcheance.TrajetObligatoire);
    }

    [Fact]
    public void Une_echeance_est_proche_dans_la_fenetre_d_alerte_en_jours_ouvrables_puis_depassee()
    {
        var information = Nouvelle().Echeances(Politiques.Defaut()).Single(e => e.Type == TypeEcheance.InformationTravailleur);

        // 30 mars 2026 ; 5 jours ouvrables avant : 23 mars.
        information.DebutAlerte(Politiques.Defaut()).ShouldBe(new DateOnly(2026, 3, 23));
        information.StatutAu(new DateOnly(2026, 3, 20), Politiques.Defaut()).ShouldBe(StatutEcheance.AVenir);
        information.StatutAu(new DateOnly(2026, 3, 23), Politiques.Defaut()).ShouldBe(StatutEcheance.Proche);
        information.StatutAu(new DateOnly(2026, 3, 30), Politiques.Defaut()).ShouldBe(StatutEcheance.Proche);
        information.StatutAu(new DateOnly(2026, 3, 31), Politiques.Defaut()).ShouldBe(StatutEcheance.Depassee);
        information.StatutAu(new DateOnly(2026, 3, 23), Politiques.Defaut(alerte: 0)).ShouldBe(StatutEcheance.AVenir);
    }

    [Fact]
    public void Les_minuteries_se_declenchent_une_seule_fois_et_s_arretent_quand_l_echeance_est_respectee()
    {
        var incapacite = Nouvelle(effectif: 5);
        var politique = Politiques.Defaut();
        incapacite.Reprogrammer(politique);
        incapacite.ProchaineEcheance.ShouldBe(new DateOnly(2026, 3, 23));

        incapacite.DeclencherMinuteries(new DateOnly(2026, 3, 23), politique).ShouldHaveSingleItem().ShouldStartWith("InformationTravailleur.Proche");
        incapacite.DeclencherMinuteries(new DateOnly(2026, 3, 23), politique).ShouldBeEmpty();
        incapacite.EnregistrerInformationTravailleur(new DateOnly(2026, 3, 24), null);
        incapacite.Reprogrammer(politique);

        // Prochaine : l'alerte de l'estimation (27 avril, 5 jours ouvrables avant = 20 avril).
        incapacite.ProchaineEcheance.ShouldBe(new DateOnly(2026, 4, 20));
        incapacite.Echeances(politique).Single(e => e.Type == TypeEcheance.InformationTravailleur).StatutAu(new DateOnly(2026, 4, 1), politique)
            .ShouldBe(StatutEcheance.Respectee);
    }

    [Fact]
    public void Une_notification_plus_ancienne_est_ignoree_et_une_prolongation_actualise_le_dossier()
    {
        var incapacite = Nouvelle();
        incapacite.Actualiser(incapacite.PersonneId, incapacite.AffilieId, Debut, new DateOnly(2026, 6, 30), SourceIncapacite.MedecinConseil, Politiques.Maintenant.AddDays(1))
            .ShouldBeTrue();
        incapacite.DateFinPrevue.ShouldBe(new DateOnly(2026, 6, 30));
        incapacite.Actualiser(incapacite.PersonneId, incapacite.AffilieId, Debut, new DateOnly(2026, 4, 30), SourceIncapacite.Employeur, Politiques.Maintenant)
            .ShouldBeFalse();
        incapacite.DateFinPrevue.ShouldBe(new DateOnly(2026, 6, 30));
        Should.Throw<DomainException>(() => incapacite.Actualiser(Guid.CreateVersion7(), incapacite.AffilieId, Debut, null, SourceIncapacite.Employeur, Politiques.Maintenant.AddDays(2)));
    }

    [Fact]
    public void L_estimation_du_potentiel_respecte_l_echeance_et_rend_l_auteur_en_relation_de_soin()
    {
        var incapacite = Nouvelle();
        incapacite.EstSuiviePar("inf-1").ShouldBeFalse();
        incapacite.EstimerPotentiel(new DateOnly(2026, 4, 28), ResultatEstimation.TrajetIndique, "METHODE_STANDARD", "inf-1", "Commentaire");

        incapacite.EstSuiviePar("inf-1").ShouldBeTrue();
        incapacite.Echeances(Politiques.Defaut()).Single(e => e.Type == TypeEcheance.EstimationPotentiel).RespecteeLe.ShouldBe(new DateOnly(2026, 4, 28));
        Should.Throw<DomainException>(() => incapacite.EstimerPotentiel(new DateOnly(2026, 2, 1), ResultatEstimation.TrajetIndique, "M", "inf-1", null));
    }

    [Fact]
    public void Une_incapacite_terminee_ou_annulee_n_a_plus_d_echeance_et_l_annulation_signale_le_trajet()
    {
        var terminee = Nouvelle();
        terminee.Terminer(new DateOnly(2026, 3, 20)).ShouldBeTrue();
        terminee.Terminer(new DateOnly(2026, 3, 21)).ShouldBeFalse();
        terminee.Echeances(Politiques.Defaut()).ShouldBeEmpty();

        var annulee = Nouvelle();
        var trajet = Guid.CreateVersion7();
        annulee.LierTrajet(trajet, new DateOnly(2026, 4, 1));
        annulee.Annuler(Politiques.Maintenant).ShouldBeTrue();
        annulee.DomainEvents.OfType<IncapaciteAnnulee>().ShouldHaveSingleItem().TrajetId.ShouldBe(trajet);
        annulee.Annuler(Politiques.Maintenant).ShouldBeFalse();
    }
}

public sealed class TrajetTests
{
    private static readonly DateOnly Demande = new(2026, 5, 4);

    private static Trajet Nouveau() =>
        Trajet.Demander(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), InitiateurTrajet.Travailleur, Demande, Politiques.Defaut(), Politiques.Maintenant);

    private static Trajet AvecPlan()
    {
        var trajet = Nouveau();
        trajet.Inviter(new DateOnly(2026, 5, 11), true, Guid.CreateVersion7());
        trajet.Evaluer(new DateOnly(2026, 5, 20), "cpmt-1", "Note médicale");
        trajet.EtablirFormulaire(new DateOnly(2026, 5, 21), ConclusionEvaluation.TravailAdapteOuAutrePossible, null, Politiques.Defaut());
        trajet.DeposerReponseEmployeur(ReponseEmployeur.PlanReintegration, new DateOnly(2026, 6, 1), Guid.CreateVersion7(), Politiques.Defaut());
        return trajet;
    }

    [Fact]
    public void La_demande_recopie_la_date_limite_d_invitation_et_publie_le_demarrage()
    {
        var trajet = Nouveau();

        trajet.DateLimiteInvitation.ShouldBe(new DateOnly(2026, 6, 22));
        trajet.Statut.ShouldBe(StatutTrajet.Demande);
        trajet.DomainEvents.OfType<TrajetDemarreLocal>().ShouldHaveSingleItem().Initiateur.ShouldBe(InitiateurTrajet.Travailleur);
        trajet.Etapes.ShouldHaveSingleItem().Echeance.ShouldBe(new DateOnly(2026, 6, 22));
    }

    [Fact]
    public void Le_workflow_complet_derive_les_statuts_et_ouvre_le_delai_de_reponse_du_travailleur()
    {
        var trajet = AvecPlan();

        trajet.Statut.ShouldBe(StatutTrajet.PlanDepose);
        trajet.EcheanceReponseTravailleur.ShouldBe(new DateOnly(2026, 6, 15));
        trajet.EnregistrerReponseTravailleur(ReponseTravailleur.Acceptation, new DateOnly(2026, 6, 10));
        trajet.Statut.ShouldBe(StatutTrajet.ReponseTravailleurRecue);
        trajet.Terminer(new DateOnly(2026, 6, 10), IssueTrajet.PlanAccepte, Politiques.Maintenant);

        trajet.Statut.ShouldBe(StatutTrajet.Termine);
        trajet.DomainEvents.OfType<TrajetTermineLocal>().ShouldHaveSingleItem().Issue.ShouldBe(IssueTrajet.PlanAccepte);
        trajet.Echeances(Politiques.Defaut()).ShouldBeEmpty();
        trajet.Etapes.Select(e => e.Type).ShouldBe(
        [
            TypeEtape.Demande, TypeEtape.Invitation, TypeEtape.Evaluation, TypeEtape.Formulaire, TypeEtape.PlanReintegration,
            TypeEtape.ReponseTravailleur, TypeEtape.Fin,
        ]);
    }

    [Fact]
    public void Les_etapes_hors_ordre_et_une_issue_incoherente_sont_refusees()
    {
        var trajet = Nouveau();
        Should.Throw<DomainException>(() => trajet.Evaluer(new DateOnly(2026, 5, 20), "cpmt-1", null));
        Should.Throw<DomainException>(() => trajet.Inviter(new DateOnly(2026, 5, 1), false, null));

        var avecPlan = AvecPlan();
        Should.Throw<DomainException>(() => avecPlan.Terminer(new DateOnly(2026, 6, 20), IssueTrajet.RapportMotive, Politiques.Maintenant));
        Should.Throw<DomainException>(() => avecPlan.Terminer(new DateOnly(2026, 6, 20), IssueTrajet.Annule, Politiques.Maintenant));
    }

    [Fact]
    public void Le_rapport_motive_n_ouvre_pas_de_delai_de_reponse_et_termine_le_trajet()
    {
        var trajet = Nouveau();
        trajet.Inviter(new DateOnly(2026, 5, 11), true, null);
        trajet.Evaluer(new DateOnly(2026, 5, 20), "cpmt-1", null);
        trajet.EtablirFormulaire(new DateOnly(2026, 5, 21), ConclusionEvaluation.AucunTravailEnvisageable, null, Politiques.Defaut());
        trajet.DeposerReponseEmployeur(ReponseEmployeur.RapportMotive, new DateOnly(2026, 6, 1), null, Politiques.Defaut());

        trajet.EcheanceReponseTravailleur.ShouldBeNull();
        Should.Throw<DomainException>(() => trajet.EnregistrerReponseTravailleur(ReponseTravailleur.Refus, new DateOnly(2026, 6, 2)));
        trajet.Terminer(new DateOnly(2026, 6, 2), IssueTrajet.RapportMotive, Politiques.Maintenant);
        trajet.Issue.ShouldBe(IssueTrajet.RapportMotive);
    }

    [Fact]
    public void L_annulation_compense_une_seule_fois_et_est_refusee_apres_la_fin()
    {
        var trajet = Nouveau();
        trajet.Annuler(new DateOnly(2026, 5, 6), Politiques.Maintenant).ShouldBeTrue();
        trajet.Annuler(new DateOnly(2026, 5, 7), Politiques.Maintenant).ShouldBeFalse();
        trajet.Statut.ShouldBe(StatutTrajet.Annule);
        trajet.DomainEvents.OfType<TrajetTermineLocal>().ShouldHaveSingleItem().Issue.ShouldBe(IssueTrajet.Annule);

        var termine = AvecPlan();
        termine.EnregistrerReponseTravailleur(ReponseTravailleur.Refus, new DateOnly(2026, 6, 3));
        termine.Terminer(new DateOnly(2026, 6, 3), IssueTrajet.PlanRefuse, Politiques.Maintenant);
        Should.Throw<DomainException>(() => termine.Annuler(new DateOnly(2026, 6, 4), Politiques.Maintenant));
    }

    [Fact]
    public void Au_seuil_d_absences_le_medecin_conseil_est_a_informer_une_seule_fois_et_les_doublons_sont_ignores()
    {
        var trajet = Nouveau();
        var politique = Politiques.Defaut();
        var rendezVous = Guid.CreateVersion7();
        var aujourdHui = new DateOnly(2026, 5, 30);

        trajet.EnregistrerAbsence(rendezVous, new DateOnly(2026, 5, 12), aujourdHui, politique, Politiques.Maintenant).ShouldBeTrue();
        trajet.EnregistrerAbsence(rendezVous, new DateOnly(2026, 5, 12), aujourdHui, politique, Politiques.Maintenant).ShouldBeFalse();
        trajet.DomainEvents.OfType<SeuilAbsencesAtteint>().ShouldBeEmpty();
        trajet.EnregistrerAbsence(null, new DateOnly(2026, 5, 26), aujourdHui, politique, Politiques.Maintenant).ShouldBeTrue();
        trajet.EnregistrerAbsence(null, new DateOnly(2026, 5, 28), aujourdHui, politique, Politiques.Maintenant).ShouldBeTrue();

        trajet.DomainEvents.OfType<SeuilAbsencesAtteint>().ShouldHaveSingleItem().NombreAbsences.ShouldBe(2);
        trajet.MedecinConseilInformeLe.ShouldBe(aujourdHui);
        trajet.Absences.Count(a => a.NotifieMedecinConseil).ShouldBe(2);
        trajet.Absences.Count.ShouldBe(3);
    }

    [Fact]
    public void Le_delai_du_plan_employeur_n_est_suivi_que_s_il_est_configure()
    {
        var sans = AvecPlan();
        sans.Echeances(Politiques.Defaut()).ShouldNotContain(e => e.Type == TypeEcheance.PlanEmployeur);

        var politique = Politiques.Defaut(planEmployeur: new DureeLegale(30, UniteDelai.JoursCalendrier));
        var trajet = Nouveau();
        trajet.Inviter(new DateOnly(2026, 5, 11), true, null);
        trajet.Evaluer(new DateOnly(2026, 5, 20), "cpmt-1", null);
        trajet.EtablirFormulaire(new DateOnly(2026, 5, 21), ConclusionEvaluation.TravailAdapteOuAutrePossible, null, politique);
        trajet.Echeances(politique).Single(e => e.Type == TypeEcheance.PlanEmployeur).Date.ShouldBe(new DateOnly(2026, 6, 20));
    }
}

public sealed class EchangeEHealthTests
{
    private static EchangeEHealth Vers(DestinataireEHealth destinataire, bool accord) =>
        EchangeEHealth.Preparer(Guid.CreateVersion7(), null, Guid.CreateVersion7(), destinataire, ObjetEchange.DemandeInformation, "Contenu", null, accord, Politiques.Maintenant);

    [Fact]
    public void L_echange_avec_le_medecin_traitant_exige_l_accord_du_travailleur()
    {
        Should.Throw<DomainException>(() => Vers(DestinataireEHealth.MedecinTraitant, accord: false));
        Vers(DestinataireEHealth.MedecinTraitant, accord: true).Statut.ShouldBe(StatutEchange.AEnvoyer);
        Vers(DestinataireEHealth.MedecinConseil, accord: false).Statut.ShouldBe(StatutEchange.AEnvoyer);
    }

    [Fact]
    public void Les_echecs_sont_retentes_jusqu_a_l_abandon_puis_relances_manuellement()
    {
        var echange = Vers(DestinataireEHealth.CoordinateurRetourAuTravail, accord: true);
        echange.MarquerEchec("ehealth.indisponible", 2);
        echange.Statut.ShouldBe(StatutEchange.EnErreur);
        echange.EstAEnvoyer.ShouldBeTrue();
        echange.MarquerEchec("ehealth.indisponible", 2);
        echange.Statut.ShouldBe(StatutEchange.Abandonne);
        echange.EstAEnvoyer.ShouldBeFalse();

        echange.Relancer();
        echange.MarquerEnvoye("REF-1", Politiques.Maintenant);
        echange.Statut.ShouldBe(StatutEchange.Envoye);
        echange.Annuler().ShouldBeFalse();
        Should.Throw<DomainException>(echange.Relancer);
    }

    [Fact]
    public void Une_reponse_recue_est_rattachee_a_l_envoi_d_origine()
    {
        var envoi = Vers(DestinataireEHealth.MedecinTraitant, accord: true);
        envoi.MarquerEnvoye("REF-1", Politiques.Maintenant);
        var reponse = EchangeEHealth.Recevoir(envoi, "REF-2", "Réponse", Politiques.Maintenant);

        reponse.Sens.ShouldBe(SensEchange.Entrant);
        reponse.Statut.ShouldBe(StatutEchange.Recu);
        reponse.EnReponseA.ShouldBe(envoi.Id);
        reponse.IncapaciteId.ShouldBe(envoi.IncapaciteId);
    }
}
