using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Obligations.Application.Consultation;
using Sepp.Obligations.Application.Gestion;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Adapters.Api;

/// <summary>Corps des demandes de pré-reprise ou de consultation spontanée : le type et la date, jamais le motif (ARC-06).</summary>
public sealed record DemandeSaisie(Guid PersonneId, Guid AffilieId, string Type, DateOnly? DateDemande);

public sealed record ReportSaisie(DateOnly Date);

public sealed record AnnulationSaisie(string Motif);

public sealed record RecalculSaisie(Guid? PersonneId, Guid? AffilieId);

/// <summary>
/// API REST du service Obligations (contrat OpenAPI, ARC-30). Zone standard : types d'examens, dates et statuts, aucune
/// donnée médicale. La lecture exige <c>obligation:lire</c> ; le périmètre de l'affilié (revendication <c>affilie_id</c>
/// des profils externes) est vérifié par les cas d'usage.
/// </summary>
public static class ObligationsEndpoints
{
    public static IEndpointRouteBuilder MapObligationsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequirePermission(Permissions.ObligationLire);
        MapConsultation(api);
        MapGestion(api);
        app.MapRepriseEndpoints();
        return app;
    }

    private static void MapConsultation(RouteGroupBuilder api)
    {
        api.MapGet("/personnes/{personneId:guid}/obligations", async (
                Guid personneId, bool? ouvertesSeulement, StatutObligation? statut, string? langue, HttpContext http,
                IQueryHandler<ListerObligationsPersonne, IReadOnlyList<ObligationDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerObligationsPersonne(personneId, ouvertesSeulement ?? false, statut, http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithTags("Obligations")
            .WithName("ListerObligationsPersonne")
            .WithSummary("Obligations d'un travailleur, avec statut et échéance (SAN-01, SAN-02).");

        api.MapGet("/affilies/{affilieId:guid}/obligations", async (
                Guid affilieId, string? categorie, string? type, bool? ouvertesSeulement, string? langue, HttpContext http,
                IQueryHandler<ListerObligationsAffilie, IReadOnlyList<ObligationDto>> handler, CancellationToken ct) =>
            {
                if (categorie is not null && !Enum.TryParse<CategorieObligation>(categorie, ignoreCase: true, out _))
                {
                    return ResultExtensions.ToProblem(Error.Validation("obligation.categorie-invalide", "La catégorie doit être EnRetard, Planifiee, Due ou AVenir."));
                }

                if (type is not null && !TypesObligation.TryParse(type, out _))
                {
                    return ResultExtensions.ToProblem(Error.Validation("obligation.type-invalide", $"Type d'obligation inconnu : {type}."));
                }

                var categorieLue = categorie is null ? (CategorieObligation?)null : Enum.Parse<CategorieObligation>(categorie, ignoreCase: true);
                TypeObligation? typeLu = type is not null && TypesObligation.TryParse(type, out var t) ? t : null;
                return (await handler.HandleAsync(
                    new ListerObligationsAffilie(affilieId, categorieLue, typeLu, ouvertesSeulement ?? false, http.ResolveLanguage(langue)), ct)).ToHttpResult();
            })
            .WithTags("Obligations")
            .WithName("ListerObligationsAffilie")
            .WithSummary("Obligations d'un affilié : dues, en retard ou planifiées pour le tableau de bord employeur (POR-02).");

        api.MapGet("/affilies/{affilieId:guid}/obligations/synthese", async (
                Guid affilieId, IQueryHandler<SynthetiserObligationsAffilie, SyntheseObligationsDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new SynthetiserObligationsAffilie(affilieId), ct)).ToHttpResult())
            .WithTags("Obligations")
            .WithName("SynthetiserObligationsAffilie")
            .WithSummary("Décomptes en retard / planifiées / dues / à venir d'un affilié (POR-02).");

        api.MapGet("/affilies/{affilieId:guid}/obligations/regroupables", async (
                Guid affilieId, int? fenetreJours, string? langue, HttpContext http,
                IQueryHandler<ListerRegroupables, IReadOnlyList<PropositionRendezVousDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerRegroupables(affilieId, fenetreJours, http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithTags("Obligations")
            .WithName("ListerRegroupables")
            .WithSummary("Obligations regroupables en un seul rendez-vous par travailleur, dans une fenêtre paramétrable (SAN-03).");

        api.MapGet("/affilies/{affilieId:guid}/alertes", async (
                Guid affilieId, IQueryHandler<ListerAlertes, IReadOnlyList<AlerteDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerAlertes(affilieId), ct)).ToHttpResult())
            .WithTags("Alertes")
            .WithName("ListerAlertes")
            .WithSummary("Alertes de non-couverture : travailleur exposé non planifié, poste sans analyse, liste non revue (AFF-32).");

        api.MapGet("/obligations/{id:guid}", async (
                Guid id, string? langue, HttpContext http, IQueryHandler<ObtenirObligation, ObligationDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirObligation(id, http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithTags("Obligations")
            .WithName("ObtenirObligation");

        api.MapGet("/obligations/{id:guid}/trace", async (
                Guid id, string? langue, HttpContext http, IQueryHandler<ObtenirTraceCalcul, TraceObligationDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirTraceCalcul(id, http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithTags("Obligations")
            .WithName("ObtenirTraceCalcul")
            .WithSummary("Trace de calcul : règle ou paramètre appliqué, version, entrées et explication (SAN-04, §15.3).");
    }

    private static void MapGestion(RouteGroupBuilder api)
    {
        api.MapPost("/obligations/{id:guid}/convocation", (Guid id, ICommandHandler<ChangerStatutObligation, Unit> handler, CancellationToken ct) =>
            Changer(handler, new ChangerStatutObligation(id, ActionStatut.Convoquer), ct))
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Statuts")
            .WithName("ConvoquerObligation");

        api.MapPost("/obligations/{id:guid}/absence", (Guid id, ICommandHandler<ChangerStatutObligation, Unit> handler, CancellationToken ct) =>
            Changer(handler, new ChangerStatutObligation(id, ActionStatut.MarquerAbsent), ct))
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Statuts")
            .WithName("MarquerObligationAbsent");

        api.MapPost("/obligations/{id:guid}/report", (Guid id, ReportSaisie body, ICommandHandler<ChangerStatutObligation, Unit> handler, CancellationToken ct) =>
            Changer(handler, new ChangerStatutObligation(id, ActionStatut.Reporter, body.Date), ct))
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Statuts")
            .WithName("ReporterObligation");

        api.MapPost("/obligations/{id:guid}/excuse", (Guid id, ICommandHandler<ChangerStatutObligation, Unit> handler, CancellationToken ct) =>
            Changer(handler, new ChangerStatutObligation(id, ActionStatut.Excuser), ct))
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Statuts")
            .WithName("ExcuserObligation");

        api.MapPost("/obligations/{id:guid}/annulation", (Guid id, AnnulationSaisie body, ICommandHandler<ChangerStatutObligation, Unit> handler, CancellationToken ct) =>
            Changer(handler, new ChangerStatutObligation(id, ActionStatut.Annuler, Motif: body.Motif), ct))
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Statuts")
            .WithName("AnnulerObligation");

        api.MapPost("/demandes-travailleur", async (DemandeSaisie body, ICommandHandler<EnregistrerDemandeTravailleur, Guid> handler, CancellationToken ct) =>
            {
                if (!TypesObligation.TryParse(body.Type, out var type))
                {
                    return ResultExtensions.ToProblem(Error.Validation("demande.type-invalide", $"Type de demande inconnu : {body.Type}."));
                }

                return (await handler.HandleAsync(new EnregistrerDemandeTravailleur(body.PersonneId, body.AffilieId, type, body.DateDemande), ct))
                    .ToHttpResult(id => Results.Created($"/api/v1/personnes/{body.PersonneId}/obligations", new { id }));
            })
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Demandes")
            .WithName("EnregistrerDemandeTravailleur")
            .WithSummary("Demande du travailleur (pré-reprise, consultation spontanée) : obligation à 10 jours ouvrables (§5.1).");

        api.MapPost("/recalcul", async (RecalculSaisie body, ICommandHandler<DemanderRecalcul, int> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DemanderRecalcul(body.PersonneId, body.AffilieId), ct)).ToHttpResult(n => Results.Ok(new { personnesRecalculees = n })))
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Calcul")
            .WithName("DemanderRecalcul")
            .WithSummary("Recalcul immédiat des échéances d'un travailleur ou d'un affilié (SAN-04).");

        api.MapPost("/traitement-echeances", async (ICommandHandler<TraiterEcheances, ResultatTraitement> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new TraiterEcheances(), ct)).ToHttpResult())
            .RequirePermission(Permissions.ObligationGerer)
            .WithTags("Calcul")
            .WithName("TraiterEcheances")
            .WithSummary("Lance à la demande le traitement périodique : recalcul et publication des ObligationEchue.");
    }

    private static async Task<IResult> Changer(ICommandHandler<ChangerStatutObligation, Unit> handler, ChangerStatutObligation commande, CancellationToken ct) =>
        (await handler.HandleAsync(commande, ct)).ToHttpResult();
}
