using Sepp.Bff.Employeur.Securite;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Bff.Employeur.Ecrans;

/// <summary>
/// API du portail employeur (§10.1), une route par écran. Toutes les routes exigent le rôle employeur ou SIPP ;
/// celles sous <c>/affilies/{affilieId}</c> sont bornées aux affiliés du claim <c>affilie_id</c> avant tout appel aval.
/// Les erreurs aval sont traduites par <see cref="Aval.ErreursAvalHandler"/>.
/// </summary>
public static class EcransEndpoints
{
    public static IEndpointRouteBuilder MapEcransEmployeur(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(PerimetreEmployeur.Politique).WithTags("Portail employeur");

        api.MapGet("/affilies", async (HttpContext http, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.MesAffiliesAsync(PerimetreEmployeur.Affilies(http.User), ct)))
            .WithName("MesAffilies")
            .WithSummary("Affiliés représentés par l'utilisateur (claim affilie_id), pour le sélecteur du portail (POR-01).");

        api.MapGet("/risques", async (string? langue, HttpContext http, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.RisquesAsync(Langue(http, langue), ct)))
            .WithName("ReferentielRisques")
            .WithSummary("Référentiel des risques, pour le formulaire de proposition (POR-03, AFF-11).");

        var affilie = api.MapGroup("/affilies/{affilieId:guid}").AddEndpointFilter(PerimetreEmployeur.FiltrerAsync);

        affilie.MapGet("/", async (Guid affilieId, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.FicheAsync(affilieId, ct)))
            .WithName("FicheAffilie")
            .WithSummary("Identité, sites et contacts en vigueur de l'affilié (AFF-01 à AFF-03).");

        affilie.MapGet("/tableau-de-bord", async (Guid affilieId, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.TableauDeBordAsync(affilieId, ct)))
            .WithName("TableauDeBord")
            .WithSummary("POR-02 : compteurs disponibles ; les autres sont explicitement « à venir » (aucun chiffre fictif).");

        affilie.MapGet("/travailleurs", async (Guid affilieId, string? recherche, int? page, int? taille, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.TravailleursAsync(affilieId, recherche, page ?? 1, taille ?? 20, ct)))
            .WithName("TravailleursAffilie")
            .WithSummary("POR-03 : travailleurs occupés chez l'affilié, recherche et pagination ; jamais de NISS (DAT-06).");

        affilie.MapGet("/postes", async (Guid affilieId, string? langue, HttpContext http, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.PostesAsync(affilieId, Langue(http, langue), ct)))
            .WithName("PostesEtRisques")
            .WithSummary("POR-03 : catalogue des postes et risques en vigueur (AFF-10, AFF-11).");

        affilie.MapPost("/postes/{posteId:guid}/propositions", async (Guid affilieId, Guid posteId, PropositionPosteCorps corps,
                EcransEmployeur ecrans, CancellationToken ct) =>
            {
                var soumise = await ecrans.ProposerPosteAsync(affilieId, posteId, corps, ct);
                return Results.Created($"/api/v1/affilies/{affilieId}/propositions", soumise);
            })
            .WithName("ProposerModificationPoste")
            .WithSummary("POR-03 : proposition de modification du profil de risques, soumise à la validation du CPMT (AFF-14).");

        affilie.MapGet("/propositions", async (Guid affilieId, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.PropositionsAsync(affilieId, ct)))
            .WithName("PropositionsAffilie")
            .WithSummary("POR-03 : état des propositions soumises (soumise, validée, refusée et motif).");

        affilie.MapGet("/listes-nominatives", async (Guid affilieId, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.ListesNominativesAsync(affilieId, ct)))
            .WithName("ListesNominatives")
            .WithSummary("POR-06 : versions des listes nominatives de l'affilié (AFF-30, AFF-31).");

        affilie.MapGet("/listes-nominatives/{listeId:guid}", async (Guid affilieId, Guid listeId, EcransEmployeur ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.ListeNominativeAsync(affilieId, listeId, ct)))
            .WithName("ListeNominative")
            .WithSummary("Une version de liste avec ses lignes (noms des travailleurs et intitulés des postes).");

        affilie.MapGet("/listes-nominatives/{listeId:guid}/csv", async (Guid affilieId, Guid listeId, string? langue, HttpContext http,
                EcransEmployeur ecrans, CancellationToken ct) =>
            {
                var detail = await ecrans.ListeNominativeAsync(affilieId, listeId, ct);
                return Results.File(ListeNominativeCsv.Generer(detail, http.ResolveLanguage(langue)), ListeNominativeCsv.TypeContenu,
                    ListeNominativeCsv.NomFichier(detail.Liste));
            })
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .WithName("TelechargerListeNominative")
            .WithSummary("POR-06 : téléchargement CSV d'une version de liste nominative (PDF : service Documents, à venir).");

        affilie.MapPost("/listes-nominatives/{listeId:guid}/propositions", async (Guid affilieId, Guid listeId, PropositionListeCorps corps,
                EcransEmployeur ecrans, CancellationToken ct) =>
            {
                var soumise = await ecrans.ProposerListeAsync(affilieId, listeId, corps, ct);
                return Results.Created($"/api/v1/affilies/{affilieId}/propositions", soumise);
            })
            .WithName("ProposerModificationListe")
            .WithSummary("POR-03 : proposition d'ajustement d'une liste nominative, soumise au CPMT (AFF-31).");

        affilie.MapPost("/reprises", async (Guid affilieId, RepriseAnnonceCorps corps, EcransReprises reprises, CancellationToken ct) =>
            {
                var annoncee = await reprises.AnnoncerAsync(affilieId, corps, ct);
                var emplacement = $"/api/v1/affilies/{affilieId}/reprises/{annoncee.RepriseId}";
                return annoncee.Cree ? Results.Created(emplacement, annoncee) : Results.Ok(annoncee);
            })
            .WithName("AnnoncerReprise")
            .WithSummary("POR-04 : annonce d'une reprise du travail, relayée au service Obligations (201 à la création, 200 si déjà connue).");

        affilie.MapGet("/reprises", async (Guid affilieId, EcransReprises reprises, CancellationToken ct) =>
                Results.Ok(await reprises.ListerAsync(affilieId, ct)))
            .WithName("ReprisesAffilie")
            .WithSummary("POR-04 : suivi des reprises annoncées (statut et date limite, sans donnée médicale).");

        affilie.MapGet("/reprises/{repriseId:guid}", async (Guid affilieId, Guid repriseId, EcransReprises reprises, CancellationToken ct) =>
                Results.Ok(await reprises.ObtenirAsync(affilieId, repriseId, ct)))
            .WithName("RepriseAffilie")
            .WithSummary("POR-04 : suivi d'une reprise ; une reprise d'un autre affilié est inconnue (404).");

        return app;
    }

    private static string Langue(HttpContext http, string? langue) => http.ResolveLanguage(langue).ToString().ToLowerInvariant();
}
