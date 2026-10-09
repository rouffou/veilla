using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Personnes.Adapters.Imports;
using Sepp.Personnes.Application;
using Sepp.Personnes.Application.Affectations;
using Sepp.Personnes.Application.EtatsParticuliers;
using Sepp.Personnes.Application.Imports;
using Sepp.Personnes.Application.Mutations;
using Sepp.Personnes.Application.Occupations;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Adapters.Api;

/// <summary>
/// API REST du service Personnes et occupations (contrat OpenAPI, ARC-30). DAT-06 : le NISS n'apparaît jamais
/// dans une URL ; il n'est accepté que dans le corps des requêtes et n'est jamais renvoyé en clair.
/// </summary>
public static class PersonnesEndpoints
{
    /// <summary>Taille maximale d'un fichier d'import (AFF-21).</summary>
    public const int TailleImportMaximum = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapPersonnesEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequirePermission(Permissions.PersonneLire);
        MapPersonnes(api.MapGroup("/personnes").WithTags("Personnes"));
        MapAffilies(api.MapGroup("/affilies/{affilieId:guid}").WithTags("Travailleurs d'un affilié"));

        // AFF-20 : points d'entrée internes appelés par le compte technique du service Intégrations (rôle « integrations »),
        // qui n'a que personne:ecrire. Hors du groupe en lecture : la permission personne:lire n'y est pas exigée.
        MapDimona(app.MapGroup("/api/v1/dimona").WithTags("Interne : alimentation DIMONA (service Intégrations)").RequirePermission(Permissions.PersonneEcrire));

        // AFF-20, AFF-22 : mutations du registre national, même compte technique et mêmes garanties (NISS dans le corps uniquement).
        MapRegistreNational(app.MapGroup("/api/v1/registre-national").WithTags("Interne : mutations du registre national (service Intégrations)").RequirePermission(Permissions.PersonneEcrire));
        return app;
    }

    private static void MapRegistreNational(RouteGroupBuilder registre)
    {
        registre.MapPost("/mutations", async (EnregistrerMutationRegistreNational body, ICommandHandler<EnregistrerMutationRegistreNational, MutationEnregistreeDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult())
            .WithName("EnregistrerMutationRegistreNational")
            .WithSummary("AFF-20, AFF-22 : changement d'adresse, de nom, de prénom, de langue ou décès reçu du registre national par le service Intégrations ; historisé (DAT-04), idempotent sur la référence de la mutation. Un décès clôt les occupations actives.");
    }

    private static void MapPersonnes(RouteGroupBuilder personnes)
    {
        personnes.MapPost("/", async (CreerPersonne body, ICommandHandler<CreerPersonne, PersonneEnregistreeDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult(r => Results.Created($"/api/v1/personnes/{r.PersonneId}", r)))
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("CreerPersonne")
            .WithSummary("AFF-21 : saisie manuelle d'un travailleur ; un NISS déjà connu (doublon) reprend la personne existante.");

        personnes.MapPost("/recherche", async (RechercherParNiss body, IQueryHandler<RechercherParNiss, PersonneResumeDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult())
            .WithName("RechercherPersonneParNiss")
            .WithSummary("Recherche par NISS via l'index aveugle (DAT-06) ; le NISS est dans le corps, jamais dans l'URL.");

        personnes.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirPersonne, PersonneDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ObtenirPersonne(id), ct)).ToHttpResult())
            .WithName("ObtenirPersonne")
            .WithSummary("Fiche du travailleur (NISS masqué) et occupations visibles dans le périmètre de l'utilisateur.");

        personnes.MapPut("/{id:guid}/identite", async (Guid id, IdentiteDto body, ICommandHandler<ModifierIdentite, Unit> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ModifierIdentite(id, body), ct)).ToHttpResult())
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("ModifierIdentitePersonne");

        personnes.MapPut("/{id:guid}/coordonnees", async (Guid id, CoordonneesDto body, ICommandHandler<ModifierCoordonnees, Unit> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ModifierCoordonnees(id, body), ct)).ToHttpResult())
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("ModifierCoordonneesPersonne");

        personnes.MapGet("/{id:guid}/occupations", async (Guid id, IQueryHandler<ListerOccupations, IReadOnlyList<OccupationDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListerOccupations(id), ct)).ToHttpResult())
            .WithName("ListerOccupations");

        personnes.MapPost("/{id:guid}/occupations", async (Guid id, NouvelleOccupationDto body, ICommandHandler<DebuterOccupation, Guid> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DebuterOccupation(id, body), ct)).ToHttpResult(o => Results.Created($"/api/v1/personnes/{id}/occupations", new { id = o })))
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("DebuterOccupation")
            .WithSummary("AFF-21, AFF-23 : entrée chez un affilié (intérimaire : agence et affilié utilisateur).");

        personnes.MapPost("/{id:guid}/occupations/{occupationId:guid}/fin", async (Guid id, Guid occupationId, FinOccupation body,
                ICommandHandler<TerminerOccupation, Unit> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new TerminerOccupation(id, occupationId, body.DateFin), ct)).ToHttpResult())
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("TerminerOccupation")
            .WithSummary("Sortie (dernier jour inclus) ; les affectations en cours sont clôturées.");

        personnes.MapGet("/{id:guid}/affectations", async (Guid id, DateOnly? date, IQueryHandler<ListerAffectations, IReadOnlyList<AffectationDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListerAffectations(id, date), ct)).ToHttpResult())
            .WithName("ListerAffectations")
            .WithSummary("AFF-22 : historique des affectations (DAT-04), ou celles en vigueur à une date.");

        personnes.MapPost("/{id:guid}/occupations/{occupationId:guid}/affectations", async (Guid id, Guid occupationId, NouvelleAffectation body,
                ICommandHandler<Affecter, Guid> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new Affecter(id, occupationId, body.PosteId, body.SiteId, body.ValideDu, body.ValideJusquAu), ct))
                .ToHttpResult(a => Results.Created($"/api/v1/personnes/{id}/affectations", new { id = a })))
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("Affecter")
            .WithSummary("AFF-22 : affectation à un poste d'un site, du valideDu au valideJusquAu exclu.");

        personnes.MapPost("/{id:guid}/affectations/{affectationId:guid}/fin", async (Guid id, Guid affectationId, FinAffectation body,
                ICommandHandler<TerminerAffectation, Unit> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new TerminerAffectation(id, affectationId, body.APartirDu), ct)).ToHttpResult())
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("TerminerAffectation");

        personnes.MapPost("/{id:guid}/affectations/{affectationId:guid}/changement", async (Guid id, Guid affectationId, ChangementAffectation body,
                ICommandHandler<ChangerAffectation, Guid> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ChangerAffectation(id, affectationId, body.PosteId, body.SiteId, body.APartirDu), ct))
                .ToHttpResult(a => Results.Created($"/api/v1/personnes/{id}/affectations", new { id = a })))
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("ChangerAffectation")
            .WithSummary("DAT-04 : l'affectation en cours est clôturée et une nouvelle est créée.");

        personnes.MapGet("/{id:guid}/etats-particuliers", async (Guid id, IQueryHandler<ListerEtatsParticuliers, IReadOnlyList<EtatParticulierDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListerEtatsParticuliers(id), ct)).ToHttpResult())
            .WithName("ListerEtatsParticuliers");

        personnes.MapPost("/{id:guid}/etats-particuliers", async (Guid id, NouvelEtatParticulier body,
                ICommandHandler<DeclarerEtatParticulier, Guid> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DeclarerEtatParticulier(id, body.Type, body.DateDebut, body.DateFin), ct))
                .ToHttpResult(e => Results.Created($"/api/v1/personnes/{id}/etats-particuliers", new { id = e })))
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("DeclarerEtatParticulier")
            .WithSummary("AFF-23, AFF-24 : grossesse, allaitement, travail de nuit, jeune ; événement à catégorie générique (ARC-06).");

        personnes.MapPost("/{id:guid}/etats-particuliers/{etatId:guid}/fin", async (Guid id, Guid etatId, FinEtatParticulier body,
                ICommandHandler<TerminerEtatParticulier, Unit> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new TerminerEtatParticulier(id, etatId, body.DateFin), ct)).ToHttpResult())
            .RequirePermission(Permissions.PersonneEcrire)
            .WithName("TerminerEtatParticulier");
    }

    private static void MapAffilies(RouteGroupBuilder affilies)
    {
        affilies.MapGet("/travailleurs", async (Guid affilieId, DateOnly? date, TimeProvider clock,
                IQueryHandler<ListerTravailleurs, IReadOnlyList<PersonneResumeDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListerTravailleurs(affilieId, date ?? clock.TodayInBelgium()), ct)).ToHttpResult())
            .WithName("ListerTravailleursAffilie")
            .WithSummary("Travailleurs occupés ou mis à disposition chez l'affilié à une date.");

        affilies.MapPost("/imports/travailleurs", async (Guid affilieId, bool? simulation, HttpRequest request,
                ICommandHandler<ImporterTravailleurs, RapportImportDto> handler, CancellationToken ct) =>
            {
                var contenu = await LireCorpsAsync(request, ct);
                if (contenu is null)
                {
                    return ResultExtensions.ToProblem(Error.Validation("import.taille", $"Le fichier dépasse {TailleImportMaximum / (1024 * 1024)} Mo."));
                }

                var fichier = CsvLecteur.Lire(contenu);
                return (await handler.HandleAsync(new ImporterTravailleurs(affilieId, fichier.Colonnes, fichier.Lignes, simulation ?? false), ct)).ToHttpResult();
            })
            .RequirePermission(Permissions.PersonneEcrire)
            .Accepts<string>("text/csv")
            .WithName("ImporterTravailleurs")
            .WithSummary("AFF-21 : import CSV (UTF-8, « ; » ou « , ») avec contrôle du NISS, doublons et rapport ligne par ligne.");
    }

    private static void MapDimona(RouteGroupBuilder dimona)
    {
        dimona.MapPost("/entrees", async (EnregistrerEntreeDimona body, ICommandHandler<EnregistrerEntreeDimona, DimonaEnregistreeDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult())
            .WithName("EnregistrerEntreeDimona")
            .WithSummary("AFF-20 : entrée DIMONA reçue de la BCSS par le service Intégrations ; idempotent sur la référence DIMONA.");

        dimona.MapPost("/{reference}/sortie", async (string reference, FinOccupation body, ICommandHandler<EnregistrerSortieDimona, Unit> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new EnregistrerSortieDimona(reference, body.DateFin), ct)).ToHttpResult())
            .WithName("EnregistrerSortieDimona")
            .WithSummary("AFF-20 : sortie DIMONA ; idempotent.");
    }

    private static async Task<string?> LireCorpsAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > TailleImportMaximum)
        {
            return null;
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[TailleImportMaximum + 1];
        var total = 0;
        int lus;
        while (total < buffer.Length && (lus = await reader.ReadAsync(buffer.AsMemory(total), ct)) > 0)
        {
            total += lus;
        }

        return total > TailleImportMaximum ? null : new string(buffer, 0, total);
    }

    public sealed record FinOccupation(DateOnly DateFin);

    public sealed record NouvelleAffectation(Guid PosteId, Guid SiteId, DateOnly ValideDu, DateOnly? ValideJusquAu);

    public sealed record FinAffectation(DateOnly APartirDu);

    public sealed record ChangementAffectation(Guid PosteId, Guid SiteId, DateOnly APartirDu);

    public sealed record NouvelEtatParticulier(TypeEtatParticulier Type, DateOnly DateDebut, DateOnly? DateFin);

    public sealed record FinEtatParticulier(DateOnly DateFin);
}
