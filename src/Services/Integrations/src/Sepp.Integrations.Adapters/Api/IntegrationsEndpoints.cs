using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Adapters.Api;

/// <summary>
/// API REST du service Intégrations (INT-01, contrat OpenAPI) : tableau de suivi des flux (INT-02), relance manuelle,
/// lancement à la demande, correspondances d'identifiants (§15.3) et lecture des données BCE reçues.
/// Aucune réponse ne contient de charge utile ni de NISS.
/// </summary>
public static class IntegrationsEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        MapFlux(api.MapGroup("/flux").WithTags("Suivi des flux").RequirePermission(Permissions.IntegrationsAdministrer));
        MapCorrespondances(api.MapGroup("/correspondances").WithTags("Correspondances d'identifiants").RequirePermission(Permissions.IntegrationsAdministrer));

        // Données d'entreprise publiques : profils qui lisent les affiliés (contrôle dans le cas d'usage).
        api.MapGet("/bce/entreprises/{numeroBce}", async (string numeroBce, IQueryHandler<ObtenirEntrepriseBce, EntrepriseBceDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ObtenirEntrepriseBce(numeroBce), ct)).ToHttpResult())
            .WithTags("BCE")
            .WithName("ObtenirEntrepriseBce")
            .WithSummary("Dernières données BCE reçues d'une entreprise, unités d'établissement et adresses comprises.");
        return app;
    }

    private static void MapFlux(RouteGroupBuilder flux)
    {
        flux.MapGet("/journal", async (string? flux, StatutEchange[]? statut, DateOnly? du, DateOnly? au, int? page, int? taille,
                IQueryHandler<ListerJournal, PageDto<EchangeFluxDto>> handler, CancellationToken ct) =>
            {
                var type = LireFlux(flux);
                return type.IsSuccess
                    ? (await handler.HandleAsync(new ListerJournal(type.Value, statut?.ToHashSet(), du, au, page ?? 1, taille ?? 50), ct)).ToHttpResult()
                    : ResultExtensions.ToProblem(type.Error!);
            })
            .WithName("ListerJournalFlux")
            .WithSummary("INT-02 : échanges journalisés, filtrés par flux (bce, dimona, registre-national), statut et période.");

        flux.MapGet("/erreurs", async (string? flux, DateOnly? du, DateOnly? au, int? page, int? taille,
                IQueryHandler<ListerJournal, PageDto<EchangeFluxDto>> handler, CancellationToken ct) =>
            {
                var type = LireFlux(flux);
                return type.IsSuccess
                    ? (await handler.HandleAsync(new ListerJournal(type.Value, new HashSet<StatutEchange> { StatutEchange.Rejete, StatutEchange.EnErreur },
                        du, au, page ?? 1, taille ?? 50), ct)).ToHttpResult()
                    : ResultExtensions.ToProblem(type.Error!);
            })
            .WithName("ListerErreursFlux")
            .WithSummary("INT-02 : échanges rejetés ou en erreur.");

        flux.MapGet("/volumes", async (DateOnly? du, DateOnly? au, IQueryHandler<ObtenirVolumes, VolumesDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ObtenirVolumes(du, au), ct)).ToHttpResult())
            .WithName("ObtenirVolumesFlux")
            .WithSummary("INT-02 : volumes par flux, par jour et sur la période (30 derniers jours par défaut, 366 au plus).");

        flux.MapGet("/journal/{id:guid}", async (Guid id, IQueryHandler<ObtenirEchange, EchangeFluxDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ObtenirEchange(id), ct)).ToHttpResult())
            .WithName("ObtenirEchangeFlux");

        flux.MapPost("/journal/{id:guid}/relance", async (Guid id, ICommandHandler<RelancerEchange, EchangeFluxDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RelancerEchange(id), ct)).ToHttpResult())
            .WithName("RelancerEchangeFlux")
            .WithSummary("INT-02 : relance manuelle d'un échange rejeté ou en erreur ; sans effet sur un échange déjà traité (idempotent).");

        flux.MapPost("/{flux}/executions", async (string flux, ICommandHandler<LancerFlux, RapportExecutionDto> handler, CancellationToken ct) =>
            {
                var type = LireFlux(flux);
                return type.IsSuccess && type.Value is { } f
                    ? (await handler.HandleAsync(new LancerFlux(f), ct)).ToHttpResult()
                    : ResultExtensions.ToProblem(type.Error ?? Error.Validation("flux.inconnu", "Flux obligatoire."));
            })
            .WithName("LancerFlux")
            .WithSummary("Lance un flux à la demande (bce, dimona, registre-national) : réception, journalisation et traitement.");

        flux.MapPost("/bce/consultations", async (DemandeConsultationBce body, ICommandHandler<ConsulterBce, EchangeFluxDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ConsulterBce(body.NumeroBce), ct)).ToHttpResult())
            .WithName("ConsulterBce")
            .WithSummary("Consultation BCE d'une entreprise à la demande ; une consultation identique le même jour renvoie l'échange existant.");
    }

    private static void MapCorrespondances(RouteGroupBuilder correspondances)
    {
        correspondances.MapGet("/", async (TypeIdentifiantExterne? type, string? valeur, Guid? identifiantInterne,
                IQueryHandler<ListerCorrespondances, IReadOnlyList<CorrespondanceDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListerCorrespondances(type, valeur, identifiantInterne), ct)).ToHttpResult())
            .WithName("ListerCorrespondances")
            .WithSummary("§15.3 : correspondances identifiant externe ↔ identifiant interne (jamais de NISS).");

        correspondances.MapPut("/", async (DefinirCorrespondance body, ICommandHandler<DefinirCorrespondance, CorrespondanceDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult())
            .WithName("DefinirCorrespondance")
            .WithSummary("Crée ou met à jour une correspondance (reprise de données) ; idempotent.");
    }

    /// <summary>Flux désigné en kebab-case dans l'API : <c>bce</c>, <c>dimona</c>, <c>registre-national</c>.</summary>
    private static Result<TypeFlux?> LireFlux(string? valeur) => valeur?.Trim().ToLowerInvariant() switch
    {
        null or "" => (TypeFlux?)null,
        "bce" => TypeFlux.Bce,
        "dimona" => TypeFlux.Dimona,
        "registre-national" or "registrenational" => TypeFlux.RegistreNational,
        _ => Error.Validation("flux.inconnu", $"Flux inconnu : '{valeur}' (attendu : bce, dimona, registre-national)."),
    };

    public sealed record DemandeConsultationBce(string NumeroBce);
}
