using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.PostesRisques.Application.Listes;
using Sepp.PostesRisques.Application.Postes;
using Sepp.PostesRisques.Application.Risques;
using Sepp.PostesRisques.Application.Surcharges;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

namespace Sepp.PostesRisques.Adapters.Api;

/// <summary>
/// API REST du service Postes et risques (contrat OpenAPI, ARC-30 ; INT-01). Les permissions de la matrice §3.3
/// sont exigées ici quand une seule suffit ; les règles combinées et le périmètre de l'affilié sont vérifiés
/// par les cas d'usage.
/// </summary>
public static class PostesRisquesEndpoints
{
    public static IEndpointRouteBuilder MapPostesRisquesEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequirePermission(Permissions.PosteLire);
        MapPostes(api);
        MapPropositionsPosteRisque(api);
        MapRisques(api);
        MapSurcharges(api);
        MapListes(api);
        return app;
    }

    private static void MapPostes(RouteGroupBuilder api)
    {
        var postes = api.MapGroup("/postes").WithTags("Postes");

        postes.MapGet("/", async (Guid affilieId, StatutPoste? statut, DateOnly? date, TimeProvider clock,
                IQueryHandler<ListerPostes, IReadOnlyList<PosteDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerPostes(affilieId, statut, date ?? clock.TodayInBelgium()), ct)).ToHttpResult())
            .WithName("ListerPostes")
            .WithSummary("Catalogue des postes d'un affilié, risques en vigueur à une date (AFF-10).");

        postes.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirPoste, PosteDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirPoste(id), ct)).ToHttpResult())
            .WithName("ObtenirPoste")
            .WithSummary("Poste et historique complet de ses liens aux risques (DAT-04).");

        postes.MapGet("/{id:guid}/profil-risques", async (Guid id, DateOnly? date, string? langue, HttpContext http, TimeProvider clock,
                IQueryHandler<ObtenirProfilRisques, ProfilRisquesDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirProfilRisques(id, date ?? clock.TodayInBelgium(), http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ObtenirProfilRisquesPoste")
            .WithSummary("Risques du poste à une date et règles de surveillance qu'ils portent (AFF-11, AFF-12).");

        postes.MapPost("/", async (CreerPoste body, ICommandHandler<CreerPoste, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/postes/{id}", new { id })))
            .RequirePermission(Permissions.PosteEcrire)
            .WithName("CreerPoste");

        postes.MapPut("/{id:guid}", async (Guid id, ModificationPoste body, ICommandHandler<ModifierPoste, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierPoste(id, body.Intitule, body.Description, body.MetierTypeCode), ct)).ToHttpResult())
            .RequirePermission(Permissions.PosteEcrire)
            .WithName("ModifierPoste");

        postes.MapPost("/{id:guid}/archivage", async (Guid id, DateOnly? date, TimeProvider clock,
                ICommandHandler<ArchiverPoste, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ArchiverPoste(id, date ?? clock.TodayInBelgium()), ct)).ToHttpResult())
            .RequirePermission(Permissions.PosteEcrire)
            .WithName("ArchiverPoste");

        postes.MapPost("/{id:guid}/reactivation", async (Guid id, ICommandHandler<ReactiverPoste, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ReactiverPoste(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.PosteEcrire)
            .WithName("ReactiverPoste");

        postes.MapPost("/{id:guid}/propositions", async (Guid id, NouvellePropositionPosteRisque body,
                ICommandHandler<SoumettrePropositionPosteRisque, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new SoumettrePropositionPosteRisque(id, body.Motif, body.ValideDu, body.Lignes, body.AvisCppt), ct))
                .ToHttpResult(pid => Results.Created($"/api/v1/propositions-poste-risque/{pid}", new { id = pid })))
            .WithName("ProposerModificationProfilRisques")
            .WithSummary("Proposition de modification du lien poste ↔ risque, sans effet avant la validation du CPMT (AFF-14, AFF-31).");
    }

    private static void MapPropositionsPosteRisque(RouteGroupBuilder api)
    {
        var propositions = api.MapGroup("/propositions-poste-risque").WithTags("Propositions poste-risque");

        propositions.MapGet("/", async (Guid? affilieId, Guid? posteId, StatutProposition? statut,
                IQueryHandler<ListerPropositionsPosteRisque, IReadOnlyList<PropositionPosteRisqueDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerPropositionsPosteRisque(affilieId, posteId, statut), ct)).ToHttpResult())
            .WithName("ListerPropositionsPosteRisque");

        propositions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirPropositionPosteRisque, PropositionPosteRisqueDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirPropositionPosteRisque(id), ct)).ToHttpResult())
            .WithName("ObtenirPropositionPosteRisque");

        propositions.MapPost("/{id:guid}/avis-cppt", async (Guid id, AvisCpptSaisie body, ICommandHandler<JoindreAvisCppt, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new JoindreAvisCppt(id, body.Date, body.DocumentId), ct)).ToHttpResult())
            .WithName("JoindreAvisCppt")
            .WithSummary("Avis du Comité PPT : date et pièce jointe (AFF-14).");

        propositions.MapPost("/{id:guid}/validation", async (Guid id, DecisionValidation body,
                ICommandHandler<ValiderPropositionPosteRisque, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ValiderPropositionPosteRisque(id, body.DateAvisCppt, body.DocumentAvisCpptId), ct)).ToHttpResult())
            .RequirePermission(Permissions.RisquePosteValider)
            .WithName("ValiderPropositionPosteRisque")
            .WithSummary("Validation par le CPMT : le profil est appliqué et ProfilRisquePosteModifie publié (AFF-14).");

        propositions.MapPost("/{id:guid}/refus", async (Guid id, DecisionRefus body,
                ICommandHandler<RefuserPropositionPosteRisque, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RefuserPropositionPosteRisque(id, body.Motif), ct)).ToHttpResult())
            .RequirePermission(Permissions.RisquePosteValider)
            .WithName("RefuserPropositionPosteRisque");
    }

    private static void MapRisques(RouteGroupBuilder api)
    {
        var risques = api.MapGroup("/risques").WithTags("Risques");

        risques.MapGet("/", async (CategorieRisque? categorie, DateOnly? date, string? langue, HttpContext http, TimeProvider clock,
                IQueryHandler<ListerRisques, IReadOnlyList<RisqueDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerRisques(categorie, date ?? clock.TodayInBelgium(), http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ListerRisques")
            .WithSummary("Référentiel des risques (annexe I.4-5) et règle applicable à une date (AFF-11, AFF-12).");

        risques.MapGet("/{code}", async (string code, DateOnly? date, string? langue, HttpContext http, TimeProvider clock,
                IQueryHandler<ObtenirRisque, RisqueDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirRisque(code, date ?? clock.TodayInBelgium(), http.ResolveLanguage(langue)), ct)).ToHttpResult())
            .WithName("ObtenirRisque");

        risques.MapPost("/", async (CreerRisque body, ICommandHandler<CreerRisque, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/risques/{body.Code.Trim().ToUpperInvariant()}", new { id })))
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("CreerRisque");

        risques.MapPost("/{code}/regles", async (string code, RegleSaisie body, ICommandHandler<DefinirRegleSurveillance, int> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DefinirRegleSurveillance(code, body), ct)).ToHttpResult(version => Results.Ok(new { version })))
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("DefinirRegleSurveillance")
            .WithSummary("Nouvelle version de la règle de surveillance ; la règle en vigueur est clôturée (AFF-12, DAT-04).");

        risques.MapPost("/import", async (HttpRequest request, DateOnly? valideDu, TimeProvider clock,
                ICommandHandler<ImporterRisques, ImportRisquesDto> handler, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var contenu = await reader.ReadToEndAsync(ct);
                return (await handler.HandleAsync(new ImporterRisques(contenu, valideDu ?? clock.TodayInBelgium()), ct)).ToHttpResult();
            })
            .Accepts<string>("text/csv")
            .RequirePermission(Permissions.ReferentielsAdministrer)
            .WithName("ImporterRisques")
            .WithSummary("Import CSV (séparateur « ; ») du référentiel des risques et de leurs règles, atomique et idempotent (AFF-11).");
    }

    private static void MapSurcharges(RouteGroupBuilder api)
    {
        var surcharges = api.MapGroup("/surcharges-frequence").WithTags("Surcharges de fréquence");

        surcharges.MapGet("/", async (Guid affilieId, CibleSurcharge? cibleType, Guid? cibleId, DateOnly? date,
                IQueryHandler<ListerSurchargesFrequence, IReadOnlyList<SurchargeDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerSurchargesFrequence(affilieId, cibleType, cibleId, date), ct)).ToHttpResult())
            .RequirePermission(Permissions.SurchargeFrequenceLire)
            .WithName("ListerSurchargesFrequence");

        surcharges.MapPost("/", async (DefinirSurchargeFrequence body, ICommandHandler<DefinirSurchargeFrequence, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/surcharges-frequence/{id}", new { id })))
            .RequirePermission(Permissions.SurchargeFrequenceEcrire)
            .WithName("DefinirSurchargeFrequence")
            .WithSummary("Surcharge de fréquence par le CPMT : poste, groupe ou travailleur, motif et période (AFF-13).");

        surcharges.MapPost("/{id:guid}/cloture", async (Guid id, Cloture body, ICommandHandler<CloturerSurchargeFrequence, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CloturerSurchargeFrequence(id, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.SurchargeFrequenceEcrire)
            .WithName("CloturerSurchargeFrequence");
    }

    private static void MapListes(RouteGroupBuilder api)
    {
        var listes = api.MapGroup("/listes-nominatives").WithTags("Listes nominatives");

        listes.MapGet("/", async (Guid affilieId, TypeListeNominative? type,
                IQueryHandler<ListerListesNominatives, IReadOnlyList<ListeNominativeDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerListesNominatives(affilieId, type), ct)).ToHttpResult())
            .WithName("ListerListesNominatives")
            .WithSummary("Historique des versions des listes d'un affilié (AFF-31).");

        listes.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirListeNominative, ListeNominativeDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirListeNominative(id), ct)).ToHttpResult())
            .WithName("ObtenirListeNominative");

        listes.MapPost("/", async (GenerationListe body, TimeProvider clock,
                ICommandHandler<GenererListeNominative, ListeGenereeDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new GenererListeNominative(body.AffilieId, body.Type, body.DateReference ?? clock.TodayInBelgium()), ct))
                .ToHttpResult(dto => Results.Created($"/api/v1/listes-nominatives/{dto.Id}", dto)))
            .WithName("GenererListeNominative")
            .WithSummary("Nouvelle version de la liste nominative d'un type, avec la date de la dernière évaluation (AFF-30).");

        listes.MapPut("/{id:guid}/document", async (Guid id, DocumentListe body, ICommandHandler<AssocierDocumentListe, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AssocierDocumentListe(id, body.DocumentId), ct)).ToHttpResult())
            .WithName("AssocierDocumentListeNominative");

        listes.MapPost("/{id:guid}/propositions", async (Guid id, NouvellePropositionListe body,
                ICommandHandler<SoumettrePropositionListe, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new SoumettrePropositionListe(id, body.Motif, body.Lignes), ct))
                .ToHttpResult(pid => Results.Created($"/api/v1/propositions-liste-nominative/{pid}", new { id = pid })))
            .WithName("ProposerModificationListeNominative")
            .WithSummary("Proposition de modification d'une liste (employeur via le portail), soumise au CPMT (AFF-31).");

        var propositions = api.MapGroup("/propositions-liste-nominative").WithTags("Listes nominatives");

        propositions.MapGet("/", async (Guid? affilieId, StatutProposition? statut,
                IQueryHandler<ListerPropositionsListe, IReadOnlyList<PropositionListeDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerPropositionsListe(affilieId, statut), ct)).ToHttpResult())
            .WithName("ListerPropositionsListeNominative");

        propositions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirPropositionListe, PropositionListeDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirPropositionListe(id), ct)).ToHttpResult())
            .WithName("ObtenirPropositionListeNominative");

        propositions.MapPost("/{id:guid}/validation", async (Guid id, ICommandHandler<ValiderPropositionListe, ListeGenereeDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ValiderPropositionListe(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.RisquePosteValider)
            .WithName("ValiderPropositionListeNominative")
            .WithSummary("Validation par le CPMT : nouvelle version de la liste (AFF-31).");

        propositions.MapPost("/{id:guid}/refus", async (Guid id, DecisionRefus body,
                ICommandHandler<RefuserPropositionListe, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RefuserPropositionListe(id, body.Motif), ct)).ToHttpResult())
            .RequirePermission(Permissions.RisquePosteValider)
            .WithName("RefuserPropositionListeNominative");
    }

    public sealed record ModificationPoste(string Intitule, string? Description, string? MetierTypeCode);

    public sealed record NouvellePropositionPosteRisque(string Motif, DateOnly ValideDu, IReadOnlyList<LigneRisqueSaisie> Lignes, AvisCpptSaisie? AvisCppt);

    public sealed record DecisionValidation(DateOnly? DateAvisCppt, Guid? DocumentAvisCpptId);

    public sealed record DecisionRefus(string Motif);

    public sealed record Cloture(DateOnly Fin);

    public sealed record GenerationListe(Guid AffilieId, TypeListeNominative Type, DateOnly? DateReference);

    public sealed record DocumentListe(Guid DocumentId);

    public sealed record NouvellePropositionListe(string Motif, IReadOnlyList<DemandeLigneListe> Lignes);
}
