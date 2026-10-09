using Sepp.Bff.Travailleur.Securite;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Bff.Travailleur.Ecrans;

/// <summary>
/// API du portail travailleur (§10.2), une route par écran. Toutes les routes exigent le rôle travailleur et un claim
/// <c>personne_id</c> valide (sinon 403 sans appel aval) : aucune route ne reçoit d'identifiant de personne, celui-ci vient
/// toujours du jeton. Les erreurs aval sont traduites par <see cref="Aval.ErreursAvalHandler"/>.
/// </summary>
public static class EcransEndpoints
{
    public static IEndpointRouteBuilder MapEcransTravailleur(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization(PerimetreTravailleur.Politique)
            .AddEndpointFilter(PerimetreTravailleur.FiltrerAsync)
            .WithTags("Portail travailleur");

        api.MapGet("/accueil", async (string? langue, HttpContext http, EcransAccueil accueil, CancellationToken ct) =>
                Results.Ok(await accueil.AccueilAsync(PerimetreTravailleur.PersonneObligatoire(http), Langue(http, langue), ct)))
            .WithName("Accueil")
            .WithSummary("Accueil composite : prochains rendez-vous, documents récents, questionnaires disponibles (POR-11 à POR-13).");

        MapRendezVous(api);
        MapQuestionnaires(api);
        MapDocuments(api);
        return app;
    }

    private static void MapRendezVous(RouteGroupBuilder api)
    {
        api.MapGet("/rendez-vous", async (HttpContext http, EcransRendezVous ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.MesRendezVousAsync(PerimetreTravailleur.PersonneObligatoire(http), ct)))
            .WithName("MesRendezVous")
            .WithSummary("POR-11 : rendez-vous du travailleur (les siens uniquement).");

        api.MapGet("/rendez-vous/creneaux", async (Guid affilieId, string typeActe, DateTimeOffset du, DateTimeOffset au, Guid? lieuId,
                EcransRendezVous ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.CreneauxAsync(affilieId, typeActe, du, au, lieuId, ct)))
            .WithName("CreneauxOuverts")
            .WithSummary("POR-11 : créneaux ouverts à la réservation en ligne (SAN-12) ; le service limite la période à 93 jours.");

        api.MapPost("/rendez-vous", async (ReservationCorps corps, HttpContext http, EcransRendezVous ecrans, CancellationToken ct) =>
            {
                var reserve = await ecrans.ReserverAsync(PerimetreTravailleur.PersonneObligatoire(http), corps, ct);
                return Results.Created($"/api/v1/rendez-vous#{reserve.Id}", reserve);
            })
            .WithName("ReserverRendezVous")
            .WithSummary("POR-11 : réservation d'un créneau ouvert pour soi-même ; jamais rejouée automatiquement.");

        api.MapPost("/rendez-vous/{rendezVousId:guid}/annulation", async (Guid rendezVousId, EcransRendezVous ecrans, CancellationToken ct) =>
            {
                await ecrans.AnnulerAsync(rendezVousId, ct);
                return Results.NoContent();
            })
            .WithName("AnnulerRendezVous")
            .WithSummary("POR-11 : annulation en ligne, au plus tard 24 heures avant le rendez-vous (règle du service Planification).");
    }

    private static void MapQuestionnaires(RouteGroupBuilder api)
    {
        api.MapGet("/questionnaires", async (string? langue, HttpContext http, EcransQuestionnaires ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.ModelesAsync(Langue(http, langue), ct)))
            .WithName("ModelesQuestionnaire")
            .WithSummary("POR-12 : modèles de questionnaire de santé à remplir (SAN-22) ; aucune réponse déjà enregistrée n'est relue.");

        api.MapPost("/questionnaires/{modeleCode}/reponses", async (string modeleCode, QuestionnaireReponsesCorps corps, HttpContext http,
                EcransQuestionnaires ecrans, CancellationToken ct) =>
            {
                var enregistre = await ecrans.RemplirAsync(PerimetreTravailleur.PersonneObligatoire(http), modeleCode, corps, ct);
                return Results.Created($"/api/v1/questionnaires/{Uri.EscapeDataString(modeleCode)}", enregistre);
            })
            .WithName("RemplirQuestionnaire")
            .WithSummary("POR-12 : questionnaire de santé rempli à l'avance, en écriture seule (SAN-22) ; jamais rejoué automatiquement.");

        api.MapPost("/demandes", async (DemandeCorps corps, HttpContext http, EcransQuestionnaires ecrans, CancellationToken ct) =>
            {
                var enregistree = await ecrans.DemanderAsync(PerimetreTravailleur.PersonneObligatoire(http), corps, ct);
                return Results.Created("/api/v1/demandes", enregistree);
            })
            .WithName("DemanderConsultation")
            .WithSummary("POR-12 : consultation spontanée ou visite de pré-reprise, sans passer par l'employeur (§5.1).");
    }

    private static void MapDocuments(RouteGroupBuilder api)
    {
        api.MapGet("/documents", async (HttpContext http, EcransDocuments ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.MesDocumentsAsync(PerimetreTravailleur.PersonneObligatoire(http), ct)))
            .WithName("MesDocuments")
            .WithSummary("POR-13 : documents publiés pour le travailleur (formulaire d'évaluation de santé, exemplaire du travailleur).");

        api.MapGet("/documents/{documentId:guid}/contenu", async (Guid documentId, HttpContext http, EcransDocuments ecrans, CancellationToken ct) =>
            {
                var (contenu, nom) = await ecrans.ContenuAsync(PerimetreTravailleur.PersonneObligatoire(http), documentId, ct);
                return Results.File(contenu, "application/pdf", nom);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .WithName("TelechargerDocument")
            .WithSummary("POR-13 : PDF d'un document du travailleur ; la lecture est journalisée par le service Documents (NF-04).");
    }

    private static string Langue(HttpContext http, string? langue) => http.ResolveLanguage(langue).ToString().ToLowerInvariant();
}
