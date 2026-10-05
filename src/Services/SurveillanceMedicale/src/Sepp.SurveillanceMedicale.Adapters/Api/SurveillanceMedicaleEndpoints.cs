using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.SurveillanceMedicale.Adapters.Securite;
using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Application.Conservation;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Decisions;
using Sepp.SurveillanceMedicale.Application.Dossiers;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Application.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Application.Protocoles;
using Sepp.SurveillanceMedicale.Application.Transferts;
using Sepp.SurveillanceMedicale.Application.Vaccinations;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Adapters.Api;

/// <summary>
/// API REST du service Surveillance médicale (zone médicale, contrat OpenAPI ARC-30). Les permissions de la matrice
/// §3.3 sont exigées ici quand une seule suffit ; la relation de soin, le motif, le bris de glace, le périmètre des
/// externes et la journalisation de chaque accès sont appliqués par les cas d'usage (<see cref="GardeDossier"/>).
/// Le motif d'accès voyage dans l'en-tête <c>X-Motif-Acces</c>, jamais dans l'URL ; aucun identifiant autre que les
/// UUID n'apparaît dans les chemins (DAT-06).
/// </summary>
public static class SurveillanceMedicaleEndpoints
{
    public static IEndpointRouteBuilder MapSurveillanceMedicaleEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter(VerifierMotif);
        MapDossiers(api.MapGroup("/dossiers").WithTags("Dossier de santé"));
        MapExamens(api.MapGroup("/examens").WithTags("Examens et consultation"));
        MapDecisions(api.MapGroup("/decisions").WithTags("Décisions et formulaire d'évaluation de santé"));
        MapVaccins(api);
        MapProtocoles(api);
        MapPurges(api.MapGroup("/purges").WithTags("Conservation et purge").RequirePermission(Permissions.DossierSantePurger));
        MapDeclarationsMp(api.MapGroup("/declarations-mp").WithTags("Maladies professionnelles (Fedris)"));
        MapTransferts(api.MapGroup("/transferts").WithTags("Transferts de dossiers"));

        api.MapPost("/questionnaires/pre-remplissage", async (PreRemplirQuestionnaire body, ICommandHandler<PreRemplirQuestionnaire, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/questionnaires/{id}", new { id })))
            .RequirePermission(Permissions.QuestionnaireSanteRemplir)
            .WithTags("Questionnaires de santé")
            .WithName("PreRemplirQuestionnaire")
            .WithSummary("SAN-22 : questionnaire rempli à l'avance (portail ou tablette) ; écriture seule, sans lecture du dossier.");
        return app;
    }

    /// <summary>Motif mal formé (trop long) ou bris de glace sans motif : refus avant tout accès (§3.3).</summary>
    private static async ValueTask<object?> VerifierMotif(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var contexte = new ContexteAccesHttp(new HttpContextAccessor { HttpContext = context.HttpContext });
        return MotifAcces.Verifier(contexte.Motif, contexte.BrisDeGlace) is { } erreur
            ? ResultExtensions.ToProblem(erreur)
            : await next(context);
    }

    private static void MapDossiers(RouteGroupBuilder dossiers)
    {
        dossiers.MapPost("/", async (OuvrirDossier body, ICommandHandler<OuvrirDossier, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/dossiers/{id}", new { id })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("OuvrirDossierSante")
            .WithSummary("SAN-40 : ouverture du dossier unique de la personne (transversal aux employeurs).");

        dossiers.MapGet("/", async (Guid personneId, IQueryHandler<TrouverDossier, DossierResumeDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new TrouverDossier(personneId), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("TrouverDossierSante")
            .WithSummary("Identifiant et statut du dossier d'une personne (sans contenu clinique, journalisé).");

        dossiers.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirDossier, DossierDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirDossier(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirDossierSante")
            .WithSummary("Parties du dossier (art. I.4-85 à I.4-87) : expositions, pièces jointes, questionnaires, vaccinations.");

        dossiers.MapGet("/{id:guid}/consultation", async (Guid id, DateOnly? date, IQueryHandler<ObtenirVueConsultation, VueConsultationDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirVueConsultation(id, date), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirVueConsultation")
            .WithSummary("SAN-20 : vue « poste de consultation » (postes et risques, historique, examens dus, questionnaires, résultats, alertes, rappels).");

        dossiers.MapGet("/{id:guid}/export", async (Guid id, IQueryHandler<ExporterDossier, ExportDossierDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ExporterDossier(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ExporterDossierSante")
            .WithSummary("SAN-43 : export structuré (JSON) du dossier pour le droit d'accès du travailleur ; journalisé comme export.");

        dossiers.MapPut("/{id:guid}/gestionnaire", async (Guid id, GestionnaireSaisi body, ICommandHandler<ChangerGestionnaire, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new ChangerGestionnaire(id, body.CpmtId), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("ChangerGestionnaireDossier");

        dossiers.MapPost("/{id:guid}/groupes-exposition", async (Guid id, GroupeSaisi body, ICommandHandler<RattacherGroupeExposition, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new RattacherGroupeExposition(id, body.GroupeExpositionId, body.AffilieId, body.ValideDu, body.ValideJusquAu), ct))
                .ToHttpResult(r => Results.Created($"/api/v1/dossiers/{id}", new { id = r })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("RattacherGroupeExposition")
            .WithSummary("SAN-40 : rattachement à un groupe d'exposition ; ses mesurages (Prévention) alimentent les données d'exposition.");

        dossiers.MapPost("/{id:guid}/expositions", async (Guid id, ExpositionSaisie body, ICommandHandler<AjouterExposition, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new AjouterExposition(id, body.Agent, body.Niveau, body.Debut, body.Fin), ct))
                .ToHttpResult(r => Results.Created($"/api/v1/dossiers/{id}", new { id = r })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("AjouterExposition");

        dossiers.MapPost("/{id:guid}/pieces-jointes", async (Guid id, PieceJointeSaisie body, ICommandHandler<AjouterPieceJointe, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new AjouterPieceJointe(id, body.DocumentId, body.Categorie, body.Titre, body.Description, body.DateDocument), ct))
                .ToHttpResult(r => Results.Created($"/api/v1/dossiers/{id}", new { id = r })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("AjouterPieceJointe")
            .WithSummary("SAN-41 : pièce jointe (fichier stocké par le service Documents, référence document_id).");

        dossiers.MapPost("/{id:guid}/questionnaires", async (Guid id, QuestionnaireSaisi body, ICommandHandler<EnregistrerQuestionnaire, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerQuestionnaire(id, body.ModeleCode, body.Reponses, body.ExamenId), ct))
                .ToHttpResult(r => Results.Created($"/api/v1/dossiers/{id}", new { id = r })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("EnregistrerQuestionnaire");

        dossiers.MapGet("/{id:guid}/questionnaires/{modeleCode}/pre-rempli", async (Guid id, string modeleCode, string? langue, HttpContext http,
                IQueryHandler<ObtenirQuestionnairePreRempli, QuestionnairePreRempliDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirQuestionnairePreRempli(id, modeleCode, http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirQuestionnairePreRempli")
            .WithSummary("SAN-22 : modèle et dernières réponses de la personne pour pré-remplir une saisie.");

        dossiers.MapPost("/{id:guid}/archivage", async (Guid id, ICommandHandler<ArchiverDossier, DossierResumeDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ArchiverDossier(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("ArchiverDossier")
            .WithSummary("SAN-44 : archivage et calcul de la date de purge (minimum légal ≥ 15 ans, durées par exposition).");

        dossiers.MapPost("/{id:guid}/vaccinations", async (Guid id, VaccinationSaisie body, ICommandHandler<AdministrerVaccin, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new AdministrerVaccin(id, body.VaccinCode, body.Dose, body.Date, body.LotId, body.Remarque), ct))
                .ToHttpResult(r => Results.Created($"/api/v1/dossiers/{id}", new { id = r })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("AdministrerVaccin")
            .WithSummary("SAN-50, SAN-51 : vaccination (lot décompté du stock) ; publie VaccinationAdministree.");

        dossiers.MapGet("/{id:guid}/vaccinations/rappels", async (Guid id, DateOnly? date, IQueryHandler<ObtenirRappels, IReadOnlyList<RappelVaccinal>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirRappels(id, date), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirRappelsVaccinaux");

        dossiers.MapPost("/{id:guid}/tests-tuberculiniques", async (Guid id, PoseTestSaisie body, ICommandHandler<PoserTestTuberculinique, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new PoserTestTuberculinique(id, body.DatePose), ct)).ToHttpResult(r => Results.Created($"/api/v1/dossiers/{id}", new { id = r })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("PoserTestTuberculinique");

        dossiers.MapPost("/{id:guid}/tests-tuberculiniques/{testId:guid}/lecture", async (Guid id, Guid testId, LectureTestSaisie body,
                ICommandHandler<LireTestTuberculinique, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new LireTestTuberculinique(id, testId, body.DateLecture, body.Resultat, body.IndurationMm), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("LireTestTuberculinique");

        dossiers.MapPost("/{id:guid}/declarations-mp", async (Guid id, DeclarationMpSaisie body, ICommandHandler<PreparerDeclarationMp, DeclarationMpDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new PreparerDeclarationMp(id, body.CodeMaladie, body.Description, body.AgentCausal), ct))
                .ToHttpResult(d => Results.Created($"/api/v1/declarations-mp/{d.Id}", d)))
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("PreparerDeclarationMp")
            .WithSummary("SAN-70 : déclaration de maladie professionnelle préremplie depuis le dossier.");

        dossiers.MapPost("/{id:guid}/transferts", async (Guid id, TransfertSaisi body, ICommandHandler<DemanderTransfert, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new DemanderTransfert(id, body.Contrepartie, body.Motif), ct)).ToHttpResult(t => Results.Created($"/api/v1/transferts/{t}", new { id = t })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("DemanderTransfertDossier")
            .WithSummary("SAN-42 : demande de transfert du dossier vers un autre SEPP ou SIPP.");
    }

    private static void MapExamens(RouteGroupBuilder examens)
    {
        examens.MapPost("/", async (OuvrirExamen body, ICommandHandler<OuvrirExamen, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/examens/{id}", new { id })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("OuvrirExamen")
            .WithSummary("Ouverture d'un examen, de préférence sur un rendez-vous planifié (affilié et obligations repris).");

        examens.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirExamen, ExamenDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirExamen(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirExamen");

        examens.MapPut("/{id:guid}/observation", async (Guid id, ObservationSaisie body, ICommandHandler<SaisirObservation, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new SaisirObservation(id, body.Anamnese, body.ExamenClinique), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("SaisirObservationClinique")
            .WithSummary("SAN-21 : anamnèse et examen clinique (chiffrés).");

        examens.MapPost("/{id:guid}/actes", async (Guid id, ActeSaisi body, ICommandHandler<EnregistrerActe, ResultatEnregistreDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerActe(id, body.TypeActe, body.Mesures, body.Commentaire, body.Date), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("EnregistrerActe")
            .WithSummary("SAN-21, SAN-23 : biométrie, vision, audiométrie, spirométrie, ECG, biologie ; alerte et proposition de fréquence si inhabituel.");

        examens.MapPost("/{id:guid}/imports", async (Guid id, ImportSaisi body, ICommandHandler<ImporterAppareil, ResultatEnregistreDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ImporterAppareil(id, body.TypeActe, body.Format, body.Contenu, body.Date), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("ImporterAppareil")
            .WithSummary("SAN-21 : import direct d'un appareil (HL7 v2 ORU, CSV, SIMULATEUR).");

        examens.MapPost("/{id:guid}/propositions-frequence/{propositionId:guid}", async (Guid id, Guid propositionId, DecisionPropositionSaisie body,
                ICommandHandler<DeciderPropositionFrequence, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new DeciderPropositionFrequence(id, propositionId, body.Acceptee), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("DeciderPropositionFrequence")
            .WithSummary("SAN-23 : acceptation ou refus de l'augmentation de fréquence (art. I.4-32).");

        examens.MapPost("/{id:guid}/cloture", async (Guid id, ClotureSaisie? body, ICommandHandler<CloturerExamen, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new CloturerExamen(id, body?.Date), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("CloturerExamen")
            .WithSummary("Clôture : obligations satisfaites, ExamenCloture publié (type et date uniquement).");

        examens.MapPost("/{id:guid}/decision", async (Guid id, ContenuDecisionSaisi body, ICommandHandler<RedigerDecision, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new RedigerDecision(id, body), ct)).ToHttpResult(d => Results.Created($"/api/v1/decisions/{d}", new { id = d })))
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("RedigerDecision")
            .WithSummary("SAN-31 : rédaction de la décision d'évaluation de santé (brouillon).");
    }

    private static void MapDecisions(RouteGroupBuilder decisions)
    {
        decisions.MapGet("/", async (Guid personneId, IQueryHandler<ListerDecisionsPersonne, IReadOnlyList<DecisionResumeDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerDecisionsPersonne(personneId), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionLire)
            .WithName("ListerDecisionsPersonne")
            .WithSummary("§3.3 : décisions émises d'une personne (catégorie, mesures, validité ; jamais de motif médical).");

        decisions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirDecision, DecisionResumeDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirDecision(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionLire)
            .WithName("ObtenirDecision");

        decisions.MapGet("/{id:guid}/complete", async (Guid id, IQueryHandler<ObtenirDecisionComplete, DecisionDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirDecisionComplete(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirDecisionComplete")
            .WithSummary("Décision avec justification, recommandations et procédures (zone médicale).");

        decisions.MapGet("/{id:guid}/formulaire", async (Guid id, ExemplaireFormulaire exemplaire, IQueryHandler<ObtenirFormulaire, FormulaireEvaluationSanteDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirFormulaire(id, exemplaire), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionLire)
            .WithName("ObtenirFormulaireEvaluationSante")
            .WithSummary("SAN-30 : données du formulaire (annexe I.4-2) par exemplaire : Employeur, Travailleur, Dossier.");

        decisions.MapPut("/{id:guid}", async (Guid id, ContenuDecisionSaisi body, ICommandHandler<ModifierDecision, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new ModifierDecision(id, body), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("ModifierDecision");

        decisions.MapPost("/{id:guid}/signature", async (Guid id, ICommandHandler<SignerDecision, DecisionResumeDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new SignerDecision(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("SignerDecision")
            .WithSummary("SAN-32, SAN-33 : signature qualifiée du CPMT puis émission (DecisionEmise).");

        decisions.MapPost("/{id:guid}/recours", async (Guid id, RecoursSaisi body, ICommandHandler<IntroduireRecours, RecoursDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new IntroduireRecours(id, body.Type, body.DateIntroduction), ct)).ToHttpResult(r => Results.Created($"/api/v1/decisions/{id}", r)))
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("IntroduireRecours")
            .WithSummary("SAN-34 : concertation ou recours auprès du médecin-inspecteur social (délais calculés).");

        decisions.MapPost("/{id:guid}/recours/{recoursId:guid}/issue", async (Guid id, Guid recoursId, IssueSaisie body,
                ICommandHandler<EnregistrerIssueRecours, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerIssueRecours(id, recoursId, body.Issue, body.DateIssue, body.Reformation, body.Commentaire), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("EnregistrerIssueRecours")
            .WithSummary("SAN-34 : issue (décision du médecin-inspecteur social) ; une décision réformée est retransmise.");
    }

    private static void MapVaccins(RouteGroupBuilder api)
    {
        var lots = api.MapGroup("/lots-vaccins").WithTags("Stock de vaccins").RequirePermission(Permissions.StockVaccinsGerer);
        lots.MapPost("/", async (ReceptionnerLot body, ICommandHandler<ReceptionnerLot, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/lots-vaccins/{id}", new { id })))
            .WithName("ReceptionnerLotVaccins")
            .WithSummary("SAN-51 : réception d'un lot (numéro, péremption, quantité) dans un centre.");

        lots.MapPost("/{id:guid}/ajustement", async (Guid id, AjustementSaisi body, ICommandHandler<AjusterLot, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new AjusterLot(id, body.Delta), ct)).ToHttpResult())
            .WithName("AjusterLotVaccins");

        api.MapGet("/centres/{centreId:guid}/stock-vaccins", async (Guid centreId, DateOnly? date, IQueryHandler<ObtenirStockCentre, IReadOnlyList<StockVaccinDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirStockCentre(centreId, date), ct)).ToHttpResult())
            .RequirePermission(Permissions.StockVaccinsGerer)
            .WithTags("Stock de vaccins")
            .WithName("ObtenirStockVaccins")
            .WithSummary("SAN-51 : stock par vaccin, lots périmés ou périmant dans les 30 jours.");
    }

    private static void MapProtocoles(RouteGroupBuilder api)
    {
        var protocoles = api.MapGroup("/protocoles").WithTags("Protocoles médicaux");

        protocoles.MapGet("/valeurs-reference", async (string? langue, HttpContext http, IQueryHandler<ListerValeursReference, IReadOnlyList<ValeurReferenceDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerValeursReference(http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerValeursReference");
        protocoles.MapPost("/valeurs-reference", async (DefinirValeurReference body, ICommandHandler<DefinirValeurReference, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/protocoles/valeurs-reference/{id}", new { id })))
            .RequirePermission(Permissions.ProtocolesMedicauxAdministrer)
            .WithName("DefinirValeurReference")
            .WithSummary("SAN-23 : valeur de référence (la précédente est clôturée, DAT-04).");

        protocoles.MapGet("/questionnaires", async (string? langue, HttpContext http, IQueryHandler<ListerModelesQuestionnaire, IReadOnlyList<ModeleQuestionnaireDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerModelesQuestionnaire(http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerModelesQuestionnaire");
        protocoles.MapPost("/questionnaires", async (PublierModeleQuestionnaire body, ICommandHandler<PublierModeleQuestionnaire, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/protocoles/questionnaires/{id}", new { id })))
            .RequirePermission(Permissions.ProtocolesMedicauxAdministrer)
            .WithName("PublierModeleQuestionnaire");

        protocoles.MapGet("/schemas-vaccinaux", async (string? langue, HttpContext http, IQueryHandler<ListerSchemasVaccinaux, IReadOnlyList<SchemaVaccinalDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerSchemasVaccinaux(http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerSchemasVaccinaux");
        protocoles.MapPost("/schemas-vaccinaux", async (DefinirSchemaVaccinal body, ICommandHandler<DefinirSchemaVaccinal, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/protocoles/schemas-vaccinaux/{id}", new { id })))
            .RequirePermission(Permissions.ProtocolesMedicauxAdministrer)
            .WithName("DefinirSchemaVaccinal")
            .WithSummary("SAN-50 : schéma vaccinal par risque ou périodicité des tests tuberculiniques.");

        protocoles.MapGet("/durees-conservation", async (IQueryHandler<ListerDureesConservation, IReadOnlyList<DureeConservationDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerDureesConservation(), ct)).ToHttpResult())
            .WithName("ListerDureesConservation");
        protocoles.MapPut("/durees-conservation", async (DefinirDureeConservation body, ICommandHandler<DefinirDureeConservation, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Ok(new { id })))
            .RequirePermission(Permissions.ProtocolesMedicauxAdministrer)
            .WithName("DefinirDureeConservation")
            .WithSummary("SAN-44 : durée de conservation par type d'exposition (≥ 15 ans).");

        var modeles = api.MapGroup("/modeles-texte").WithTags("Modèles de texte du CPMT").RequirePermission(Permissions.DossierSanteEcrire);
        modeles.MapGet("/", async (IQueryHandler<ListerModelesTexte, IReadOnlyList<ModeleTexteDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerModelesTexte(), ct)).ToHttpResult())
            .WithName("ListerModelesTexte")
            .WithSummary("SAN-24 : modèles de texte du CPMT connecté (la dictée vocale est hors périmètre).");
        modeles.MapPost("/", async (ModeleTexteSaisi body, ICommandHandler<EnregistrerModeleTexte, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerModeleTexte(null, body.Code, body.Rubrique, body.Titre, body.Texte), ct))
                .ToHttpResult(id => Results.Created($"/api/v1/modeles-texte/{id}", new { id })))
            .WithName("CreerModeleTexte");
        modeles.MapPut("/{id:guid}", async (Guid id, ModeleTexteSaisi body, ICommandHandler<EnregistrerModeleTexte, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerModeleTexte(id, body.Code, body.Rubrique, body.Titre, body.Texte), ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("ModifierModeleTexte");
        modeles.MapDelete("/{id:guid}", async (Guid id, ICommandHandler<SupprimerModeleTexte, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new SupprimerModeleTexte(id), ct)).ToHttpResult())
            .WithName("SupprimerModeleTexte");
    }

    private static void MapPurges(RouteGroupBuilder purges)
    {
        purges.MapPost("/propositions", async (DateOnly? date, ICommandHandler<ProposerPurges, IReadOnlyList<DossierResumeDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ProposerPurges(date), ct)).ToHttpResult())
            .WithName("ProposerPurges")
            .WithSummary("NF-22 : liste des dossiers dont la conservation est échue, proposés à la purge (aucune destruction).");

        purges.MapGet("/propositions", async (IQueryHandler<ListerPurgesProposees, IReadOnlyList<DossierResumeDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerPurgesProposees(), ct)).ToHttpResult())
            .WithName("ListerPurgesProposees");

        purges.MapPost("/{dossierId:guid}/validation", async (Guid dossierId, MotifSaisi body, ICommandHandler<ValiderPurge, PreuveDestructionDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ValiderPurge(dossierId, body.Motif), ct)).ToHttpResult())
            .WithName("ValiderPurge")
            .WithSummary("SAN-44 : validation humaine par le responsable du traitement ; destruction et preuve de destruction.");

        purges.MapPost("/{dossierId:guid}/refus", async (Guid dossierId, RefusPurgeSaisi body, ICommandHandler<RefuserPurge, DossierResumeDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new RefuserPurge(dossierId, body.ProlongationAnnees, body.Motif), ct)).ToHttpResult())
            .WithName("RefuserPurge");

        purges.MapGet("/preuves", async (IQueryHandler<ListerPreuvesDestruction, IReadOnlyList<PreuveDestructionDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerPreuvesDestruction(), ct)).ToHttpResult())
            .WithName("ListerPreuvesDestruction");
    }

    private static void MapDeclarationsMp(RouteGroupBuilder declarations)
    {
        declarations.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirDeclarationMp, DeclarationMpDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirDeclarationMp(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirDeclarationMp");

        declarations.MapPost("/{id:guid}/envoi", async (Guid id, ICommandHandler<EnvoyerDeclarationMp, DeclarationMpDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnvoyerDeclarationMp(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("EnvoyerDeclarationMp")
            .WithSummary("SAN-70 : envoi à Fedris (et au médecin-inspecteur social).");

        declarations.MapPost("/{id:guid}/suivi", async (Guid id, ICommandHandler<SuivreDeclarationMp, DeclarationMpDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new SuivreDeclarationMp(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("SuivreDeclarationMp")
            .WithSummary("SAN-71 : mise à jour du statut auprès de Fedris.");

        declarations.MapPost("/{id:guid}/demandes-information", async (Guid id, DemandeInformationSaisie body,
                ICommandHandler<EnregistrerDemandeInformationFedris, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerDemandeInformationFedris(id, body.DateDemande, body.Echeance, body.Objet), ct))
                .ToHttpResult(d => Results.Created($"/api/v1/declarations-mp/{id}", new { id = d })))
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("EnregistrerDemandeInformationFedris");

        declarations.MapPost("/{id:guid}/demandes-information/{demandeId:guid}/reponse", async (Guid id, Guid demandeId, ReponseInformationSaisie body,
                ICommandHandler<RepondreDemandeInformationFedris, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new RepondreDemandeInformationFedris(id, demandeId, body.Date, body.Reponse), ct)).ToHttpResult())
            .RequirePermission(Permissions.DecisionEcrire)
            .WithName("RepondreDemandeInformationFedris");
    }

    private static void MapTransferts(RouteGroupBuilder transferts)
    {
        transferts.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirTransfert, TransfertDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirTransfert(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteLire)
            .WithName("ObtenirTransfert");

        transferts.MapPost("/{id:guid}/transmission", async (Guid id, ICommandHandler<TransmettreTransfert, TransfertDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new TransmettreTransfert(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("TransmettreTransfert")
            .WithSummary("SAN-42 : export chiffré du dossier et transmission par le canal sécurisé (à confirmer).");

        transferts.MapPost("/entrants", async (RecevoirDossier body, ICommandHandler<RecevoirDossier, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/transferts/{id}", new { id })))
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("RecevoirDossier")
            .WithSummary("SAN-42 : réception d'un dossier entrant (contrôle d'intégrité par empreinte SHA-256).");

        transferts.MapPost("/{id:guid}/integration", async (Guid id, ICommandHandler<IntegrerTransfert, TransfertDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new IntegrerTransfert(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteEcrire)
            .WithName("IntegrerTransfert");
    }
}

// ---- Corps de requête (les identifiants viennent du chemin) ----

public sealed record GestionnaireSaisi(string CpmtId);

public sealed record GroupeSaisi(Guid GroupeExpositionId, Guid AffilieId, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record ExpositionSaisie(string Agent, string Niveau, DateOnly Debut, DateOnly? Fin);

public sealed record PieceJointeSaisie(Guid DocumentId, CategoriePieceJointe Categorie, string Titre, string? Description, DateOnly DateDocument);

public sealed record QuestionnaireSaisi(string ModeleCode, IReadOnlyList<ReponseSaisie> Reponses, Guid? ExamenId);

public sealed record VaccinationSaisie(string VaccinCode, int Dose, DateOnly? Date, Guid? LotId, string? Remarque);

public sealed record PoseTestSaisie(DateOnly? DatePose);

public sealed record LectureTestSaisie(DateOnly? DateLecture, ResultatTuberculinique Resultat, int? IndurationMm);

public sealed record DeclarationMpSaisie(string CodeMaladie, string Description, string? AgentCausal);

public sealed record TransfertSaisi(string Contrepartie, MotifTransfert Motif);

public sealed record ObservationSaisie(string? Anamnese, string? ExamenClinique);

public sealed record ActeSaisi(TypeActe TypeActe, IReadOnlyList<MesureSaisie> Mesures, string? Commentaire, DateOnly? Date);

public sealed record ImportSaisi(TypeActe TypeActe, string Format, string Contenu, DateOnly? Date);

public sealed record DecisionPropositionSaisie(bool Acceptee);

public sealed record ClotureSaisie(DateOnly? Date);

public sealed record RecoursSaisi(TypeRecours Type, DateOnly DateIntroduction);

public sealed record IssueSaisie(IssueRecours Issue, DateOnly DateIssue, ContenuDecisionSaisi? Reformation, string? Commentaire);

public sealed record AjustementSaisi(int Delta);

public sealed record ModeleTexteSaisi(string Code, RubriqueModeleTexte Rubrique, string Titre, string Texte);

public sealed record MotifSaisi(string Motif);

public sealed record RefusPurgeSaisi(int ProlongationAnnees, string Motif);

public sealed record DemandeInformationSaisie(DateOnly DateDemande, DateOnly? Echeance, string Objet);

public sealed record ReponseInformationSaisie(DateOnly Date, string Reponse);
