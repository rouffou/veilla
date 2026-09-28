using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Referentiels.Application.Calendrier;
using Sepp.Referentiels.Application.Nomenclatures;
using Sepp.Referentiels.Application.Parametres;

namespace Sepp.Referentiels.Adapters.Api;

/// <summary>API REST du service Référentiels (contrat OpenAPI, ARC-30 ; INT-01).</summary>
public static class ReferentielsEndpoints
{
    public static IEndpointRouteBuilder MapReferentielsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequirePermission(Permissions.ReferentielsLire);

        var parametres = api.MapGroup("/parametres-legaux").WithTags("Paramètres légaux");
        parametres.MapGet("/", async (DateOnly? date, string? langue, HttpContext http, TimeProvider clock,
                IQueryHandler<ListerParametres, IReadOnlyList<ParametreDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerParametres(date ?? clock.TodayInBelgium(), http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerParametresLegaux")
            .WithSummary("Paramètres légaux et leur valeur à une date (ARC-21).");

        parametres.MapGet("/{code}", async (string code, DateOnly? date, string? langue, HttpContext http, TimeProvider clock,
                IQueryHandler<ObtenirParametre, ParametreDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirParametre(code, date ?? clock.TodayInBelgium(), http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ObtenirParametreLegal");

        parametres.MapPost("/{code}/valeurs", async (string code, NouvelleValeur body,
                ICommandHandler<DefinirValeurParametre, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DefinirValeurParametre(code, body.Valeur, body.ValideDu), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("DefinirValeurParametreLegal")
            .WithSummary("Nouvelle valeur à partir d'une date ; la valeur en cours est clôturée (DAT-04).");

        var calendrier = api.MapGroup("/calendrier").WithTags("Calendrier");
        calendrier.MapGet("/{annee:int}/jours-feries", async (int annee, string? langue, HttpContext http,
                IQueryHandler<ListerJoursFeries, IReadOnlyList<JourFerieDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerJoursFeries(annee, http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerJoursFeries");

        calendrier.MapGet("/echeance", async (DateOnly depart, int joursOuvrables,
                IQueryHandler<CalculerEcheance, EcheanceDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CalculerEcheance(depart, joursOuvrables), ct)).ToHttpResult())
            .WithName("CalculerEcheance")
            .WithSummary("Échéance en jours ouvrables belges (DAT-08).");

        calendrier.MapPost("/jours-feries", async (AjouterJourFerie body,
                ICommandHandler<AjouterJourFerie, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult())
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("AjouterJourFerie");

        var nomenclatures = api.MapGroup("/nomenclatures").WithTags("Nomenclatures");
        nomenclatures.MapGet("/", async (string? langue, HttpContext http,
                IQueryHandler<ListerNomenclatures, IReadOnlyList<NomenclatureDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerNomenclatures(http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerNomenclatures");

        nomenclatures.MapGet("/{code}", async (string code, DateOnly? date, string? langue, HttpContext http, TimeProvider clock,
                IQueryHandler<ObtenirNomenclature, NomenclatureDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirNomenclature(code, date ?? clock.TodayInBelgium(), http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ObtenirNomenclature")
            .WithSummary("Codes en vigueur à une date, libellés dans la langue demandée (DAT-07).");

        nomenclatures.MapPost("/", async (CreerNomenclature body,
                ICommandHandler<CreerNomenclature, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/nomenclatures/{body.Code.Trim().ToUpperInvariant()}", new { id })))
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("CreerNomenclature");

        nomenclatures.MapPost("/{code}/entrees", async (string code, NouvelleEntree body,
                ICommandHandler<AjouterEntree, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AjouterEntree(code, body.Code, body.Libelle, body.ValideDu, body.CodeParent), ct)).ToHttpResult())
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("AjouterEntreeNomenclature");

        return app;
    }

    public sealed record NouvelleValeur(decimal Valeur, DateOnly ValideDu);

    public sealed record NouvelleEntree(string Code, LibellesDto Libelle, DateOnly ValideDu, string? CodeParent);
}
