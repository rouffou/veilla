using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Prevention;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.SurveillanceMedicale.Application.Conservation;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Decisions;
using Sepp.SurveillanceMedicale.Application.Dossiers;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Application.Projections;
using Sepp.SurveillanceMedicale.Application.Vaccinations;
using Sepp.SurveillanceMedicale.Domain;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Projections;
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

    public GardeDossier Garde => new(Store, Store, Utilisateur, Contexte, Audit, Store);

    public ParametresMedicaux Parametres => new(Store, new OptionsSurveillanceMedicale());

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
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId]);
        _monde.Store.RendezVous.Add(rdv);

        _monde.Agir("infirmier-1", Roles.Infirmier);
        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");

        var examenId = (await new OuvrirExamenHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new OuvrirExamen(dossierId, null, null, rdv.RendezVousId, null), _ct)).Value;
        var examen = _monde.Store.Examens.Single(e => e.Id == examenId);
        examen.ShouldSatisfyAllConditions(e => e.AffilieId.ShouldBe(affilie), e => e.TypeExamen.ShouldBe(TypesExamen.EvaluationPeriodique), e => e.ObligationIds.ShouldBe([obligation.ObligationId]));

        (await new ObtenirDossierHandler(_monde.Garde).HandleAsync(new ObtenirDossier(dossierId), _ct)).IsSuccess.ShouldBeTrue();

        // L'infirmier ne rédige pas de décision (decision:ecrire réservé au CPMT).
        (await new RedigerDecisionHandler(_monde.AccesExamens, _monde.Store, _monde.Utilisateur, _monde.Store)
            .HandleAsync(new RedigerDecision(examenId, new ContenuDecisionSaisi(CategorieDecision.Apte, null, null, null, null)), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
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
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, _affilie, debut, [obligation.ObligationId]);
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
        var rdv = new RendezVousPrevu(Guid.CreateVersion7(), _personne, _affilie, new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId]);
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
        (await signer.HandleAsync(new SignerDecision(decisionId), _ct)).Error!.Code.ShouldBe("decision.examen-non-cloture");

        await new CloturerExamenHandler(_monde.AccesExamens, _monde.Store, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new CloturerExamen(examenId, null), _ct);

        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Relecture");
        (await new SignerDecisionHandler(_monde.AccesDecisions, _monde.Store, _monde.Signature, _monde.Utilisateur, _monde.Parametres, _monde.Store, _monde.Store)
            .HandleAsync(new SignerDecision(decisionId), _ct)).Error!.Code.ShouldBe("decision.signataire");

        _monde.Agir("cpmt-1", Roles.Cpmt);
        var signee = await new SignerDecisionHandler(_monde.AccesDecisions, _monde.Store, _monde.Signature, _monde.Utilisateur, _monde.Parametres, _monde.Store, _monde.Store)
            .HandleAsync(new SignerDecision(decisionId), _ct);
        signee.Value.Statut.ShouldBe(StatutDecision.Emise);
        _monde.Signature.Demandes.Single().EmpreinteFormulaire.Length.ShouldBe(64);

        var emise = _monde.Store.Published.OfType<DecisionEmise>().Single();
        emise.Categorie.ShouldBe("APTE_AVEC_MESURES");
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
            .HandleAsync(new SignerDecision(decisionId), _ct);

        _monde.Agir("employeur-1", Roles.Employeur, affilie: _affilie);
        (await lire()).Value.Categorie.ShouldBe("INAPTITUDE_DEFINITIVE");
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

    private async Task<IReadOnlyList<ExamenDuDto>> ExamensDusAsync(Guid dossierId) =>
        (await new ObtenirVueConsultationHandler(_monde.Garde, _monde.Store, _monde.Store, _monde.Store, _monde.Store, new RisquesPersonne(_monde.Store), _monde.Horloge)
            .HandleAsync(new ObtenirVueConsultation(dossierId, null), _ct)).Value.ExamensDus;
}
