using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Contracts.Decisions;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Prevention;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.SurveillanceMedicale.Application.Conservation;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Decisions;
using Sepp.SurveillanceMedicale.Application.Dossiers;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Application.Projections;
using Sepp.SurveillanceMedicale.Application.Transferts;
using Sepp.SurveillanceMedicale.Application.Vaccinations;
using Sepp.SurveillanceMedicale.Domain;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Projections;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Application.Tests;

/// <summary>Monde de test : un magasin partagé, un utilisateur et un contexte d'accès par appel.</summary>
internal sealed class Monde
{
    public InMemoryStore Store { get; } = new();

    public FakeAudit Audit { get; }

    public FakeContexte Contexte { get; } = new();

    public FakeHorloge Horloge { get; } = new(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero));

    public FakeSignature Signature { get; } = new();

    public Monde() => Audit = new FakeAudit(Store);

    public ICurrentUser Utilisateur { get; set; } = new FakeUser("cpmt-1", Roles.Cpmt);

    public GardeDossier Garde => new(Store, Store, Store, Utilisateur, Contexte, Audit, Store);

    public ParametresMedicaux Parametres => new(Store);

    public AccesExamens AccesExamens => new(Store, Garde);

    public AccesDecisions AccesDecisions => new(Store, Garde, Utilisateur, Contexte, Audit);

    public ExportDossiers Export => new(Store, Store, Store, Store, Horloge);

    public void Agir(string userId, string role, string? motif = null, bool brisDeGlace = false, Guid? affilie = null)
    {
        Utilisateur = new FakeUser(userId, role);
        Contexte.Motif = motif;
        Contexte.BrisDeGlace = brisDeGlace;
        Contexte.AffilieIdJeton = affilie;
    }

    public async Task<Guid> OuvrirDossierAsync(Guid personneId, CancellationToken ct) =>
        (await new OuvrirDossierHandler(Store, Garde, Utilisateur, Store, Horloge).HandleAsync(new OuvrirDossier(personneId, null), ct)).Value;

    public async Task<Guid> OuvrirExamenAsync(Guid dossierId, Guid affilieId, CancellationToken ct, string type = TypesExamen.EvaluationPeriodique) =>
        (await new OuvrirExamenHandler(Garde, Store, Store, Store, Utilisateur, Store, Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, type, affilieId, null, null), ct)).Value;
}

public sealed class SecretMedicalTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Monde _monde = new();
    private readonly Guid _personne = Guid.CreateVersion7();

    [Theory]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Cpap)]
    [InlineData(Roles.AssistantMedical)]
    public async Task Les_profils_non_medicaux_n_accedent_jamais_au_dossier(string role)
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Audit.Traces.Clear();
        _monde.Agir("autre", role, motif: "Je veux voir");

        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ExporterDossierHandler(_monde.Garde, _monde.Export).HandleAsync(new ExporterDossier(dossierId), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new TrouverDossierHandler(_monde.Store, _monde.Garde, _monde.Audit, _monde.Contexte).HandleAsync(new TrouverDossier(_personne), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new AjouterExpositionHandler(_monde.Garde, _monde.Store).HandleAsync(new AjouterExposition(dossierId, "BRUIT", "ELEVE", new DateOnly(2026, 1, 1), null), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _monde.Audit.Traces.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_gestionnaire_lit_sans_motif_et_chaque_lecture_est_tracee_avant_d_etre_servie()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Audit.Traces.Single().ShouldBe(new Trace(ActionAudit.Creation, "dossier-sante.dossier", dossierId, null, false, false));

        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).IsSuccess.ShouldBeTrue();
        (await new ObtenirVueConsultationHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Store, new RisquesPersonne(_monde.Store), _monde.Horloge)
            .HandleAsync(new ObtenirVueConsultation(dossierId, null), _ct)).IsSuccess.ShouldBeTrue();
        (await new ExporterDossierHandler(_monde.Garde, _monde.Export).HandleAsync(new ExporterDossier(dossierId), _ct)).IsSuccess.ShouldBeTrue();

        _monde.Audit.Traces.Select(t => (t.Action, t.ObjetType)).ShouldBe(
        [
            (ActionAudit.Creation, "dossier-sante.dossier"),
            (ActionAudit.Lecture, "dossier-sante.dossier"),
            (ActionAudit.Lecture, "dossier-sante.consultation"),
            (ActionAudit.Export, "dossier-sante.export"),
        ]);
        _monde.Audit.Traces.Where(t => t.Action == ActionAudit.Lecture).ShouldAllBe(t => t.Immediate);
    }

    [Fact]
    public async Task Hors_relation_de_soin_le_motif_est_obligatoire_et_le_bris_de_glace_est_signale()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Audit.Traces.Clear();

        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
        _monde.Audit.Traces.ShouldBeEmpty();

        _monde.Agir("cpmt-2", Roles.Cpmt, brisDeGlace: true);
        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).Error!.Code.ShouldBe("audit.motif-obligatoire");

        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr X en congé", brisDeGlace: true);
        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).IsSuccess.ShouldBeTrue();
        _monde.Audit.Traces.Single().ShouldBe(new Trace(ActionAudit.Lecture, "dossier-sante.dossier", dossierId, "Remplacement du Dr X en congé", true, true));
    }

    [Fact]
    public async Task L_infirmier_qui_ouvre_un_examen_sur_rendez_vous_entre_en_relation_de_soin()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var affilie = Guid.CreateVersion7();
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 10), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId], "infirmier-1");
        _monde.Store.RendezVous.Add(rdv);

        _monde.Agir("infirmier-1", Roles.Infirmier);
        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");

        var examenId = (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).Value;
        var examen = _monde.Store.Examens.Single(e => e.Id == examenId);
        examen.ShouldSatisfyAllConditions(e => e.AffilieId.ShouldBe(affilie), e => e.TypeExamen.ShouldBe(TypesExamen.EvaluationPeriodique), e => e.ObligationIds.ShouldBe([obligation.ObligationId]));

        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).IsSuccess.ShouldBeTrue();

        // #401, ADR 0017 : l'infirmier rédige et modifie la décision (decision:ecrire) ; l'auteur légal n'est fixé qu'à la signature.
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;
        _monde.Store.Decisions.Single(d => d.Id == decisionId).ShouldSatisfyAllConditions(
            d => d.AuteurId.ShouldBeNull(), d => d.RedigeePar.ShouldBe("infirmier-1"), d => d.VersionContenu.ShouldBe(1));
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).IsSuccess.ShouldBeTrue();
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).Error!.Code.ShouldBe("decision.version-obsolete");
        (await Modifier(decisionId, null, ["PAS_TRAVAIL_NUIT"])).Error!.Code.ShouldBe("decision.version-requise");

        // Actes du CPMT refusés à l'infirmier : clôture, signature, issue d'un recours, fréquence (art. I.4-32), gestion du dossier.
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Signer(decisionId, 2)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new EnregistrerIssueRecoursHandler(_monde.AccesDecisions, _monde.Store, _monde.Store)
            .HandleAsync(new EnregistrerIssueRecours(decisionId, Guid.CreateVersion7(), IssueRecours.Confirmee, new DateOnly(2026, 3, 20), null, null), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new DeciderPropositionFrequenceHandler(_monde.AccesExamens, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new DeciderPropositionFrequence(examenId, Guid.CreateVersion7(), true), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ChangerGestionnaireHandler(_monde.Garde, _monde.Store).HandleAsync(new ChangerGestionnaire(dossierId, "infirmier-1"), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new DemanderTransfertHandler(_monde.Garde, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new DemanderTransfert(dossierId, "0123456789", MotifTransfert.ChangementEmployeur), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _monde.Signature.Demandes.ShouldBeEmpty();

        // Un CPMT qui n'est pas le gestionnaire (cpmt-1) : un motif lui permet de clôturer, pas de signer (ADR 0017).
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr gestionnaire");
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).IsSuccess.ShouldBeTrue();
        var complete = (await new ObtenirDecisionCompleteHandler(_monde.AccesDecisions).HandleAsync(new ObtenirDecisionComplete(decisionId), _ct)).Value;
        complete.ShouldSatisfyAllConditions(
            d => d.AuteurId.ShouldBeNull(), d => d.RedigeePar.ShouldBe("infirmier-1"), d => d.ModifieePar.ShouldBe("infirmier-1"),
            d => d.ModifieeLe.ShouldBe(_monde.Horloge.GetUtcNow()), d => d.Version.ShouldBe(2));
        (await Signer(decisionId, complete.Version)).Error!.Code.ShouldBe("decision.signature-hors-relation");

        // Par bris de glace motivé (tracé et signalé), il signe la version relue et devient l'auteur légal.
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr gestionnaire", brisDeGlace: true);
        (await Signer(decisionId, 1)).Error!.Code.ShouldBe("decision.version-obsolete");
        var signee = await Signer(decisionId, complete.Version);
        signee.Value.Statut.ShouldBe(StatutDecision.Emise);
        signee.Value.Mesures.ShouldBe(["PAS_TRAVAIL_NUIT"]);
        _monde.Store.Decisions.Single(d => d.Id == decisionId).AuteurId.ShouldBe("cpmt-2");
        _monde.Audit.Traces.ShouldContain(t => t.Action == ActionAudit.Modification && t.Motif == "Remplacement du Dr gestionnaire" && t.BrisDeGlace);

        // L'infirmier enregistre l'introduction d'une concertation (#401).
        _monde.Agir("infirmier-1", Roles.Infirmier);
        (await new IntroduireRecoursHandler(_monde.AccesDecisions, _monde.Parametres, _monde.Store)
            .HandleAsync(new IntroduireRecours(decisionId, TypeRecours.Concertation, new DateOnly(2026, 3, 3)), _ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Sans_cpmt_gestionnaire_l_infirmier_redige_quand_meme_et_n_en_devient_pas_gestionnaire()
    {
        var affilie = Guid.CreateVersion7();
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 10), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId], "infirmier-1");
        _monde.Store.RendezVous.Add(rdv);

        // L'infirmier qui ouvre le dossier n'en devient pas gestionnaire et ne peut en désigner un (dossier-sante:gerer).
        _monde.Agir("infirmier-1", Roles.Infirmier);
        (await new OuvrirDossierHandler(_monde.Store, _monde.Garde, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirDossier(_personne, "cpmt-1"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Store.Dossiers.Single(d => d.Id == dossierId).GestionnaireCpmtId.ShouldBeNull();
        var examenId = (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).Value;

        // ADR 0017 : plus de refus « decision.cpmt-requis », l'auteur légal sera le CPMT qui signera.
        (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task L_infirmier_ne_modifie_le_brouillon_d_un_cpmt_que_sur_l_examen_de_la_decision()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;

        // Sur simple motif : refus.
        _monde.Agir("infirmier-2", Roles.Infirmier, motif: "Aide au Dr pour la décision");
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).Error!.Code.ShouldBe("decision.relation-de-soin-requise");

        // Contournement : un examen spontané ouvert sur motif fait de l'infirmier un professionnel du dossier, mais pas de
        // l'examen de la décision : refus (ADR 0017).
        (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, TypesExamen.ConsultationSpontanee, Guid.CreateVersion7(), null, null), _ct)).IsSuccess.ShouldBeTrue();
        _monde.Agir("infirmier-2", Roles.Infirmier);
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).Error!.Code.ShouldBe("decision.relation-de-soin-requise");
        _monde.Store.Decisions.Single().VersionContenu.ShouldBe(1);
    }

    [Fact]
    public async Task Un_cpmt_ne_signe_pas_apres_avoir_ouvert_un_examen_spontane_sur_motif()
    {
        // cpmt-1 gestionnaire : examen, brouillon et clôture.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).IsSuccess.ShouldBeTrue();

        // Contournement (#495) : cpmt-2, hors relation de soin, ouvre sur motif un examen spontané. Il devient professionnel
        // d'un examen du dossier, mais pas de l'examen de la décision : la signature reste refusée, avec ou sans motif.
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr gestionnaire");
        (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, TypesExamen.ConsultationSpontanee, Guid.CreateVersion7(), null, null), _ct)).IsSuccess.ShouldBeTrue();
        (await Signer(decisionId, 1)).Error!.Code.ShouldBe("decision.signature-hors-relation");
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await Signer(decisionId, 1)).Error!.Code.ShouldBe("decision.signature-hors-relation");
        _monde.Signature.Demandes.ShouldBeEmpty();
        _monde.Store.Decisions.Single(d => d.Id == decisionId).AuteurId.ShouldBeNull();

        // Seul le bris de glace motivé (tracé et signalé) lui permet de signer.
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr gestionnaire", brisDeGlace: true);
        (await Signer(decisionId, 1)).Value.Statut.ShouldBe(StatutDecision.Emise);
        _monde.Store.Decisions.Single(d => d.Id == decisionId).AuteurId.ShouldBe("cpmt-2");
    }

    [Fact]
    public async Task Un_cpmt_hors_relation_ne_se_designe_pas_gestionnaire_sur_motif_pour_signer()
    {
        // cpmt-1 gestionnaire : examen, brouillon et clôture.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).IsSuccess.ShouldBeTrue();

        // P1-1 de la revue de #496 : sur simple motif, cpmt-2 ne se désigne pas gestionnaire, même après avoir ouvert un examen
        // spontané sur motif ; il ne peut donc pas signer sans bris de glace.
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr gestionnaire");
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");
        (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, TypesExamen.ConsultationSpontanee, Guid.CreateVersion7(), null, null), _ct)).IsSuccess.ShouldBeTrue();
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");
        _monde.Store.Dossiers.Single(d => d.Id == dossierId).GestionnaireCpmtId.ShouldBe("cpmt-1");
        (await Signer(decisionId, 1)).Error!.Code.ShouldBe("decision.signature-hors-relation");

        // Par bris de glace motivé (tracé et signalé, ARC-41), il se désigne gestionnaire, puis signe en relation de soin.
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Reprise du dossier, Dr gestionnaire indisponible", brisDeGlace: true);
        (await ChangerGestionnaire(dossierId, "cpmt-2")).IsSuccess.ShouldBeTrue();
        _monde.Audit.Traces.ShouldContain(t => t.Action == ActionAudit.Modification && t.Motif == "Reprise du dossier, Dr gestionnaire indisponible" && t.BrisDeGlace);
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await Signer(decisionId, 1)).Value.Statut.ShouldBe(StatutDecision.Emise);
    }

    [Fact]
    public async Task Le_gestionnaire_actuel_ou_un_cpmt_en_relation_de_soin_designe_le_gestionnaire_sans_bris_de_glace()
    {
        // Remplacement légitime : le gestionnaire actuel (cpmt-1) désigne son remplaçant, qui signe ensuite sans motif.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        (await ChangerGestionnaire(dossierId, "cpmt-2")).IsSuccess.ShouldBeTrue();
        _monde.Store.Dossiers.Single(d => d.Id == dossierId).GestionnaireCpmtId.ShouldBe("cpmt-2");

        // Un CPMT qui a examiné la personne sur un rendez-vous planifié (relation établie, sans motif) reprend le dossier.
        var affilie = Guid.CreateVersion7();
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 10), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId], "cpmt-3");
        _monde.Store.RendezVous.Add(rdv);
        _monde.Agir("cpmt-3", Roles.Cpmt);
        (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).IsSuccess.ShouldBeTrue();
        (await ChangerGestionnaire(dossierId, "cpmt-3")).IsSuccess.ShouldBeTrue();
        _monde.Audit.Traces.ShouldNotContain(t => t.BrisDeGlace);
    }

    [Fact]
    public async Task Un_cpmt_ne_modifie_le_brouillon_d_un_autre_qu_en_relation_sur_l_examen_ou_par_bris_de_glace()
    {
        // P2-1 de la revue de #496 : même règle pour la modification que pour la signature, CPMT compris.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;

        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Avis du Dr remplaçant");
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).Error!.Code.ShouldBe("decision.relation-de-soin-requise");
        _monde.Store.Decisions.Single().VersionContenu.ShouldBe(1);

        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Avis du Dr remplaçant", brisDeGlace: true);
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).IsSuccess.ShouldBeTrue();

        // L'infirmier hors relation passe lui aussi par le bris de glace.
        _monde.Agir("infirmier-2", Roles.Infirmier, motif: "Complément demandé par le Dr", brisDeGlace: true);
        (await Modifier(decisionId, 2, ["PAS_TRAVAIL_NUIT", "PROTECTION_AUDITIVE"])).IsSuccess.ShouldBeTrue();
        _monde.Store.Decisions.Single().ModifieePar.ShouldBe("infirmier-2");
    }

    [Fact]
    public async Task Un_rendez_vous_deja_honore_ne_permet_ni_de_se_designer_gestionnaire_ni_de_signer()
    {
        // P1-1 bis de la revue de #496 : cpmt-1 (gestionnaire) honore le rendez-vous et rédige la décision.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var affilie = Guid.CreateVersion7();
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 10), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId], "cpmt-1");
        _monde.Store.RendezVous.Add(rdv);
        var examenId = (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Value;
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).IsSuccess.ShouldBeTrue();

        // cpmt-2 rouvre ce rendez-vous sans motif : refus (un rendez-vous ne porte qu'un examen), puis ni désignation ni
        // signature sans bris de glace.
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Error!.Code.ShouldBe("examen.rendez-vous-deja-honore");
        _monde.Store.Examens.Count.ShouldBe(1);
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Reprise du suivi");
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");
        (await Signer(decisionId, 1)).Error!.Code.ShouldBe("decision.signature-hors-relation");
    }

    [Fact]
    public async Task Un_examen_sur_un_rendez_vous_qui_en_porte_plusieurs_n_ouvre_pas_la_designation_du_gestionnaire()
    {
        // Défense en profondeur (#496) : même si un doublon existait (données antérieures à l'index unique), l'examen d'un
        // rendez-vous qui en porte plusieurs n'établit pas la relation de soin qui permet de se désigner gestionnaire.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rendezVous = Guid.CreateVersion7();
        _monde.Store.RendezVous.Add(new RendezVousPrevu(rendezVous, _personne, Guid.CreateVersion7(), new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [], "cpmt-2"));
        _monde.Store.Examens.Add(Examen.Ouvrir(dossierId, _personne, Guid.CreateVersion7(), TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 2), "cpmt-1", rendezVous, []));
        _monde.Store.Examens.Add(Examen.Ouvrir(dossierId, _personne, Guid.CreateVersion7(), TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 2), "cpmt-2", rendezVous, []));

        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Reprise du suivi");
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");

        // Le doublon de cpmt-1 annulé, l'examen de cpmt-2 redevient l'unique examen du rendez-vous.
        _monde.Store.Examens.First().Annuler();
        (await ChangerGestionnaire(dossierId, "cpmt-2")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_cpmt_non_convoque_n_ouvre_un_rendez_vous_futur_que_sur_motif_et_ne_se_designe_pas_gestionnaire()
    {
        // #503 : cpmt-1 (gestionnaire) suit le dossier ; un rendez-vous futur est pris chez cpmt-conv. cpmt-2, hors relation,
        // connaît l'identifiant du rendez-vous : sans motif, il ne l'ouvre pas.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var decisionId = await DecisionDuGestionnaireAsync(dossierId);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), "cpmt-conv");

        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
        _monde.Store.Examens.Count.ShouldBe(1);

        // Sur motif, il l'ouvre (remplacement du confrère), et la trace porte le motif ; il n'est pas pour autant en relation
        // établie : ni désignation du gestionnaire ni signature sans bris de glace.
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr convoqué");
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).IsSuccess.ShouldBeTrue();
        _monde.Audit.Traces.ShouldContain(t => t.Action == ActionAudit.Creation && t.ObjetType == "dossier-sante.examen" && t.Motif == "Remplacement du Dr convoqué");
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");
        (await Signer(decisionId, 1)).Error!.Code.ShouldBe("decision.signature-hors-relation");
        _monde.Store.Dossiers.Single(d => d.Id == dossierId).GestionnaireCpmtId.ShouldBe("cpmt-1");
    }

    [Fact]
    public async Task Le_professionnel_convoque_ouvre_sans_motif_mais_un_professionnel_deja_en_relation_non_convoque_motive()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), "infirmier-1");

        // Le gestionnaire (cpmt-1) est en relation de soin avec la personne, mais le rendez-vous est celui de l'infirmier.
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");

        _monde.Agir("infirmier-1", Roles.Infirmier);
        var examenId = (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Value;
        _monde.Store.Examens.Single(e => e.Id == examenId).ShouldSatisfyAllConditions(
            e => e.ProfessionnelId.ShouldBe("infirmier-1"), e => e.RendezVousId.ShouldBe(rdv.RendezVousId));
        _monde.Audit.Traces.Last().ShouldSatisfyAllConditions(t => t.Motif.ShouldBeNull(), t => t.BrisDeGlace.ShouldBeFalse());
    }

    [Fact]
    public async Task Un_rendez_vous_projete_sans_professionnel_ne_convoque_personne()
    {
        // #503, tolérance documentée : un rendez-vous projeté avant l'ajout du professionnel (ou publié par un producteur
        // antérieur) n'établit la relation de personne. Motif ou bris de glace, et pas de désignation du gestionnaire.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), null);

        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Consultation du jour, convocation antérieure");
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).IsSuccess.ShouldBeTrue();
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await ChangerGestionnaire(dossierId, "cpmt-2")).Error!.Code.ShouldBe("dossier-sante.gestion-hors-relation");
    }

    [Fact]
    public async Task Le_convoque_du_rendez_vous_replanifie_est_le_remplacant()
    {
        // PLA-07 : le rendez-vous de cpmt-absent est reporté chez cpmt-remplacant. Seul ce dernier ouvre sans motif.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), "cpmt-absent");
        await new RendezVousReplanifieHandler(_monde.Store, _monde.Store).HandleAsync(
            new RendezVousReplanifie(rdv.RendezVousId, _personne, rdv.AffilieId, rdv.Debut, rdv.Debut.AddDays(1), "AbsenceRessource", "cpmt-remplacant")
            {
                OccurredAt = _monde.Horloge.GetUtcNow(),
            }, _ct);

        _monde.Agir("cpmt-absent", Roles.Cpmt);
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
        _monde.Agir("cpmt-remplacant", Roles.Cpmt);
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).IsSuccess.ShouldBeTrue();
        (await ChangerGestionnaire(dossierId, "cpmt-remplacant")).IsSuccess.ShouldBeTrue();
        _monde.Store.Examens.Single().Date.ShouldBe(new DateOnly(2026, 3, 21));
    }

    [Fact]
    public async Task Une_ouverture_concurrente_refusee_par_la_base_est_un_conflit_et_non_une_erreur()
    {
        // #503 (P3) : deux ouvertures simultanées passent toutes deux le contrôle applicatif ; l'index unique refuse la seconde.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), "cpmt-1");
        _monde.Store.EchecAuProchainEnregistrement = new RendezVousDejaHonoreException();

        var resultat = await OuvrirSurRendezVous(dossierId, rdv.RendezVousId);

        resultat.Error!.ShouldSatisfyAllConditions(e => e.Code.ShouldBe("examen.rendez-vous-deja-honore"), e => e.Kind.ShouldBe(ErrorKind.Conflict));
    }

    [Fact]
    public async Task Le_convoque_annule_l_examen_ouvert_sur_motif_par_un_tiers_puis_ouvre_le_rendez_vous()
    {
        // #523 : cpmt-2 ouvre sur motif le rendez-vous de cpmt-conv, qui reçoit alors un conflit.
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), "cpmt-conv");
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du Dr convoqué");
        var examenTiers = (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Value;
        _monde.Agir("cpmt-conv", Roles.Cpmt);
        (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Error!.Code.ShouldBe("examen.rendez-vous-deja-honore");

        // Le convoqué, sans motif d'accès, annule l'examen du tiers (motif d'annulation tracé), puis ouvre son rendez-vous.
        _monde.Audit.Traces.Clear();
        (await Annuler(examenTiers, "  Ouvert par erreur sur le rendez-vous d'un confrère  ")).IsSuccess.ShouldBeTrue();
        _monde.Store.Examens.Single(e => e.Id == examenTiers).Statut.ShouldBe(StatutExamen.Annule);
        _monde.Audit.Traces.ShouldBe(
        [
            new Trace(ActionAudit.Modification, "dossier-sante.examen", dossierId, null, false, false),
            new Trace(ActionAudit.Modification, "dossier-sante.examen-annulation", dossierId, "Ouvert par erreur sur le rendez-vous d'un confrère", false, false),
        ]);
        var examenConvoque = (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Value;
        _monde.Store.Examens.Single(e => e.Id == examenConvoque).ProfessionnelId.ShouldBe("cpmt-conv");

        // L'examen annulé n'établit plus la relation de soin de cpmt-2 : il lui faut de nouveau un motif.
        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
    }

    [Fact]
    public async Task Un_tiers_n_annule_un_examen_que_par_bris_de_glace_jamais_sur_simple_motif()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var rdv = RendezVous(new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero), "cpmt-conv");
        _monde.Agir("cpmt-conv", Roles.Cpmt);
        var examenId = (await OuvrirSurRendezVous(dossierId, rdv.RendezVousId)).Value;

        _monde.Agir("cpmt-2", Roles.Cpmt);
        (await Annuler(examenId, "Doublon")).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Doublon constaté");
        (await Annuler(examenId, "Doublon")).Error!.ShouldSatisfyAllConditions(
            e => e.Code.ShouldBe("examen.annulation-hors-relation"), e => e.Kind.ShouldBe(ErrorKind.Forbidden));
        _monde.Store.Examens.Single().Statut.ShouldBe(StatutExamen.EnCours);

        // Le bris de glace motivé est tracé et signalé (ARC-41).
        _monde.Audit.Traces.Clear();
        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Dr convoqué indisponible, rendez-vous repris", brisDeGlace: true);
        (await Annuler(examenId, "Doublon")).IsSuccess.ShouldBeTrue();
        _monde.Store.Examens.Single().Statut.ShouldBe(StatutExamen.Annule);
        _monde.Audit.Traces.ShouldContain(t => t.ObjetType == "dossier-sante.examen" && t.BrisDeGlace && t.Motif == "Dr convoqué indisponible, rendez-vous repris");
        _monde.Audit.Traces.ShouldContain(t => t.ObjetType == "dossier-sante.examen-annulation" && t.Motif == "Doublon");
    }

    [Fact]
    public async Task Le_gestionnaire_annule_l_examen_d_un_autre_professionnel()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Agir("infirmier-1", Roles.Infirmier, motif: "Examen délégué par le Dr gestionnaire");
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);

        _monde.Agir("cpmt-1", Roles.Cpmt);
        (await Annuler(examenId, "Travailleur absent")).IsSuccess.ShouldBeTrue();
        _monde.Audit.Traces.ShouldNotContain(t => t.BrisDeGlace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task L_annulation_exige_un_motif(string? motif)
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);

        (await Annuler(examenId, motif)).Error!.Code.ShouldBe("examen.motif-annulation-obligatoire");
        (await Annuler(examenId, new string('x', MotifAcces.LongueurMaximale + 1))).Error!.Code.ShouldBe("examen.motif-annulation-obligatoire");
        _monde.Store.Examens.Single().Statut.ShouldBe(StatutExamen.EnCours);
    }

    [Fact]
    public async Task Un_examen_cloture_ou_deja_annule_ne_s_annule_pas()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        await DecisionDuGestionnaireAsync(dossierId);
        var cloture = _monde.Store.Examens.Single().Id;
        (await Annuler(cloture, "Erreur")).Error!.ShouldSatisfyAllConditions(e => e.Code.ShouldBe("examen.non-annulable"), e => e.Kind.ShouldBe(ErrorKind.Conflict));
        _monde.Store.Examens.Single().Statut.ShouldBe(StatutExamen.Cloture);

        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        (await Annuler(examenId, "Erreur")).IsSuccess.ShouldBeTrue();
        (await Annuler(examenId, "Erreur")).Error!.Code.ShouldBe("examen.non-annulable");
    }

    [Theory]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.AssistantMedical)]
    public async Task Un_profil_non_medical_n_annule_pas_un_examen(string role)
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);

        _monde.Agir("autre", role, motif: "Erreur", brisDeGlace: true);
        (await Annuler(examenId, "Erreur")).Error!.Code.ShouldBe("dossier-sante.interdit");
    }

    private Task<Result<Unit>> Annuler(Guid examenId, string? motif) =>
        new AnnulerExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Contexte, _monde.Audit, _monde.Store)
            .HandleAsync(new AnnulerExamen(examenId, motif), _ct);

    private RendezVousPrevu RendezVous(DateTimeOffset debut, string? professionnelId)
    {
        var affilie = Guid.CreateVersion7();
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 10), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, debut, [obligation.ObligationId], professionnelId, _monde.Horloge.GetUtcNow().AddDays(-7));
        _monde.Store.RendezVous.Add(rdv);
        return rdv;
    }

    /// <summary>cpmt-1, gestionnaire, examine la personne sans rendez-vous et rédige une décision, puis clôture l'examen.</summary>
    private async Task<Guid> DecisionDuGestionnaireAsync(Guid dossierId)
    {
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).IsSuccess.ShouldBeTrue();
        return decisionId;
    }

    private Task<Result<Guid>> OuvrirSurRendezVous(Guid dossierId, Guid rendezVousId) =>
        new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rendezVousId, null), _ct);

    private Task<Result<Unit>> ChangerGestionnaire(Guid dossierId, string cpmtId) =>
        new ChangerGestionnaireHandler(_monde.Garde, _monde.Store).HandleAsync(new ChangerGestionnaire(dossierId, cpmtId), _ct);

    [Fact]
    public async Task Le_cpmt_gestionnaire_signe_la_decision_d_un_examen_realise_par_un_autre()
    {
        // Relation de soin sur l'examen de la décision : le CPMT gestionnaire du dossier signe sans motif (ADR 0017).
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Agir("infirmier-1", Roles.Infirmier, motif: "Examen périodique délégué par le Dr gestionnaire");
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;

        _monde.Agir("cpmt-1", Roles.Cpmt);
        _monde.Store.Examens.Single().ProfessionnelId.ShouldBe("infirmier-1");
        (await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct)).IsSuccess.ShouldBeTrue();
        (await Signer(decisionId, 1)).Value.Statut.ShouldBe(StatutDecision.Emise);
    }

    [Fact]
    public async Task L_infirmier_professionnel_de_l_examen_modifie_le_brouillon_redige_par_le_cpmt()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var affilie = Guid.CreateVersion7();
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, affilie, TypesExamen.EvaluationPeriodique, new DateOnly(2026, 3, 10), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId], "infirmier-2");
        _monde.Store.RendezVous.Add(rdv);

        // L'infirmier réalise l'examen ; le CPMT gestionnaire rédige la décision de cet examen ; l'infirmier la complète.
        _monde.Agir("infirmier-2", Roles.Infirmier);
        var examenId = (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).Value;
        _monde.Agir("cpmt-1", Roles.Cpmt);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Value;

        _monde.Agir("infirmier-2", Roles.Infirmier);
        (await Modifier(decisionId, 1, ["PAS_TRAVAIL_NUIT"])).IsSuccess.ShouldBeTrue();
    }

    private Task<Result<Unit>> Modifier(Guid decisionId, int? version, string[] mesures) =>
        new ModifierDecisionHandler(_monde.AccesDecisions, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new ModifierDecision(decisionId, new ContenuDecisionSaisi(CategorieDecision.ApteAvecMesures, mesures, null, null, null), version), _ct);

    private Task<Result<DecisionResumeDto>> Signer(Guid decisionId, int? version) =>
        new SignerDecisionHandler(_monde.AccesDecisions, _monde.Store, _monde.Signature, _monde.Utilisateur, _monde.Parametres, _monde.Store, _monde.Store)
            .HandleAsync(new SignerDecision(decisionId, version), _ct);
}

public sealed class DecisionsEtExamensTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Monde _monde = new();
    private readonly Guid _personne = Guid.CreateVersion7();
    private readonly Guid _affilie = Guid.CreateVersion7();

    /// <summary>Examen de reprise ouvert sur rendez-vous, couvrant une obligation reprise le 2 février et limitée au 16 février.</summary>
    private async Task<(Guid ExamenId, ObligationDue Obligation)> OuvrirRepriseAsync(DateOnly dateRendezVous)
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, _affilie, TypesExamen.ExamenReprise, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 16));
        _monde.Store.Obligations.Add(obligation);
        var debut = new DateTimeOffset(dateRendezVous.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, _affilie, debut, [obligation.ObligationId], "cpmt-1");
        _monde.Store.RendezVous.Add(rdv);
        var examenId = (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).Value;
        return (examenId, obligation);
    }

    private Task<Result<Unit>> CloturerAsync(Guid examenId) =>
        new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge).HandleAsync(new CloturerExamen(examenId, null), _ct);

    [Fact]
    public async Task La_cloture_satisfait_les_obligations_et_signale_une_reprise_hors_delai()
    {
        var (examenId, obligation) = await OuvrirRepriseAsync(new DateOnly(2026, 3, 2));

        (await CloturerAsync(examenId)).IsSuccess.ShouldBeTrue();

        obligation.SatisfaiteParExamenId.ShouldBe(examenId);
        _monde.Store.Examens.Single().HorsDelaiLegal.ShouldBe(true);
        var cloture = _monde.Store.Published.OfType<ExamenCloture>().Single();
        cloture.ShouldBe(new ExamenCloture(examenId, _personne, _affilie, TypesExamen.ExamenReprise, new DateOnly(2026, 3, 2)) { EventId = cloture.EventId, OccurredAt = cloture.OccurredAt });
    }

    [Theory]
    [InlineData("2026-02-16", false)]
    [InlineData("2026-02-17", true)]
    [InlineData("2026-02-02", false)]
    public async Task Le_hors_delai_d_une_reprise_se_lit_dans_la_projection_a_la_frontiere_exacte_de_la_date_limite(string dateExamen, bool horsDelai)
    {
        // La date limite (16 février) vient d'Obligations : aucun recalcul par le calendrier de ce service, même si un
        // paramètre local de délai différent était présent.
        var (examenId, _) = await OuvrirRepriseAsync(DateOnly.Parse(dateExamen, System.Globalization.CultureInfo.InvariantCulture));

        (await CloturerAsync(examenId)).IsSuccess.ShouldBeTrue();

        _monde.Store.Examens.Single().HorsDelaiLegal.ShouldBe(horsDelai);
    }

    [Fact]
    public async Task Sans_date_limite_recue_le_hors_delai_reste_indetermine()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var obligation = new ObligationDue(Guid.CreateVersion7(), _personne, _affilie, TypesExamen.ExamenReprise, new DateOnly(2026, 2, 2), null);
        _monde.Store.Obligations.Add(obligation);
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, _affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId], "cpmt-1");
        _monde.Store.RendezVous.Add(rdv);
        var examenId = (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).Value;

        (await CloturerAsync(examenId)).IsSuccess.ShouldBeTrue();

        _monde.Store.Examens.Single().HorsDelaiLegal.ShouldBeNull();
    }

    [Fact]
    public async Task La_decision_signee_ne_publie_que_categorie_mesures_et_validite()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, _affilie, _ct);
        (await new SaisirObservationHandler(_monde.AccesExamens, _monde.Store, _monde.Horloge)
            .HandleAsync(new SaisirObservation(examenId, "Lombalgie chronique depuis 2019", "Raideur lombaire"), _ct)).IsSuccess.ShouldBeTrue();

        var contenu = new ContenuDecisionSaisi(CategorieDecision.ApteAvecMesures, ["PAS_PORT_CHARGES_LOURDES"], new DateOnly(2027, 3, 1), "Hernie discale L5-S1", "Kinésithérapie");
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, contenu), _ct)).Value;

        var signer = new SignerDecisionHandler(_monde.AccesDecisions, _monde.Store, _monde.Signature, _monde.Utilisateur, _monde.Parametres, _monde.Store, _monde.Store);
        (await signer.HandleAsync(new SignerDecision(decisionId, 1), _ct)).Error!.Code.ShouldBe("decision.examen-non-cloture");

        await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct);

        var signee = await new SignerDecisionHandler(_monde.AccesDecisions, _monde.Store, _monde.Signature, _monde.Utilisateur, _monde.Parametres, _monde.Store, _monde.Store)
            .HandleAsync(new SignerDecision(decisionId, 1), _ct);
        signee.Value.Statut.ShouldBe(StatutDecision.Emise);
        _monde.Signature.Demandes.Single().EmpreinteFormulaire.Length.ShouldBe(64);

        var emise = _monde.Store.Published.OfType<DecisionEmise>().Single();
        emise.Categorie.ShouldBe(CategoriesDecision.ApteAvecMesures);
        emise.CodesMesures.ShouldBe(["PAS_PORT_CHARGES_LOURDES"]);
        emise.ValideJusquAu.ShouldBe(new DateOnly(2027, 3, 1));
        emise.ExamenId.ShouldBe(examenId);
        emise.DecisionId.ShouldBe(decisionId);

        var evenements = string.Join("\n", _monde.Store.Published.Select(e => JsonSerializer.Serialize(e, e.GetType(), JsonSerializerOptions.Web)));
        foreach (var clinique in new[] { "Hernie", "Kinésithérapie", "Lombalgie", "Raideur" })
        {
            evenements.ShouldNotContain(clinique);
        }
    }

    [Fact]
    public async Task L_employeur_ne_lit_que_le_resume_des_decisions_emises_de_son_affilie()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, _affilie, _ct);
        var decisionId = (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.InaptitudeDefinitive, null, null, "Motif secret", null)), _ct)).Value;

        _monde.Agir("employeur-1", Roles.Employeur, affilie: _affilie);
        var lire = () => new ObtenirDecisionHandler(_monde.AccesDecisions).HandleAsync(new ObtenirDecision(decisionId), _ct);
        (await lire()).Error!.Kind.ShouldBe(ErrorKind.NotFound);

        _monde.Agir("cpmt-1", Roles.Cpmt);
        await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge).HandleAsync(new CloturerExamen(examenId, null), _ct);
        await new SignerDecisionHandler(_monde.AccesDecisions, _monde.Store, _monde.Signature, _monde.Utilisateur, _monde.Parametres, _monde.Store, _monde.Store)
            .HandleAsync(new SignerDecision(decisionId, 1), _ct);

        _monde.Agir("employeur-1", Roles.Employeur, affilie: _affilie);
        (await lire()).Value.Categorie.ShouldBe(CategoriesDecision.InaptitudeDefinitive);
        var formulaire = (await new ObtenirFormulaireHandler(_monde.AccesDecisions, _monde.Parametres)
            .HandleAsync(new ObtenirFormulaire(decisionId, ExemplaireFormulaire.Employeur), _ct)).Value;
        formulaire.ShouldSatisfyAllConditions(f => f.Justification.ShouldBeNull(), f => f.Recommandations.ShouldBeNull(), f => f.VoiesDeRecours.Count.ShouldBe(2));
        (await new ObtenirFormulaireHandler(_monde.AccesDecisions, _monde.Parametres)
            .HandleAsync(new ObtenirFormulaire(decisionId, ExemplaireFormulaire.Dossier), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        _monde.Agir("employeur-2", Roles.Employeur, affilie: Guid.CreateVersion7());
        (await lire()).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Une_vaccination_consomme_le_lot_et_publie_vaccination_administree()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var lot = LotVaccin.Receptionner(Guid.CreateVersion7(), "GRIPPE", "L-01", new DateOnly(2026, 12, 31), 2, new DateOnly(2026, 3, 1));
        _monde.Store.Lots.Add(lot);

        var handler = new AdministrerVaccinHandler(_monde.Garde, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Store, _monde.Horloge);
        (await handler.HandleAsync(new AdministrerVaccin(dossierId, "grippe", 1, null, lot.Id, "Aucune réaction"), _ct)).IsSuccess.ShouldBeTrue();

        lot.QuantiteRestante.ShouldBe(1);
        var publie = _monde.Store.Published.OfType<VaccinationAdministree>().Single();
        publie.ShouldSatisfyAllConditions(p => p.CodeVaccin.ShouldBe("GRIPPE"), p => p.Dose.ShouldBe(1), p => p.PersonneId.ShouldBe(_personne));
        (await handler.HandleAsync(new AdministrerVaccin(dossierId, "TETANOS", 1, null, lot.Id, null), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }
}

public sealed class PurgeEtProjectionsTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Monde _monde = new();

    [Fact]
    public async Task La_destruction_n_a_lieu_qu_apres_validation_humaine_et_laisse_une_preuve()
    {
        var dossierId = await _monde.OuvrirDossierAsync(Guid.CreateVersion7(), _ct);
        var archiver = new ArchiverDossierHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Parametres, _monde.Store, _monde.Horloge);
        var archive = (await archiver.HandleAsync(new ArchiverDossier(dossierId), _ct)).Value;
        archive.DatePurgePrevue.ShouldBe(new DateOnly(2041, 3, 2));

        var proposer = () => new ProposerPurges(new DateOnly(2041, 3, 2));
        (await new ProposerPurgesHandler(_monde.Store, _monde.Utilisateur, _monde.Garde, _monde.Store, _monde.Horloge).HandleAsync(proposer(), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        _monde.Agir("dirigeant-1", Roles.CpmtDirigeant);
        (await new ProposerPurgesHandler(_monde.Store, _monde.Utilisateur, _monde.Garde, _monde.Store, _monde.Horloge)
            .HandleAsync(new ProposerPurges(new DateOnly(2041, 3, 1)), _ct)).Value.ShouldBeEmpty();
        (await new ProposerPurgesHandler(_monde.Store, _monde.Utilisateur, _monde.Garde, _monde.Store, _monde.Horloge).HandleAsync(proposer(), _ct))
            .Value.Single().Id.ShouldBe(dossierId);
        _monde.Store.Dossiers.ShouldHaveSingleItem();

        var valider = new ValiderPurgeHandler(_monde.Store, _monde.Store, _monde.Export, _monde.Utilisateur, _monde.Audit, _monde.Horloge);
        (await valider.HandleAsync(new ValiderPurge(dossierId, " "), _ct)).Error!.Code.ShouldBe("purge.motif-obligatoire");
        _monde.Store.Dossiers.ShouldHaveSingleItem();

        var preuve = (await valider.HandleAsync(new ValiderPurge(dossierId, "Conservation légale échue, aucune procédure en cours"), _ct)).Value;
        preuve.ShouldSatisfyAllConditions(p => p.ValideePar.ShouldBe("dirigeant-1"), p => p.Empreinte.Length.ShouldBe(64), p => p.NombreElements.ShouldBe(1));
        _monde.Store.Dossiers.ShouldBeEmpty();
        _monde.Audit.Traces.Last().ShouldSatisfyAllConditions(t => t.Action.ShouldBe(ActionAudit.Suppression), t => t.ObjetId.ShouldBe(dossierId));
    }

    [Fact]
    public async Task Un_mesurage_est_verse_une_seule_fois_aux_dossiers_rattaches()
    {
        var dossierId = await _monde.OuvrirDossierAsync(Guid.CreateVersion7(), _ct);
        var groupe = Guid.CreateVersion7();
        var affilie = Guid.CreateVersion7();
        (await new RattacherGroupeExpositionHandler(_monde.Garde, _monde.Store, _monde.Store)
            .HandleAsync(new RattacherGroupeExposition(dossierId, groupe, affilie, new DateOnly(2026, 1, 1), null), _ct)).IsSuccess.ShouldBeTrue();

        var mesurage = new MesurageEnregistre(Guid.CreateVersion7(), groupe, affilie, "BRUIT", "ELEVE", new DateOnly(2026, 2, 1));
        var handler = new MesurageEnregistreHandler(_monde.Store, _monde.Store, _monde.Store);
        await handler.HandleAsync(mesurage, _ct);
        await handler.HandleAsync(mesurage, _ct);
        await handler.HandleAsync(mesurage with { MesurageId = Guid.CreateVersion7(), Date = new DateOnly(2025, 6, 1) }, _ct);

        _monde.Store.Dossiers.Single().Expositions.Single().MesurageId.ShouldBe(mesurage.MesurageId);
        _monde.Store.Mesurages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Les_projections_d_obligations_et_d_affectations_sont_idempotentes()
    {
        var personne = Guid.CreateVersion7();
        var obligation = new ObligationCreee(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), TypesExamen.EvaluationPeriodique, new DateOnly(2026, 4, 1), null);
        var handler = new ObligationCreeeHandler(_monde.Store, _monde.Store);
        await handler.HandleAsync(obligation, _ct);
        await handler.HandleAsync(obligation with { DateDue = new DateOnly(2026, 5, 1) }, _ct);
        _monde.Store.Obligations.Single().DateDue.ShouldBe(new DateOnly(2026, 5, 1));

        var recente = new Contracts.Personnes.AffectationModifiee(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), new DateOnly(2026, 1, 1), null)
        { OccurredAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) };
        var affectations = new AffectationModifieeHandler(_monde.Store, _monde.Store);
        await affectations.HandleAsync(recente, _ct);
        await affectations.HandleAsync(recente with { DateFin = new DateOnly(2026, 1, 15), OccurredAt = recente.OccurredAt.AddDays(-1) }, _ct);
        _monde.Store.Affectations.Single().DateFin.ShouldBeNull();
    }

    [Theory]
    [InlineData("Annule")]
    [InlineData("SortiEntreprise")]
    public async Task Une_obligation_cloturee_sans_realisation_est_retiree_des_examens_dus_de_facon_idempotente(string statut)
    {
        var personne = Guid.CreateVersion7();
        var dossierId = await _monde.OuvrirDossierAsync(personne, _ct);
        var creee = new ObligationCreee(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), TypesExamen.ExamenReprise, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 16))
        { OccurredAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero) };
        await new ObligationCreeeHandler(_monde.Store, _monde.Store).HandleAsync(creee, _ct);
        (await ExamensDusAsync(dossierId)).ShouldHaveSingleItem();

        var cloturee = new ObligationCloturee(creee.ObligationId, personne, creee.AffilieId, creee.TypeExamen, statut, null, new DateOnly(2026, 3, 3))
        { OccurredAt = creee.OccurredAt.AddDays(2) };
        var handler = new ObligationClotureeHandler(_monde.Store, _monde.Store);
        await handler.HandleAsync(cloturee, _ct);
        var saves = _monde.Store.Saves;
        await handler.HandleAsync(cloturee, _ct);

        _monde.Store.Saves.ShouldBe(saves);
        _monde.Store.Obligations.Single().ShouldSatisfyAllConditions(o => o.EstRetiree.ShouldBeTrue(), o => o.StatutRetrait.ShouldBe(statut));
        (await ExamensDusAsync(dossierId)).ShouldBeEmpty();

        // Republication de la création, plus ancienne que la clôture (ordre d'arrivée quelconque) : l'examen reste retiré.
        await new ObligationCreeeHandler(_monde.Store, _monde.Store).HandleAsync(creee, _ct);
        (await ExamensDusAsync(dossierId)).ShouldBeEmpty();

        // L'obligation redevient due (nouvelle création après recalcul) : l'examen dû réapparaît.
        await new ObligationCreeeHandler(_monde.Store, _monde.Store).HandleAsync(creee with { OccurredAt = cloturee.OccurredAt.AddDays(1) }, _ct);
        (await ExamensDusAsync(dossierId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Une_cloture_recue_avant_la_creation_empeche_l_examen_du_d_apparaitre_et_une_realisation_est_ignoree()
    {
        var personne = Guid.CreateVersion7();
        var dossierId = await _monde.OuvrirDossierAsync(personne, _ct);
        var creee = new ObligationCreee(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), TypesExamen.ExamenReprise, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 16))
        { OccurredAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero) };
        var handler = new ObligationClotureeHandler(_monde.Store, _monde.Store);

        await handler.HandleAsync(new ObligationCloturee(creee.ObligationId, personne, creee.AffilieId, creee.TypeExamen, "Annule", null, new DateOnly(2026, 3, 3))
        { OccurredAt = creee.OccurredAt.AddHours(1) }, _ct);
        await new ObligationCreeeHandler(_monde.Store, _monde.Store).HandleAsync(creee, _ct);
        _monde.Store.Obligations.Single().ShouldSatisfyAllConditions(o => o.EstRetiree.ShouldBeTrue(), o => o.DateLimite.ShouldBe(new DateOnly(2026, 3, 16)));
        (await ExamensDusAsync(dossierId)).ShouldBeEmpty();

        var autre = creee with { ObligationId = Guid.CreateVersion7() };
        await new ObligationCreeeHandler(_monde.Store, _monde.Store).HandleAsync(autre, _ct);
        await handler.HandleAsync(new ObligationCloturee(autre.ObligationId, personne, autre.AffilieId, autre.TypeExamen, "Realise", null, new DateOnly(2026, 3, 3)), _ct);
        _monde.Store.Obligations.Single(o => o.ObligationId == autre.ObligationId).EstRetiree.ShouldBeFalse();
    }

    [Fact]
    public async Task Le_rendez_vous_projete_suit_la_replanification_la_plus_recente_dans_n_importe_quel_ordre()
    {
        // #503 : professionnel convoqué et horaire suivent l'événement le plus récent ; les obligations viennent de la planification.
        var personne = Guid.CreateVersion7();
        var obligation = Guid.CreateVersion7();
        var t0 = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var planifie = new RendezVousPlanifie(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), t0.AddDays(10), [obligation], "cpmt-a") { OccurredAt = t0 };
        var replanifie = new RendezVousReplanifie(planifie.RendezVousId, personne, planifie.AffilieId, planifie.Debut, t0.AddDays(12), "AbsenceRessource", "cpmt-b")
        { OccurredAt = t0.AddDays(1) };
        var planification = new RendezVousPlanifieHandler(_monde.Store, _monde.Store);
        var replanification = new RendezVousReplanifieHandler(_monde.Store, _monde.Store);

        // Replanification reçue d'abord, puis planification plus ancienne, puis rejeu de la replanification.
        await replanification.HandleAsync(replanifie, _ct);
        await planification.HandleAsync(planifie, _ct);
        var saves = _monde.Store.Saves;
        await replanification.HandleAsync(replanifie with { OccurredAt = t0.AddHours(12) }, _ct);

        _monde.Store.Saves.ShouldBe(saves);
        _monde.Store.RendezVous.Single().ShouldSatisfyAllConditions(
            r => r.ProfessionnelId.ShouldBe("cpmt-b"),
            r => r.Debut.ShouldBe(t0.AddDays(12)),
            r => r.ObligationIds.ShouldBe([obligation]),
            r => r.EstConvoque("cpmt-b").ShouldBeTrue(),
            r => r.EstConvoque("cpmt-a").ShouldBeFalse());

        // Un producteur antérieur ne publie pas le professionnel : personne n'est plus convoqué.
        await replanification.HandleAsync(replanifie with { ProfessionnelId = null, OccurredAt = t0.AddDays(2) }, _ct);
        _monde.Store.RendezVous.Single().ShouldSatisfyAllConditions(r => r.ProfessionnelId.ShouldBeNull(), r => r.EstConvoque(null).ShouldBeFalse());
    }

    [Fact]
    public async Task Les_jours_feries_supplementaires_entrent_dans_les_delais_de_recours()
    {
        // Remise le mardi 3 mars 2026 : 7 jours ouvrables → jeudi 12 mars ; un jour férié supplémentaire le 5 mars → 13 mars.
        var remise = new DateOnly(2026, 3, 3);
        (await _monde.Parametres.PolitiqueRecoursAsync(remise, _ct)).DateLimiteIntroduction(TypeRecours.RecoursMedecinInspecteur, remise).ShouldBe(new DateOnly(2026, 3, 12));

        var handler = new JoursFeriesModifiesHandler(_monde.Store, _monde.Store);
        var modifies = new JoursFeriesModifies(2026, [new DateOnly(2026, 3, 5)]) { OccurredAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) };
        await handler.HandleAsync(modifies, _ct);
        await handler.HandleAsync(new JoursFeriesModifies(2026, []) { OccurredAt = modifies.OccurredAt.AddDays(-1) }, _ct);
        await handler.HandleAsync(new JoursFeriesModifies(2027), _ct);

        _monde.Store.Calendriers.Single().JoursSupplementaires.ShouldBe([new DateOnly(2026, 3, 5)]);
        (await _monde.Parametres.PolitiqueRecoursAsync(remise, _ct)).DateLimiteIntroduction(TypeRecours.RecoursMedecinInspecteur, remise).ShouldBe(new DateOnly(2026, 3, 13));
    }

    [Fact]
    public async Task Les_delais_de_recours_viennent_de_referentiels_sinon_de_leurs_valeurs_initiales()
    {
        var date = new DateOnly(2026, 3, 3);
        var initiaux = await _monde.Parametres.DelaisRecoursAsync(date, _ct);
        initiaux.ShouldBe(new DelaisRecours(
            ParametresLegauxParDefaut.Obtenir(CodesParametresLegaux.ConcertationIntroductionDelai).Valeur,
            ParametresLegauxParDefaut.Obtenir(CodesParametresLegaux.ConcertationIssueDelai).Valeur,
            ParametresLegauxParDefaut.Obtenir(CodesParametresLegaux.RecoursIntroductionDelai).Valeur,
            ParametresLegauxParDefaut.Obtenir(CodesParametresLegaux.RecoursDecisionDelai).Valeur));

        var handler = new ParametreLegalModifieHandler(_monde.Store, _monde.Store);
        await handler.HandleAsync(new ParametreLegalModifie(CodesParametresLegaux.RecoursIntroductionDelai, 10, UnitesParametreLegal.JoursOuvrables, new DateOnly(2026, 1, 1), null), _ct);
        await handler.HandleAsync(new ParametreLegalModifie(CodesParametresLegaux.RecoursDecisionDelai, 40, UnitesParametreLegal.Semaines, new DateOnly(2026, 1, 1), null), _ct);

        var recus = await _monde.Parametres.DelaisRecoursAsync(date, _ct);
        recus.IntroductionRecours.ShouldBe(10);
        recus.DecisionMedecinInspecteur.ShouldBe(initiaux.DecisionMedecinInspecteur); // unité inattendue : valeur initiale conservée
        (await _monde.Parametres.PolitiqueRecoursAsync(date, _ct)).DateLimiteIntroduction(TypeRecours.RecoursMedecinInspecteur, date).ShouldBe(new DateOnly(2026, 3, 17));
    }

    /// <summary>
    /// #293 : toute catégorie émise par le domaine (<c>DecisionEmise.Categorie</c>) est un code partagé de
    /// <see cref="CategoriesDecision"/>, et tout code partagé est effectivement émis (aucun code mort ni divergent).
    /// Le domaine ne référence pas Sepp.Contracts (règles d'architecture) : la conformité est vérifiée ici.
    /// </summary>
    [Fact]
    public void Les_categories_de_decision_emises_par_le_domaine_sont_les_codes_partages()
    {
        var emis = Enum.GetValues<CategorieDecision>().Select(CodesDecision.Code).ToList();

        foreach (var code in emis)
        {
            CategoriesDecision.Connus.ShouldContain(code, $"{code} n'est pas dans Sepp.Contracts.Decisions.CategoriesDecision.");
        }

        emis.Order(StringComparer.Ordinal).ShouldBe(CategoriesDecision.Connus.Order(StringComparer.Ordinal));
    }

    private async Task<IReadOnlyList<ExamenDuDto>> ExamensDusAsync(Guid dossierId) =>
        (await new ObtenirVueConsultationHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Store, new RisquesPersonne(_monde.Store), _monde.Horloge)
            .HandleAsync(new ObtenirVueConsultation(dossierId, null), _ct)).Value.ExamensDus;
}
