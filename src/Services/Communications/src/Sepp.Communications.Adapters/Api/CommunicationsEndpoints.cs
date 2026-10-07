using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Communications.Application.Messages;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Adapters.Api;

/// <summary>API REST du service Communications (ARC-30) : journal des messages et preuves d'envoi (DOC-05), envoi manuel, relance.</summary>
public static class CommunicationsEndpoints
{
    public static IEndpointRouteBuilder MapCommunicationsEndpoints(this IEndpointRouteBuilder app)
    {
        var messages = app.MapGroup("/api/v1/messages").WithTags("Messages").RequirePermission(Permissions.CommunicationsLire);

        messages.MapGet("/", async (TypeDestinataire? typeDestinataire, Guid? destinataireId, string? objetType, Guid? objetId, StatutMessage? statut, Canal? canal,
                int? page, int? taille, IQueryHandler<RechercherMessages, PageMessagesDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RechercherMessages(typeDestinataire, destinataireId, objetType, objetId, statut, canal, page, taille), ct)).ToHttpResult())
            .WithName("RechercherMessages")
            .WithSummary("Journal des messages (DOC-05), du plus récent au plus ancien.");

        messages.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirMessage, MessageDetailDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirMessage(id), ct)).ToHttpResult())
            .WithName("ObtenirMessage")
            .WithSummary("Message, corps et preuves d'envoi (DOC-05).");

        messages.MapPost("/", async (EnvoyerMessage body, ICommandHandler<EnvoyerMessage, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/messages/{id}", new { id })))
            .RequirePermission(Permissions.CommunicationsEnvoyer)
            .WithName("EnvoyerMessage")
            .WithSummary("Envoi manuel ; par e-mail ou SMS, seule la notification générique est envoyée.");

        messages.MapPost("/{id:guid}/relance", async (Guid id, ICommandHandler<RelancerMessage, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RelancerMessage(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.CommunicationsAdministrer)
            .WithName("RelancerMessage")
            .WithSummary("Relance d'un message abandonné.");

        messages.MapPost("/expedition", async (ICommandHandler<ExpedierMessagesEchus, ExpeditionDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ExpedierMessagesEchus(), ct)).ToHttpResult())
            .RequirePermission(Permissions.CommunicationsAdministrer)
            .WithName("ExpedierMessagesEchus")
            .WithSummary("Envoi immédiat des messages échus.");

        return app;
    }
}
