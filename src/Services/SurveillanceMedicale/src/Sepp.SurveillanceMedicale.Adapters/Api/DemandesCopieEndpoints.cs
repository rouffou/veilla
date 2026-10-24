using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.SurveillanceMedicale.Application.DemandesCopie;
using Sepp.SurveillanceMedicale.Domain.DemandesCopie;

namespace Sepp.SurveillanceMedicale.Adapters.Api;

/// <summary>Corps du refus d'une demande de copie : motif codé uniquement.</summary>
public sealed record RefusCopieSaisi(MotifRefusCopie Motif);

/// <summary>
/// Droit d'accès du travailleur (§3.3 « copie sur demande », SAN-43) et carnet de vaccination (SAN-50), POR-13. Trois
/// publics, trois permissions : le travailleur dépose et suit sa demande (aucun contenu médical, personne du jeton), le
/// CPMT la traite, le compte technique du service Documents lit le contenu à publier. Le portail ne lit jamais le
/// dossier : il télécharge le document publié par Documents.
/// </summary>
public static class DemandesCopieEndpoints
{
    public static void MapDemandesCopie(this RouteGroupBuilder api)
    {
        var demandes = api.MapGroup("/demandes-copie").WithTags("Copie du dossier et carnet de vaccination (travailleur)");

        demandes.MapPost("/", async (ICommandHandler<DeposerDemandeCopie, DemandeCopieDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new DeposerDemandeCopie(), ct)).ToHttpResult(d => Results.Created($"/api/v1/demandes-copie/{d.Id}", d)))
            .RequirePermission(Permissions.CopieDossierDemander)
            .WithName("DeposerDemandeCopie")
            .WithSummary("SAN-43 : le travailleur (claim personne_id) demande la copie de son dossier de santé ; aucun contenu médical.");

        demandes.MapGet("/miennes", async (IQueryHandler<ListerMesDemandesCopie, IReadOnlyList<DemandeCopieDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerMesDemandesCopie(), ct)).ToHttpResult())
            .RequirePermission(Permissions.CopieDossierDemander)
            .WithName("ListerMesDemandesCopie")
            .WithSummary("SAN-43 : suivi de ses demandes par le travailleur (statut, date limite, document publié).");

        demandes.MapGet("/", async (StatutDemandeCopie? statut, IQueryHandler<ListerDemandesCopie, IReadOnlyList<DemandeCopieDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerDemandesCopie(statut), ct)).ToHttpResult())
            .RequirePermission(Permissions.CopieDossierTraiter)
            .WithName("ListerDemandesCopie")
            .WithSummary("SAN-43 : file de travail du CPMT (déposées par défaut), par date limite.");

        demandes.MapPost("/{id:guid}/validation", async (Guid id, ICommandHandler<ValiderDemandeCopie, DemandeCopieDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ValiderDemandeCopie(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.CopieDossierTraiter)
            .WithName("ValiderDemandeCopie")
            .WithSummary("SAN-43 : le CPMT valide l'export ; Documents génère et publie la copie au travailleur (zone médicale).");

        demandes.MapPost("/{id:guid}/refus", async (Guid id, RefusCopieSaisi body, ICommandHandler<RefuserDemandeCopie, DemandeCopieDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new RefuserDemandeCopie(id, body.Motif), ct)).ToHttpResult())
            .RequirePermission(Permissions.CopieDossierTraiter)
            .WithName("RefuserDemandeCopie")
            .WithSummary("SAN-43 : refus pour un motif codé (AucunDossier, IdentiteNonConfirmee).");

        demandes.MapGet("/{id:guid}/contenu", async (Guid id, IQueryHandler<ObtenirContenuCopie, CopieDossierDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirContenuCopie(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteTransmettreDocuments)
            .WithName("ObtenirContenuCopie")
            .WithSummary("SAN-43 : contenu d'une copie validée, pour le seul service Documents (compte technique) ; journalisé comme export.");

        api.MapGet("/vaccinations/{vaccinationId:guid}/carnet", async (Guid vaccinationId, IQueryHandler<ObtenirCarnetVaccination, CarnetVaccinationDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirCarnetVaccination(vaccinationId), ct)).ToHttpResult())
            .RequirePermission(Permissions.DossierSanteTransmettreDocuments)
            .WithTags("Copie du dossier et carnet de vaccination (travailleur)")
            .WithName("ObtenirCarnetVaccination")
            .WithSummary("SAN-50 : carnet de vaccination à jour d'une vaccination, pour le seul service Documents ; journalisé comme export.");
    }
}
