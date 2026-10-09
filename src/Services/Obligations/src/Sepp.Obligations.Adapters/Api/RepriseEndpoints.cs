using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Obligations.Application.Reprises;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Adapters.Api;

/// <summary>Annonce d'une reprise : travailleur, affilié, date de reprise et début de l'absence (identifiants et dates seulement, ARC-06).</summary>
public sealed record RepriseSaisie(Guid PersonneId, Guid AffilieId, DateOnly DateReprise, DateOnly DebutAbsence);

public sealed record RepriseModification(DateOnly DebutAbsence);

public sealed record RepriseAnnulationSaisie(string? Motif);

/// <summary>
/// API du processus de reprise (ARC-33, POR-04). Les permissions <c>reprise:annoncer</c>, <c>reprise:lire</c> et
/// <c>reprise:gerer</c> sont contrôlées par les cas d'usage, avec le périmètre de l'affilié (revendication <c>affilie_id</c>
/// des profils externes). Le BFF employeur relaie l'annonce en synchrone (ARC-30).
/// </summary>
public static class RepriseEndpoints
{
    public static IEndpointRouteBuilder MapRepriseEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/reprises").WithTags("Reprises");

        api.MapPost("/", async (RepriseSaisie body, ICommandHandler<EnregistrerReprise, ResultatEnregistrement> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new EnregistrerReprise(body.PersonneId, body.AffilieId, body.DateReprise, body.DebutAbsence), ct))
                    .ToHttpResult(r => r.Cree ? Results.Created($"/api/v1/reprises/{r.RepriseId}", r) : Results.Ok(r)))
            .RequirePermission(Permissions.RepriseAnnoncer)
            .WithName("EnregistrerReprise")
            .WithSummary("Annonce une reprise du travail (idempotent : même travailleur, affilié et date = même processus ; 201 à la création, 200 sinon).");

        api.MapGet("/", async (
                Guid? affilieId, StatutReprise? statut, DateOnly? echeanceAvant,
                IQueryHandler<ListerReprises, IReadOnlyList<RepriseDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerReprises(affilieId, statut, echeanceAvant), ct)).ToHttpResult())
            .RequirePermission(Permissions.RepriseLire)
            .WithName("ListerReprises")
            .WithSummary("Suivi des reprises et alertes : filtre par statut et par date limite au plus tard à echeanceAvant (POR-04).");

        api.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirReprise, RepriseDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirReprise(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.RepriseLire)
            .WithName("ObtenirReprise");

        api.MapPut("/{id:guid}", async (Guid id, RepriseModification body, ICommandHandler<ModifierReprise, ResultatEnregistrement> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierReprise(id, body.DebutAbsence), ct)).ToHttpResult())
            .RequirePermission(Permissions.RepriseGerer)
            .WithName("ModifierReprise")
            .WithSummary("Corrige le début de l'absence ; pour changer la date de reprise, annuler puis annoncer de nouveau.");

        api.MapPost("/{id:guid}/annulation", async (Guid id, RepriseAnnulationSaisie? body, ICommandHandler<AnnulerReprise, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AnnulerReprise(id, body?.Motif), ct)).ToHttpResult())
            .RequirePermission(Permissions.RepriseGerer)
            .WithName("AnnulerReprise")
            .WithSummary("Annule la reprise (compensation) ; 409 si l'examen est clôturé.");

        return app;
    }
}
