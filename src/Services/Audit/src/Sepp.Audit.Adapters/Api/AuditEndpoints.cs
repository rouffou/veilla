using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.Audit.Application.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Audit.Adapters.Api;

/// <summary>
/// API de consultation du journal d'audit (NF-04, PSY-20), en lecture seule : le journal n'est alimenté que par
/// les événements <c>audit.acces-donnee-sensible</c>. Réservée à <see cref="Permissions.AuditLire"/>, restreinte par zone.
/// </summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/audit").RequirePermission(Permissions.AuditLire).WithTags("Journal d'audit");

        api.MapGet("/entrees", async (string? zone, string? utilisateurId, string? objetType, Guid? objetId,
                DateTimeOffset? du, DateTimeOffset? au, bool? brisDeGlace, int? page, int? taille,
                IQueryHandler<RechercherEntrees, PageDto<EntreeAuditDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RechercherEntrees(zone, utilisateurId, objetType, objetId, du, au, brisDeGlace, page ?? 1, taille ?? 50), ct)).ToHttpResult())
            .WithName("RechercherEntreesAudit")
            .WithSummary("Recherche par utilisateur, objet, période et zone, dans le périmètre de l'utilisateur (PSY-20).");

        api.MapGet("/entrees/{id:guid}", async (Guid id, IQueryHandler<ObtenirEntree, EntreeAuditDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirEntree(id), ct)).ToHttpResult())
            .WithName("ObtenirEntreeAudit");

        api.MapGet("/bris-de-glace", async (string? zone, DateTimeOffset? du, DateTimeOffset? au, int? page, int? taille,
                IQueryHandler<RechercherEntrees, PageDto<EntreeAuditDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RechercherEntrees(zone, Du: du, Au: au, BrisDeGlace: true, Page: page ?? 1, Taille: taille ?? 50), ct)).ToHttpResult())
            .WithName("ListerBrisDeGlace")
            .WithSummary("Alertes « bris de glace » (§3.3) : CPMT dirigeant (zone médicale), CPAP dirigeant (zone psychosociale), DPO.");

        api.MapGet("/integrite/{zone}", async (string zone, IQueryHandler<VerifierIntegrite, IntegriteDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new VerifierIntegrite(zone), ct)).ToHttpResult())
            .WithName("VerifierIntegriteJournal")
            .WithSummary("Vérifie la chaîne d'empreintes d'une zone : détecte toute ligne modifiée ou supprimée (NF-04).");

        return app;
    }
}
