using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Reintegration.Adapters.Securite;
using Sepp.Reintegration.Application.Echanges;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Application.TableauDeBord;
using Sepp.Reintegration.Application.Trajets;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Adapters.Api;

/// <summary>
/// API REST du service Réintégration (zone médicale, ARC-30). Les permissions de la matrice §3.3 sont exigées ici quand une
/// seule suffit ; la relation de soin, le motif, le bris de glace, le périmètre des externes et la journalisation de chaque
/// accès sont appliqués par les cas d'usage (<c>GardeReintegration</c>). Le motif voyage dans l'en-tête <c>X-Motif-Acces</c>,
/// jamais dans l'URL ; aucun identifiant autre que les UUID n'apparaît dans les chemins (DAT-06).
/// </summary>
public static class ReintegrationEndpoints
{
    public static IEndpointRouteBuilder MapReintegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter(VerifierMotif);
        MapIncapacites(api.MapGroup("/incapacites").WithTags("Dossiers d'incapacité (SAN-60, SAN-61)"));
        MapTrajets(api.MapGroup("/trajets").WithTags("Trajets de réintégration (SAN-62, SAN-63)"));
        MapEchanges(api.MapGroup("/echanges-ehealth").WithTags("Échanges eHealth (SAN-64)"));

        api.MapGet("/tableau-de-bord/delais", async (Guid? affilieId, StatutEcheance? statut, bool? inclureRespectees,
                    IQueryHandler<ObtenirTableauDeBord, TableauDeBordDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirTableauDeBord(affilieId, statut, inclureRespectees ?? false), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationLire)
            .WithTags("Tableau de bord des délais (SAN-65)")
            .WithName("ObtenirTableauDeBordDelais")
            .WithSummary("SAN-65 : échéances légales des dossiers et trajets ouverts, alertes avant dépassement (Proche) et dépassements.");

        api.MapGet("/affilies/{affilieId:guid}/trajets", async (Guid affilieId, IQueryHandler<SuivreTrajets, IReadOnlyList<SuiviTrajetDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new SuivreTrajets(affilieId), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationSuiviLire)
            .WithTags("Suivi employeur (sans donnée médicale)")
            .WithName("SuivreTrajetsAffilie")
            .WithSummary("SAN-62 : statuts, échéances et conclusion du formulaire des trajets de l'affilié (employeur et SIPP limités à leur affilié).");
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

    private static void MapIncapacites(RouteGroupBuilder g)
    {
        g.MapPost("/", async (SaisirIncapacite body, ICommandHandler<SaisirIncapacite, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/incapacites/{id}", new { id })))
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("SaisirIncapacite")
            .WithSummary("SAN-60 : saisie manuelle d'une notification d'incapacité reçue hors flux eHealth.");

        g.MapGet("/", async (Guid personneId, IQueryHandler<RechercherIncapacites, IReadOnlyList<IncapaciteResumeDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new RechercherIncapacites(personneId), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationLire)
            .WithName("RechercherIncapacites")
            .WithSummary("Dossiers d'une personne : identifiants, dates et statuts (chaque dossier trouvé est tracé).");

        g.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirIncapacite, IncapaciteDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirIncapacite(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationLire)
            .WithName("ObtenirIncapacite")
            .WithSummary("Dossier de réintégration : estimations, trajets et échéances légales.");

        g.MapPost("/{id:guid}/prise-en-charge", async (Guid id, ICommandHandler<PrendreEnCharge, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new PrendreEnCharge(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("PrendreEnChargeIncapacite")
            .WithSummary("L'utilisateur devient gestionnaire du dossier (relation de soin ; motif exigé s'il ne l'était pas).");

        g.MapPut("/{id:guid}/effectif-employeur", async (Guid id, EffectifDto body, ICommandHandler<DefinirEffectifEmployeur, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new DefinirEffectifEmployeur(id, body.Effectif), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("DefinirEffectifEmployeur")
            .WithSummary("Effectif de l'employeur : trajet obligatoire à partir de REINTEGRATION.SEUIL_TRAVAILLEURS.");

        g.MapPost("/{id:guid}/information-travailleur", async (Guid id, InformationDto body, ICommandHandler<InformerTravailleur, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new InformerTravailleur(id, body.Date, body.DocumentId), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("InformerTravailleur")
            .WithSummary("SAN-60 : information du travailleur (dès REINTEGRATION.INFORMATION_TRAVAILLEUR d'incapacité).");

        g.MapPost("/{id:guid}/estimations", async (Guid id, EstimationSaisieDto body, ICommandHandler<EstimerPotentiel, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new EstimerPotentiel(id, body.Date, body.Resultat, body.Methode, body.Commentaire), ct))
                .ToHttpResult(e => Results.Created($"/api/v1/incapacites/{id}", new { id = e })))
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("EstimerPotentielTravail")
            .WithSummary("SAN-61 : estimation du potentiel de travail par le CPMT ou l'infirmier (commentaire chiffré).");

        g.MapPost("/{id:guid}/fin", async (Guid id, FinIncapaciteDto body, ICommandHandler<TerminerIncapacite, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new TerminerIncapacite(id, body.DernierJour), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("TerminerIncapacite");

        g.MapPost("/{id:guid}/annulation", async (Guid id, ICommandHandler<AnnulerIncapacite, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new AnnulerIncapacite(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("AnnulerIncapacite")
            .WithSummary("Notification erronée : annulation et compensation du trajet rattaché.");

        g.MapGet("/{id:guid}/echanges-ehealth", async (Guid id, IQueryHandler<ListerEchanges, IReadOnlyList<EchangeDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerEchanges(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationLire)
            .WithName("ListerEchangesEHealth");
    }

    private static void MapTrajets(RouteGroupBuilder g)
    {
        g.MapPost("/", async (DemanderTrajet body, ICommandHandler<DemanderTrajet, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/trajets/{id}", new { id })))
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("DemanderTrajet")
            .WithSummary("SAN-62 : demande de trajet ; publie reintegration.trajet-demarre.");

        g.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirTrajet, TrajetDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirTrajet(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationLire)
            .WithName("ObtenirTrajet");

        g.MapPost("/{id:guid}/invitation", async (Guid id, InvitationDto body, ICommandHandler<InviterTravailleur, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new InviterTravailleur(id, body.Date, body.Recommandee, body.DocumentId), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("InviterTravailleur");

        g.MapPost("/{id:guid}/evaluation", async (Guid id, EvaluationDto body, ICommandHandler<EvaluerReintegration, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new EvaluerReintegration(id, body.Date, body.Note), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEvaluer)
            .WithName("EvaluerReintegration");

        g.MapPost("/{id:guid}/formulaire", async (Guid id, FormulaireDto body, ICommandHandler<EtablirFormulaire, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new EtablirFormulaire(id, body.Date, body.Conclusion, body.DocumentId), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEvaluer)
            .WithName("EtablirFormulaireReintegration");

        // Employeur / SIPP (périmètre affilie_id), gestionnaire ou professionnel de la zone médicale : contrôlé par le cas d'usage.
        g.MapPost("/{id:guid}/reponse-employeur", async (Guid id, ReponseEmployeurDto body, ICommandHandler<DeposerReponseEmployeur, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new DeposerReponseEmployeur(id, body.Reponse, body.Date, body.DocumentId), ct)).ToHttpResult())
            .WithName("DeposerReponseEmployeur")
            .WithSummary("Plan de réintégration ou rapport motivé de l'employeur (référence au document).");

        // Travailleur lui-même (claim personne_id) ou professionnel de la zone médicale : contrôlé par le cas d'usage.
        g.MapPost("/{id:guid}/reponse-travailleur", async (Guid id, ReponseTravailleurDto body, ICommandHandler<EnregistrerReponseTravailleur, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerReponseTravailleur(id, body.Reponse, body.Date), ct)).ToHttpResult())
            .WithName("EnregistrerReponseTravailleur")
            .WithSummary("Acceptation ou refus du plan (REINTEGRATION.REPONSE_PLAN.DELAI).");

        g.MapPost("/{id:guid}/fin", async (Guid id, FinTrajetDto body, ICommandHandler<TerminerTrajet, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new TerminerTrajet(id, body.Date, body.Issue), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("TerminerTrajet")
            .WithSummary("Fin du trajet ; publie reintegration.trajet-termine.");

        g.MapPost("/{id:guid}/annulation", async (Guid id, AnnulationDto body, ICommandHandler<AnnulerTrajet, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new AnnulerTrajet(id, body.Date), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("AnnulerTrajet")
            .WithSummary("Compensation : arrêt du trajet, envois eHealth non effectués annulés, trajet-termine(ANNULE).");

        g.MapPost("/{id:guid}/absences", async (Guid id, AbsenceSaisieDto body, ICommandHandler<SignalerAbsence, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new SignalerAbsence(id, body.DateRendezVous), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("SignalerAbsenceRendezVous")
            .WithSummary("SAN-63 : absence à un rendez-vous ; au seuil, information du médecin-conseil par eHealth.");
    }

    private static void MapEchanges(RouteGroupBuilder g)
    {
        g.MapPost("/", async (PreparerEchange body, ICommandHandler<PreparerEchange, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/echanges-ehealth/{id}", new { id })))
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("PreparerEchangeEHealth")
            .WithSummary("SAN-64 : message au médecin traitant (accord du travailleur), au médecin-conseil ou au coordinateur Retour au travail.");

        g.MapPost("/{id:guid}/relance", async (Guid id, ICommandHandler<RelancerEchange, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new RelancerEchange(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("RelancerEchangeEHealth");

        g.MapPost("/receptions", async (ICommandHandler<RecevoirEchanges, int> h, CancellationToken ct) =>
                (await h.HandleAsync(new RecevoirEchanges(), ct)).ToHttpResult(n => Results.Ok(new { recus = n })))
            .RequirePermission(Permissions.ReintegrationEcrire)
            .WithName("RecevoirEchangesEHealth");
    }

    public sealed record EffectifDto(int Effectif);

    public sealed record InformationDto(DateOnly Date, Guid? DocumentId);

    public sealed record EstimationSaisieDto(DateOnly Date, ResultatEstimation Resultat, string Methode, string? Commentaire);

    public sealed record FinIncapaciteDto(DateOnly DernierJour);

    public sealed record InvitationDto(DateOnly Date, bool Recommandee, Guid? DocumentId);

    public sealed record EvaluationDto(DateOnly Date, string? Note);

    public sealed record FormulaireDto(DateOnly Date, ConclusionEvaluation Conclusion, Guid? DocumentId);

    public sealed record ReponseEmployeurDto(ReponseEmployeur Reponse, DateOnly Date, Guid? DocumentId);

    public sealed record ReponseTravailleurDto(ReponseTravailleur Reponse, DateOnly Date);

    public sealed record FinTrajetDto(DateOnly Date, IssueTrajet Issue);

    public sealed record AnnulationDto(DateOnly Date);

    public sealed record AbsenceSaisieDto(DateOnly DateRendezVous);
}
