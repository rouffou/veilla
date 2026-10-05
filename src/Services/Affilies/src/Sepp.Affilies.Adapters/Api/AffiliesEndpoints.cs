using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Concertation;
using Sepp.Affilies.Application.Groupes;
using Sepp.Affilies.Application.Hierarchie;
using Sepp.Affilies.Application.Historique;
using Sepp.Affilies.Application.Operations;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Affilies.Adapters.Api;

/// <summary>
/// API REST du service Affiliés (contrat OpenAPI, ARC-30). Lecture : <c>affilie:lire</c> ; écriture : <c>affilie:ecrire</c>.
/// Le périmètre (affilié de l'employeur ou du SIPP, parties de la fiche modifiables) est vérifié par les cas d'usage (§3.3).
/// </summary>
public static class AffiliesEndpoints
{
    public static IEndpointRouteBuilder MapAffiliesEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequirePermission(Permissions.AffilieLire);
        MapGroupes(api.MapGroup("/groupes").WithTags("Groupes"));
        var affilies = api.MapGroup("/affilies");
        MapFiche(affilies.MapGroup("").WithTags("Affiliés"));
        MapHierarchie(affilies.MapGroup("/{affilieId:guid}").WithTags("Hiérarchie"));
        MapConcertation(affilies.MapGroup("/{affilieId:guid}").WithTags("Contacts et concertation"));
        MapOperations(affilies.MapGroup("/{affilieId:guid}/operations").WithTags("Fusions, scissions et transferts"));
        return app;
    }

    private static void MapGroupes(RouteGroupBuilder groupes)
    {
        groupes.MapGet("/", async (IQueryHandler<ListerGroupes, IReadOnlyList<GroupeDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerGroupes(), ct)).ToHttpResult())
            .WithName("ListerGroupes");

        groupes.MapPost("/", async (NomGroupe body, ICommandHandler<CreerGroupe, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CreerGroupe(body.Nom), ct)).ToHttpResult(Cree(id => $"/api/v1/groupes/{id}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("CreerGroupe");

        groupes.MapPut("/{groupeId:guid}", async (Guid groupeId, NomGroupe body, ICommandHandler<RenommerGroupe, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RenommerGroupe(groupeId, body.Nom), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("RenommerGroupe");
    }

    private static void MapFiche(RouteGroupBuilder affilies)
    {
        affilies.MapGet("/", async (string? bce, string? denomination, int? page, int? taille,
                IQueryHandler<RechercherAffilies, PageDto<AffilieResumeDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RechercherAffilies(bce, denomination, page ?? 1, taille ?? 20), ct)).ToHttpResult())
            .WithName("RechercherAffilies")
            .WithSummary("Recherche par numéro BCE et / ou dénomination, paginée.");

        affilies.MapGet("/bce/{numero}", async (string numero, IQueryHandler<ObtenirAffilieParBce, AffilieDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirAffilieParBce(numero), ct)).ToHttpResult())
            .WithName("ObtenirAffilieParBce");

        affilies.MapGet("/{affilieId:guid}", async (Guid affilieId, IQueryHandler<ObtenirAffilie, AffilieDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirAffilie(affilieId), ct)).ToHttpResult())
            .WithName("ObtenirAffilie")
            .WithSummary("Fiche complète : identité, hiérarchie, contacts, organes de concertation, opérations (AFF-01 à AFF-06).");

        affilies.MapPost("/", async (CreerAffilie body, ICommandHandler<CreerAffilie, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(Cree(id => $"/api/v1/affilies/{id}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("CreerAffilie")
            .WithSummary("Affiliation d'un employeur (numéro BCE contrôlé par modulo 97).");

        affilies.MapPut("/{affilieId:guid}", async (Guid affilieId, FicheCorps body, ICommandHandler<ModifierFiche, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierFiche(affilieId, body.Fiche, body.GroupeId), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ModifierFicheAffilie");

        affilies.MapPost("/{affilieId:guid}/resiliation", async (Guid affilieId, DateFinCorps body, ICommandHandler<ResilierAffiliation, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ResilierAffiliation(affilieId, body.DateFin), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ResilierAffiliation");

        affilies.MapDelete("/{affilieId:guid}/resiliation", async (Guid affilieId, ICommandHandler<AnnulerResiliation, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AnnulerResiliation(affilieId), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("AnnulerResiliation");

        affilies.MapGet("/{affilieId:guid}/historique", async (Guid affilieId, IQueryHandler<ConsulterHistorique, IReadOnlyList<ModificationDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ConsulterHistorique(affilieId), ct)).ToHttpResult())
            .WithName("ConsulterHistoriqueAffilie")
            .WithSummary("Historique versionné : qui, quand, avant, après (AFF-05).");
    }

    private static void MapHierarchie(RouteGroupBuilder affilie)
    {
        affilie.MapPost("/unites-etablissement", async (Guid affilieId, UniteCorps body, ICommandHandler<AjouterUniteEtablissement, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AjouterUniteEtablissement(affilieId, body.Numero, body.Nom, body.Adresse, body.Langue, body.Depuis), ct))
            .ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("AjouterUniteEtablissement");

        affilie.MapPut("/unites-etablissement/{uniteId:guid}", async (Guid affilieId, Guid uniteId, UniteModifiee body, ICommandHandler<ModifierUniteEtablissement, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierUniteEtablissement(affilieId, uniteId, body.Nom, body.Adresse, body.Langue), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ModifierUniteEtablissement");

        affilie.MapPost("/unites-etablissement/{uniteId:guid}/fermeture", async (Guid affilieId, Guid uniteId, FinCorps body, ICommandHandler<FermerUniteEtablissement, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new FermerUniteEtablissement(affilieId, uniteId, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("FermerUniteEtablissement");

        affilie.MapPost("/unites-etablissement/{uniteId:guid}/sites", async (Guid affilieId, Guid uniteId, SiteCorps body, ICommandHandler<AjouterSite, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AjouterSite(affilieId, uniteId, body.Nom, body.Adresse, body.Latitude, body.Longitude, body.Depuis), ct))
            .ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("AjouterSite");

        affilie.MapPut("/sites/{siteId:guid}", async (Guid affilieId, Guid siteId, SiteModifie body, ICommandHandler<ModifierSite, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierSite(affilieId, siteId, body.Nom, body.Adresse, body.Latitude, body.Longitude), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ModifierSite");

        affilie.MapPost("/sites/{siteId:guid}/fermeture", async (Guid affilieId, Guid siteId, FinCorps body, ICommandHandler<FermerSite, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new FermerSite(affilieId, siteId, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("FermerSite");

        affilie.MapPost("/sites/{siteId:guid}/departements", async (Guid affilieId, Guid siteId, DepartementCorps body, ICommandHandler<AjouterDepartement, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AjouterDepartement(affilieId, siteId, body.Nom, body.ParentId, body.Depuis), ct))
            .ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("AjouterDepartement");

        affilie.MapPut("/departements/{departementId:guid}", async (Guid affilieId, Guid departementId, DepartementModifie body, ICommandHandler<ModifierDepartement, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierDepartement(affilieId, departementId, body.Nom, body.ParentId), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ModifierDepartement");

        affilie.MapPost("/departements/{departementId:guid}/fermeture", async (Guid affilieId, Guid departementId, FinCorps body, ICommandHandler<FermerDepartement, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new FermerDepartement(affilieId, departementId, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("FermerDepartement");
    }

    private static void MapConcertation(RouteGroupBuilder affilie)
    {
        affilie.MapPost("/contacts", async (Guid affilieId, ContactCorps body, ICommandHandler<AjouterContact, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AjouterContact(affilieId, body.ToSaisie(), body.ValideDu), ct)).ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("AjouterContact")
            .WithSummary("Contact avec rôle et période de validité (AFF-03) ; ouvert à l'employeur et au SIPP sur leur affilié.");

        affilie.MapPut("/contacts/{contactId:guid}", async (Guid affilieId, Guid contactId, ContactCorps body, ICommandHandler<ModifierContact, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierContact(affilieId, contactId, body.ToSaisie(), body.ValideDu), ct)).ToHttpResult(id => Results.Ok(new { id })))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ModifierContact")
            .WithSummary("Clôture la ligne en vigueur et crée une nouvelle ligne à partir de « valideDu » (DAT-04).");

        affilie.MapPost("/contacts/{contactId:guid}/fin", async (Guid affilieId, Guid contactId, FinCorps body, ICommandHandler<TerminerContact, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new TerminerContact(affilieId, contactId, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("TerminerContact");

        affilie.MapPost("/organes-concertation", async (Guid affilieId, OrganeCorps body, ICommandHandler<InstallerOrgane, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new InstallerOrgane(affilieId, body.Type, body.Depuis), ct)).ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("InstallerOrganeConcertation")
            .WithSummary("Comité PPT ou délégation syndicale (AFF-04).");

        affilie.MapPost("/organes-concertation/{organeId:guid}/dissolution", async (Guid affilieId, Guid organeId, FinCorps body, ICommandHandler<DissoudreOrgane, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DissoudreOrgane(affilieId, organeId, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("DissoudreOrganeConcertation");

        affilie.MapPost("/organes-concertation/{organeId:guid}/reunions", async (Guid affilieId, Guid organeId, ReunionCorps body, ICommandHandler<PlanifierReunion, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new PlanifierReunion(affilieId, organeId, body.DateReunion, body.OrdreDuJourDocumentId, body.ParticipationSepp), ct))
            .ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("PlanifierReunion");

        affilie.MapPut("/organes-concertation/{organeId:guid}/reunions/{reunionId:guid}", async (Guid affilieId, Guid organeId, Guid reunionId, ReunionCorps body,
                ICommandHandler<ModifierReunion, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierReunion(affilieId, organeId, reunionId, body.DateReunion, body.OrdreDuJourDocumentId, body.ParticipationSepp), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ModifierReunion");
    }

    private static void MapOperations(RouteGroupBuilder operations)
    {
        operations.MapPost("/", async (Guid affilieId, OperationCorps body, ICommandHandler<ProjeterOperation, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ProjeterOperation(affilieId, body.Type, body.DateEffet, body.AffilieAbsorbantId, body.AffiliesBeneficiaires, body.SeppDestination), ct))
            .ToHttpResult(Cree(id => $"/api/v1/affilies/{affilieId}")))
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("ProjeterOperation")
            .WithSummary("Fusion, scission ou transfert sortant vers un autre SEPP (AFF-06).");

        operations.MapPost("/{operationId:guid}/realisation", async (Guid affilieId, Guid operationId, ICommandHandler<RealiserOperation, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RealiserOperation(affilieId, operationId), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("RealiserOperation");

        operations.MapPost("/{operationId:guid}/annulation", async (Guid affilieId, Guid operationId, ICommandHandler<AnnulerOperation, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AnnulerOperation(affilieId, operationId), ct)).ToHttpResult())
            .RequirePermission(Permissions.AffilieEcrire)
            .WithName("AnnulerOperation");
    }

    private static Func<Guid, IResult> Cree(Func<Guid, string> location) => id => Results.Created(location(id), new { id });

    public sealed record NomGroupe(string Nom);

    public sealed record FicheCorps(FicheSaisie Fiche, Guid? GroupeId);

    public sealed record DateFinCorps(DateOnly DateFin);

    public sealed record FinCorps(DateOnly Fin);

    public sealed record UniteCorps(string Numero, string Nom, AdresseDto Adresse, Language Langue, DateOnly Depuis);

    public sealed record UniteModifiee(string Nom, AdresseDto Adresse, Language Langue);

    public sealed record SiteCorps(string Nom, AdresseDto Adresse, double? Latitude, double? Longitude, DateOnly Depuis);

    public sealed record SiteModifie(string Nom, AdresseDto Adresse, double? Latitude, double? Longitude);

    public sealed record DepartementCorps(string Nom, Guid? ParentId, DateOnly Depuis);

    public sealed record DepartementModifie(string Nom, Guid? ParentId);

    /// <summary>Contact ; <c>ValideDu</c> est la date de prise d'effet (ajout ou nouvelle version).</summary>
    public sealed record ContactCorps(string Nom, string? Fonction, RoleContact Role, string? Email, string? Telephone, DateOnly ValideDu)
    {
        public ContactSaisi ToSaisie() => new(Nom, Fonction, Role, Email, Telephone);
    }

    public sealed record OrganeCorps(TypeOrgane Type, DateOnly Depuis);

    public sealed record ReunionCorps(DateOnly DateReunion, Guid? OrdreDuJourDocumentId, bool ParticipationSepp);

    public sealed record OperationCorps(TypeOperation Type, DateOnly DateEffet, Guid? AffilieAbsorbantId, IReadOnlyList<Guid>? AffiliesBeneficiaires, string? SeppDestination);
}
