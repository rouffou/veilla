using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application.Republication;
using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.BuildingBlocks.Web;

/// <summary>Corps de <c>POST /api/v1/admin/republications</c> : type d'état à republier (par ex. <c>occupations</c>).</summary>
public sealed record DemandeRepublicationRequete(string Type);

/// <summary>
/// Point d'entrée d'exploitation d'un producteur (ADR 0011) : republier l'état courant pour rattraper les projections locales
/// des consommateurs. Réservé à <c>plateforme:republier</c> (rôle <c>exploitation</c>), sans aucune donnée métier en réponse.
/// </summary>
public static class RepublicationEndpoints
{
    public const string Route = "/api/v1/admin/republications";

    public static IEndpointRouteBuilder MapSeppRepublications(this IEndpointRouteBuilder app)
    {
        var groupe = app.MapGroup(Route)
            .RequirePermission(Permissions.PlateformeRepublier)
            .WithTags("Exploitation");

        groupe.MapPost("/", async (DemandeRepublicationRequete body, IRepublications republications, CancellationToken ct) =>
                (await republications.DemanderAsync(body.Type, ct)).ToHttpResult(r => Results.Accepted($"{Route}/{r.Id}", r)))
            .WithName("DemanderRepublication")
            .WithSummary("ADR 0011 : republie par lots l'état courant d'un type (instantanés via l'outbox) ; 202, suivi par GET.");

        groupe.MapGet("/", async (IRepublications republications, CancellationToken ct) =>
                (await republications.ListerAsync(ct)).ToHttpResult())
            .WithName("ListerRepublications")
            .WithSummary("Republications les plus récentes (type, statut, compteurs).");

        groupe.MapGet("/types", (IRepublications republications) => Results.Ok(republications.Types))
            .WithName("ListerTypesRepublication")
            .WithSummary("Types d'état que ce service sait republier.");

        groupe.MapGet("/{id:guid}", async (Guid id, IRepublications republications, CancellationToken ct) =>
                (await republications.ConsulterAsync(id, ct)).ToHttpResult())
            .WithName("ConsulterRepublication")
            .WithSummary("Avancement d'une republication : EnCours, Terminee ou Echouee, nombre d'instantanés écrits.");
        return app;
    }
}
