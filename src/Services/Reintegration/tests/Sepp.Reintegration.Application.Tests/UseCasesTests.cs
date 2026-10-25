using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.Reintegration;
using Sepp.Reintegration.Application.Echanges;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Application.Minuteries;
using Sepp.Reintegration.Application.Projections;
using Sepp.Reintegration.Application.TableauDeBord;
using Sepp.Reintegration.Application.Trajets;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Trajets;

using Shouldly;

namespace Sepp.Reintegration.Application.Tests;

/// <summary>Secret médical : matrice des droits, relation de soin, motif, bris de glace et journalisation de chaque accès.</summary>
public sealed class SecretMedicalTests
{
    [Theory]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.AssistantMedical)]
    [InlineData(Roles.Cpap)]
    public async Task Un_profil_non_medical_recoit_403_et_ne_laisse_aucune_trace(string role)
    {
        var monde = new Monde();
        var id = await monde.NotifierAsync();
        monde.Agir("intrus", role, motif: "curiosité");

        (await monde.Creer<ObtenirIncapaciteHandler>().HandleAsync(new ObtenirIncapacite(id), monde.Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await monde.Creer<RechercherIncapacitesHandler>().HandleAsync(new RechercherIncapacites(monde.Personne), monde.Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await monde.Creer<ObtenirTableauDeBordHandler>().HandleAsync(new ObtenirTableauDeBord(null, null), monde.Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await monde.Creer<EstimerPotentielHandler>().HandleAsync(new EstimerPotentiel(id, new DateOnly(2026, 4, 28), ResultatEstimation.TrajetIndique, "M", "x"), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await monde.Creer<SaisirIncapaciteHandler>().HandleAsync(new SaisirIncapacite(monde.Personne, monde.Affilie, new DateOnly(2026, 3, 2), null), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        monde.Audit.Traces.ShouldBeEmpty();
    }

    [Fact]
    public async Task Hors_relation_de_soin_le_motif_est_exige_puis_chaque_acces_est_trace()
    {
        var monde = new Monde();
        var id = await monde.NotifierAsync();

        var refus = await monde.Creer<ObtenirIncapaciteHandler>().HandleAsync(new ObtenirIncapacite(id), monde.Ct);
        refus.Error!.Code.ShouldBe("reintegration.motif-obligatoire");

        monde.Agir("cpmt-1", Roles.Cpmt, motif: "Prise en charge du dossier");
        await monde.Ok(monde.Creer<PrendreEnChargeHandler>(), new PrendreEnCharge(id));
        monde.Agir("cpmt-1", Roles.Cpmt);
        (await monde.Creer<ObtenirIncapaciteHandler>().HandleAsync(new ObtenirIncapacite(id), monde.Ct)).IsSuccess.ShouldBeTrue();
        monde.Agir("inf-9", Roles.Infirmier, motif: "Remplacement urgent", brisDeGlace: true);
        (await monde.Creer<ObtenirIncapaciteHandler>().HandleAsync(new ObtenirIncapacite(id), monde.Ct)).IsSuccess.ShouldBeTrue();

        monde.Audit.Traces.ShouldAllBe(t => t.ObjetId == id);
        monde.Audit.Traces.Select(t => (t.Action, t.ObjetType, t.Motif, t.BrisDeGlace)).ShouldBe(
        [
            (ActionAudit.Modification, "reintegration.incapacite", "Prise en charge du dossier", false),
            (ActionAudit.Lecture, "reintegration.incapacite", null, false),
            (ActionAudit.Lecture, "reintegration.incapacite", "Remplacement urgent", true),
        ]);
    }

    [Fact]
    public async Task L_evaluation_et_le_formulaire_sont_reserves_au_cpmt()
    {
        var monde = new Monde();
        var (_, trajet) = await monde.TrajetAsync();
        await monde.Ok(monde.Creer<InviterTravailleurHandler>(), new InviterTravailleur(trajet, new DateOnly(2026, 5, 11), true, null));
        monde.Agir("inf-1", Roles.Infirmier, motif: "Préparation");

        (await monde.Creer<EvaluerReintegrationHandler>().HandleAsync(new EvaluerReintegration(trajet, new DateOnly(2026, 5, 20), "note"), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
}

/// <summary>SAN-60 à SAN-62 : incapacités, estimation et trajet de réintégration avec publication des contrats.</summary>
public sealed class TrajetTests
{
    [Fact]
    public async Task Une_notification_d_incapacite_est_idempotente_et_une_prolongation_actualise_le_dossier()
    {
        var monde = new Monde();
        var e = new IncapaciteNotifiee(Guid.CreateVersion7(), monde.Personne, monde.Affilie, new DateOnly(2026, 3, 2), CodesReintegration.SourcesIncapacite.MedecinConseil, new DateOnly(2026, 4, 30));
        await monde.Creer<IncapaciteNotifieeHandler>().HandleAsync(e, monde.Ct);
        await monde.Creer<IncapaciteNotifieeHandler>().HandleAsync(e, monde.Ct);
        await monde.Creer<IncapaciteNotifieeHandler>().HandleAsync(e with { DateFinPrevue = new DateOnly(2026, 6, 30), OccurredAt = e.OccurredAt.AddDays(10) }, monde.Ct);

        var incapacite = monde.Store.Incapacites.ShouldHaveSingleItem();
        incapacite.Source.ShouldBe(SourceIncapacite.MedecinConseil);
        incapacite.DateFinPrevue.ShouldBe(new DateOnly(2026, 6, 30));
        incapacite.ProchaineEcheance.ShouldBe(new DateOnly(2026, 3, 23), "alerte 5 jours ouvrables avant l'information du travailleur (30 mars)");
    }

    [Fact]
    public async Task Le_trajet_complet_publie_demarrage_et_fin_avec_les_codes_partages_sans_contenu_medical()
    {
        var monde = new Monde();
        var (incapacite, trajet) = await monde.TrajetAsync();
        await monde.Ok(monde.Creer<InviterTravailleurHandler>(), new InviterTravailleur(trajet, new DateOnly(2026, 5, 11), true, Guid.CreateVersion7()));
        await monde.Ok(monde.Creer<EvaluerReintegrationHandler>(), new EvaluerReintegration(trajet, new DateOnly(2026, 5, 20), "Note-médicale-Zorglub"));
        await monde.Ok(monde.Creer<EtablirFormulaireHandler>(), new EtablirFormulaire(trajet, new DateOnly(2026, 5, 21), ConclusionEvaluation.TravailAdapteOuAutrePossible, null));

        // L'employeur de l'affilié dépose le plan ; un autre employeur ne voit pas le trajet.
        monde.Agir("employeur-2", Roles.Employeur, affilie: Guid.CreateVersion7());
        (await monde.Creer<DeposerReponseEmployeurHandler>().HandleAsync(new DeposerReponseEmployeur(trajet, ReponseEmployeur.PlanReintegration, new DateOnly(2026, 6, 1), null), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);
        monde.Agir("employeur-1", Roles.Employeur, affilie: monde.Affilie);
        await monde.Ok(monde.Creer<DeposerReponseEmployeurHandler>(), new DeposerReponseEmployeur(trajet, ReponseEmployeur.PlanReintegration, new DateOnly(2026, 6, 1), Guid.CreateVersion7()));
        var suivi = await monde.Ok(new QueryAdapter<SuivreTrajets, IReadOnlyList<SuiviTrajetDto>>(monde.Creer<SuivreTrajetsHandler>()), new SuivreTrajets(monde.Affilie));
        suivi.ShouldHaveSingleItem().EcheanceReponseTravailleur.ShouldBe(new DateOnly(2026, 6, 15));

        // Le travailleur accepte lui-même ; le CPMT clôt le trajet.
        monde.Agir("travailleur-1", Roles.Travailleur, personne: monde.Personne);
        await monde.Ok(monde.Creer<EnregistrerReponseTravailleurHandler>(), new EnregistrerReponseTravailleur(trajet, ReponseTravailleur.Acceptation, new DateOnly(2026, 6, 8)));
        monde.Agir("cpmt-1", Roles.Cpmt);
        await monde.Ok(monde.Creer<TerminerTrajetHandler>(), new TerminerTrajet(trajet, new DateOnly(2026, 6, 8), IssueTrajet.PlanAccepte));

        monde.Publies<TrajetDemarre>().ShouldHaveSingleItem().Initiateur.ShouldBe(CodesReintegration.InitiateursTrajet.Travailleur);
        var fin = monde.Publies<TrajetTermine>().ShouldHaveSingleItem();
        fin.Statut.ShouldBe(CodesReintegration.StatutsFinTrajet.PlanAccepte);
        fin.DateFin.ShouldBe(new DateOnly(2026, 6, 8));
        monde.Store.Incapacites.Single(i => i.Id == incapacite).TrajetId.ShouldBeNull();
        foreach (var publie in monde.Store.Published)
        {
            publie.ToString().ShouldNotContain("Zorglub");
        }

        // Le suivi employeur ne porte aucune donnée médicale : pas de note d'évaluation ni d'identifiant d'évaluateur.
        typeof(SuiviTrajetDto).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Note", StringComparison.Ordinal) || n.Contains("Evaluateur", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_seul_trajet_actif_par_incapacite_et_la_demande_ne_precede_pas_l_incapacite()
    {
        var monde = new Monde();
        var (incapacite, _) = await monde.TrajetAsync();

        (await monde.Creer<DemanderTrajetHandler>().HandleAsync(new DemanderTrajet(incapacite, InitiateurTrajet.Cpmt, new DateOnly(2026, 5, 10)), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Conflict);
        var autre = await monde.Ok(monde.Creer<SaisirIncapaciteHandler>(), new SaisirIncapacite(monde.Personne, monde.Affilie, new DateOnly(2026, 6, 1), null));
        (await monde.Creer<DemanderTrajetHandler>().HandleAsync(new DemanderTrajet(autre, InitiateurTrajet.Cpmt, new DateOnly(2026, 5, 10)), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task L_annulation_de_l_incapacite_compense_le_trajet_et_les_envois_en_attente()
    {
        var monde = new Monde();
        var (incapacite, trajet) = await monde.TrajetAsync();
        var echange = await monde.Ok(monde.Creer<PreparerEchangeHandler>(),
            new PreparerEchange(incapacite, trajet, DestinataireEHealth.CoordinateurRetourAuTravail, ObjetEchange.InformationTrajet, "Information", null, true));

        await monde.Ok(monde.Creer<AnnulerIncapaciteHandler>(), new AnnulerIncapacite(incapacite));

        monde.Store.Trajets.Single().Statut.ShouldBe(StatutTrajet.Annule);
        monde.Publies<TrajetTermine>().ShouldHaveSingleItem().Statut.ShouldBe(CodesReintegration.StatutsFinTrajet.Annule);
        monde.Store.Echanges.Single(e => e.Id == echange).Statut.ShouldBe(StatutEchange.Annule);
        monde.Audit.Traces.ShouldContain(t => t.Action == ActionAudit.Suppression && t.ObjetId == incapacite);
    }

    [Fact]
    public async Task Les_delais_viennent_des_parametres_legaux_de_referentiels_quand_ils_existent()
    {
        var monde = new Monde();
        await monde.Creer<ParametreLegalModifieHandler>().HandleAsync(
            new ParametreLegalModifie("REINTEGRATION.INVITATION.DELAI", 60, "JoursCalendrier", new DateOnly(2026, 1, 1), null), monde.Ct);
        await monde.Creer<ParametreLegalModifieHandler>().HandleAsync(
            new ParametreLegalModifie("SANTE.REPRISE.DELAI", 12, "JoursOuvrables", new DateOnly(2026, 1, 1), null), monde.Ct);

        var (_, trajet) = await monde.TrajetAsync();

        monde.Store.Parametres.ShouldHaveSingleItem();
        monde.Store.Trajets.Single(t => t.Id == trajet).DateLimiteInvitation.ShouldBe(new DateOnly(2026, 7, 3));
    }

    [Fact]
    public async Task La_reprise_annoncee_termine_les_incapacites_en_cours_du_travailleur()
    {
        var monde = new Monde();
        var id = await monde.NotifierAsync();

        await monde.Creer<RepriseEnregistreeHandler>().HandleAsync(
            new RepriseEnregistree(Guid.CreateVersion7(), monde.Personne, monde.Affilie, new DateOnly(2026, 4, 20), new DateOnly(2026, 3, 2), "Interne", "Enregistree"), monde.Ct);

        var incapacite = monde.Store.Incapacites.Single(i => i.Id == id);
        incapacite.Statut.ShouldBe(StatutIncapacite.Terminee);
        incapacite.DateFin.ShouldBe(new DateOnly(2026, 4, 19));
        incapacite.ProchaineEcheance.ShouldBeNull();
    }

    [Fact]
    public void Les_codes_emis_sont_exactement_les_codes_partages()
    {
        Enum.GetValues<InitiateurTrajet>().Select(i => i.Code()).ShouldBe(CodesReintegration.InitiateursTrajet.Connus, ignoreOrder: true);
        Enum.GetValues<IssueTrajet>().Select(i => i.Code()).ShouldBe(CodesReintegration.StatutsFinTrajet.Connus, ignoreOrder: true);
        Enum.GetValues<SourceIncapacite>().Select(s => s.Code()).ShouldBe(CodesReintegration.SourcesIncapacite.Connus, ignoreOrder: true);
        Enum.GetValues<SourceIncapacite>().ShouldAllBe(s => CodesContrats.Source(s.Code()) == s);
    }
}

/// <summary>SAN-63, SAN-64 : compteur d'absences, information du médecin-conseil et échanges eHealth.</summary>
public sealed class AbsencesEtEHealthTests
{
    [Fact]
    public async Task Deux_absences_constatees_sur_une_obligation_de_reintegration_informent_le_medecin_conseil_par_ehealth()
    {
        var monde = new Monde();
        var (_, trajet) = await monde.TrajetAsync();
        var obligation = Guid.CreateVersion7();
        await monde.Creer<ObligationCreeeHandler>().HandleAsync(
            new ObligationCreee(obligation, monde.Personne, monde.Affilie, TypesExamen.EvaluationReintegration, new DateOnly(2026, 5, 4), new DateOnly(2026, 6, 22)), monde.Ct);
        await monde.Creer<ObligationCreeeHandler>().HandleAsync(
            new ObligationCreee(Guid.CreateVersion7(), monde.Personne, monde.Affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 5, 4), null), monde.Ct);
        monde.Store.Obligations.ShouldHaveSingleItem();

        var absence = new AbsenceRendezVousConstatee(Guid.CreateVersion7(), monde.Personne, monde.Affilie, new DateTimeOffset(2026, 5, 12, 8, 0, 0, TimeSpan.Zero), [obligation]);
        await monde.Creer<AbsenceRendezVousConstateeHandler>().HandleAsync(absence, monde.Ct);
        await monde.Creer<AbsenceRendezVousConstateeHandler>().HandleAsync(absence, monde.Ct);
        await monde.Creer<AbsenceRendezVousConstateeHandler>().HandleAsync(
            absence with { RendezVousId = Guid.CreateVersion7(), ObligationIds = [Guid.CreateVersion7()] }, monde.Ct);
        monde.Store.Echanges.ShouldBeEmpty();
        await monde.Creer<AbsenceRendezVousConstateeHandler>().HandleAsync(
            absence with { RendezVousId = Guid.CreateVersion7(), Debut = new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero) }, monde.Ct);

        var t = monde.Store.Trajets.Single(x => x.Id == trajet);
        t.Absences.Count.ShouldBe(2);
        var notification = monde.Store.Echanges.ShouldHaveSingleItem();
        notification.Destinataire.ShouldBe(DestinataireEHealth.MedecinConseil);
        notification.Objet.ShouldBe(ObjetEchange.NotificationAbsences);

        // Envoi par le canal (service d'arrière-plan) : panne puis succès.
        monde.Canal.EnPanne = true;
        (await monde.Creer<EnvoiEchangesEHealth>().ExecuterLotAsync(monde.Ct)).EnErreur.ShouldBe(1);
        notification.Statut.ShouldBe(StatutEchange.EnErreur);
        monde.Canal.EnPanne = false;
        (await monde.Creer<EnvoiEchangesEHealth>().ExecuterLotAsync(monde.Ct)).Envoyes.ShouldBe(1);
        notification.Statut.ShouldBe(StatutEchange.Envoye);
        monde.Canal.Envoyes.ShouldHaveSingleItem().ToString().ShouldNotContain("REINTEGRATION.ABSENCES");
    }

    [Fact]
    public async Task L_echange_avec_le_medecin_traitant_exige_l_accord_et_les_reponses_sont_rattachees()
    {
        var monde = new Monde();
        var (incapacite, trajet) = await monde.TrajetAsync();
        (await monde.Creer<PreparerEchangeHandler>().HandleAsync(
                new PreparerEchange(incapacite, trajet, DestinataireEHealth.MedecinTraitant, ObjetEchange.DemandeInformation, "Question", "INAMI-1", false), monde.Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
        var id = await monde.Ok(monde.Creer<PreparerEchangeHandler>(),
            new PreparerEchange(incapacite, trajet, DestinataireEHealth.MedecinTraitant, ObjetEchange.DemandeInformation, "Question", "INAMI-1", true));
        await monde.Creer<EnvoiEchangesEHealth>().ExecuterLotAsync(monde.Ct);
        var reference = monde.Store.Echanges.Single(e => e.Id == id).ReferenceEHealth!;

        monde.Canal.AReceptionner.Enqueue(new MessageEHealthRecu("EH-REP-1", reference, "Réponse", Monde.Debut));
        monde.Canal.AReceptionner.Enqueue(new MessageEHealthRecu("EH-REP-2", "INCONNUE", "Réponse orpheline", Monde.Debut));
        (await monde.Creer<ReceptionEchangesEHealth>().ExecuterAsync(monde.Ct)).ShouldBe(1);
        monde.Canal.AReceptionner.Enqueue(new MessageEHealthRecu("EH-REP-1", reference, "Réponse", Monde.Debut));
        (await monde.Creer<ReceptionEchangesEHealth>().ExecuterAsync(monde.Ct)).ShouldBe(0);

        var liste = await new QueryAdapter<ListerEchanges, IReadOnlyList<EchangeDto>>(monde.Creer<ListerEchangesHandler>()).HandleAsync(new ListerEchanges(incapacite), monde.Ct);
        liste.Value.Single(e => e.Sens == SensEchange.Entrant).EnReponseA.ShouldBe(id);
    }
}

/// <summary>SAN-65 et ADR 0008 : tableau de bord des délais et minuteries en base.</summary>
public sealed class DelaisTests
{
    [Fact]
    public async Task Le_tableau_de_bord_classe_les_depassements_puis_les_alertes_et_trace_chaque_dossier()
    {
        var monde = new Monde();
        var (incapacite, _) = await monde.TrajetAsync();
        monde.Horloge.Maintenant = new DateTimeOffset(2026, 3, 31, 8, 0, 0, TimeSpan.Zero);

        var tableau = await new QueryAdapter<ObtenirTableauDeBord, TableauDeBordDto>(monde.Creer<ObtenirTableauDeBordHandler>())
            .HandleAsync(new ObtenirTableauDeBord(monde.Affilie, null), monde.Ct);

        var lignes = tableau.Value.Lignes;
        lignes[0].Type.ShouldBe(TypeEcheance.InformationTravailleur);
        lignes[0].Statut.ShouldBe(StatutEcheance.Depassee);
        lignes.ShouldContain(l => l.Type == TypeEcheance.InvitationEvaluation && l.Statut == StatutEcheance.AVenir && l.TrajetId != null);
        lignes.ShouldNotContain(l => l.Type == TypeEcheance.TrajetObligatoire, "le trajet est demandé : échéance respectée, omise");
        tableau.Value.Depassees.ShouldBe(1);
        monde.Audit.Traces.ShouldContain(t => t.ObjetType == "reintegration.tableau-de-bord" && t.ObjetId == incapacite);
    }

    [Fact]
    public async Task Les_minuteries_echues_sont_declenchees_une_seule_fois()
    {
        var monde = new Monde();
        await monde.NotifierAsync();
        monde.Horloge.Maintenant = new DateTimeOffset(2026, 3, 24, 8, 0, 0, TimeSpan.Zero);

        var premier = await monde.Creer<TraiterMinuteries>().ExecuterLotAsync(monde.Ct);
        var second = await monde.Creer<TraiterMinuteries>().ExecuterLotAsync(monde.Ct);

        premier.MinuteriesDeclenchees.ShouldBe(1);
        second.DossiersTraites.ShouldBe(0);
        monde.Store.Incapacites.Single().MinuteriesDeclenchees.ShouldHaveSingleItem().ShouldStartWith("InformationTravailleur.Proche");
    }
}

/// <summary>SAN-60, SAN-02 : publication des incapacités saisies à la main (reintegration.incapacite-saisie, ARC-06).</summary>
public sealed class PublicationIncapaciteTests
{
    [Fact]
    public async Task Une_incapacite_saisie_a_la_main_est_publiee_avec_identifiants_dates_et_categorie()
    {
        var monde = new Monde();

        var id = await monde.Ok(monde.Creer<SaisirIncapaciteHandler>(),
            new SaisirIncapacite(monde.Personne, monde.Affilie, new DateOnly(2026, 3, 2), new DateOnly(2026, 4, 30)));

        var publie = monde.Publies<IncapaciteSaisie>().ShouldHaveSingleItem();
        publie.IncapaciteId.ShouldBe(id);
        publie.PersonneId.ShouldBe(monde.Personne);
        publie.AffilieId.ShouldBe(monde.Affilie);
        publie.DateDebut.ShouldBe(new DateOnly(2026, 3, 2));
        publie.DateFinPrevue.ShouldBe(new DateOnly(2026, 4, 30));
        publie.Source.ShouldBe(CodesReintegration.SourcesIncapacite.SaisieManuelle);
    }

    [Fact]
    public async Task Une_incapacite_recue_par_ehealth_n_est_pas_republiee_par_reintegration()
    {
        var monde = new Monde();

        await monde.NotifierAsync();

        monde.Publies<IncapaciteSaisie>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_saisie_refusee_ne_publie_rien()
    {
        var monde = new Monde();
        monde.Agir("emp-1", Roles.Employeur);

        (await monde.Creer<SaisirIncapaciteHandler>().HandleAsync(new SaisirIncapacite(monde.Personne, monde.Affilie, new DateOnly(2026, 3, 2), null), monde.Ct))
            .IsSuccess.ShouldBeFalse();
        monde.Agir("cpmt-1", Roles.Cpmt);
        (await monde.Creer<SaisirIncapaciteHandler>().HandleAsync(new SaisirIncapacite(monde.Personne, monde.Affilie, new DateOnly(2026, 3, 2), new DateOnly(2026, 2, 1)), monde.Ct))
            .IsSuccess.ShouldBeFalse();

        monde.Publies<IncapaciteSaisie>().ShouldBeEmpty();
    }
}

/// <summary>Accès à un gestionnaire de requête sans connaître son type concret (pour <see cref="Monde.Ok{TCommand,TResult}"/>).</summary>
internal sealed class QueryAdapter<TQuery, TResult>(IQueryHandler<TQuery, TResult> handler) : ICommandHandler<TQuery, TResult>
{
    public Task<Result<TResult>> HandleAsync(TQuery command, CancellationToken cancellationToken) => handler.HandleAsync(command, cancellationToken);
}
