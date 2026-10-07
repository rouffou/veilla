using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Planification.Application.Agenda;
using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Application.Replanification;
using Sepp.Planification.Application.Reservations;
using Sepp.Planification.Application.Ressources;
using Sepp.Planification.Application.SalleAttente;
using Sepp.Planification.Application.Sessions;
using Sepp.Planification.Application.Synchronisation;
using Sepp.Planification.Application.Urgences;

namespace Sepp.Planification.Adapters.Api;

/// <summary>
/// API REST du service Planification (§8 PLA-01 à PLA-09, §5.3 SAN-10 à SAN-13). Chaque route exige la permission de la
/// matrice §3.3 ; les cas d'usage la revérifient (défense en profondeur) et bornent les externes à leur périmètre
/// (claims <c>affilie_id</c> et <c>personne_id</c>). Aucune réponse ne contient de donnée de santé.
/// </summary>
public static class PlanificationEndpoints
{
    public sealed record CorpsCompetences(IReadOnlyList<string> Competences);

    public sealed record CorpsAgendaExterne(string Fournisseur, string Compte);

    public sealed record CorpsPeriode(DateOnly Du, DateOnly Au);

    public sealed record CorpsAnnulation(string Motif);

    public sealed record CorpsReconvocation(Guid? CreneauId, string? Canal);

    public sealed record CorpsCanal(string Canal);

    public sealed record CorpsAppel(Guid? SalleId);

    public sealed record CorpsRappels(DateOnly? Date);

    public sealed record CorpsPlanificationSession(bool Convoquer = true, string? Canal = null);

    public sealed record CorpsAbsenceRessource(DateTimeOffset Debut, DateTimeOffset Fin);

    public static IEndpointRouteBuilder MapPlanificationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        MapRessources(api);
        MapAgenda(api);
        MapSessions(api);
        MapRendezVous(api);
        MapSalleAttente(api);
        MapReservationEnLigne(api.MapGroup("/reservations").WithTags("Réservation en ligne"));
        return app;
    }

    private static void MapRessources(RouteGroupBuilder api)
    {
        var lieux = api.MapGroup("/lieux").WithTags("Lieux (PLA-02)");
        lieux.MapGet("/", async (IQueryHandler<ListerLieux, IReadOnlyList<LieuDto>> h, CancellationToken ct) => (await h.HandleAsync(new ListerLieux(), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ListerLieux");
        lieux.MapPost("/", async (CreerLieu body, ICommandHandler<CreerLieu, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/lieux/{id}", new { id })))
            .RequirePermission(Permissions.PlanificationRessources).WithName("CreerLieu")
            .WithSummary("PLA-02 : déclare un lieu (centre, cabinet en entreprise, unité mobile, distance).");

        var ressources = api.MapGroup("/ressources").WithTags("Ressources (PLA-01)");
        ressources.MapGet("/", async (string? type, IQueryHandler<ListerRessources, IReadOnlyList<RessourceDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerRessources(type), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ListerRessources");
        ressources.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirRessource, RessourceDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirRessource(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ObtenirRessource");
        ressources.MapPost("/", async (CreerRessource body, ICommandHandler<CreerRessource, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/ressources/{id}", new { id })))
            .RequirePermission(Permissions.PlanificationRessources).WithName("CreerRessource")
            .WithSummary("PLA-01 : déclare une ressource (conseiller, infirmier, salle, appareil, unité mobile…) et ses compétences.");
        ressources.MapPut("/{id:guid}/competences", async (Guid id, CorpsCompetences body, ICommandHandler<DefinirCompetences, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new DefinirCompetences(id, body.Competences), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationRessources).WithName("DefinirCompetences");
        ressources.MapPut("/{id:guid}/agenda-externe", async (Guid id, CorpsAgendaExterne body, ICommandHandler<RattacherAgendaExterne, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new RattacherAgendaExterne(id, body.Fournisseur, body.Compte), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationRessources).WithName("RattacherAgendaExterne")
            .WithSummary("PLA-09 : rattache l'agenda Microsoft 365 ou Google d'une ressource humaine.");

        api.MapGet("/absences", async (Guid ressourceId, DateTimeOffset du, DateTimeOffset au, IQueryHandler<ListerAbsences, IReadOnlyList<AbsenceDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerAbsences(ressourceId, du, au), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithTags("Ressources (PLA-01)").WithName("ListerAbsences");
        api.MapPost("/conges/imports", async (CorpsPeriode body, ICommandHandler<ImporterConges, ImportCongesDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ImporterConges(body.Du, body.Au), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationRessources).WithTags("Ressources (PLA-01)").WithName("ImporterConges")
            .WithSummary("PLA-03 : import des congés de l'outil RH (lecture seule, idempotent) ; bloque les créneaux libres couverts.");
        api.MapPost("/synchronisations-agenda", async (CorpsPeriode body, ICommandHandler<SynchroniserAgendas, SynchronisationDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new SynchroniserAgendas(body.Du, body.Au), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationRessources).WithTags("Ressources (PLA-01)").WithName("SynchroniserAgendas")
            .WithSummary("PLA-09 : écrit les rendez-vous dans les agendas externes (intitulé neutre) et lit leurs occupations.");
    }

    private static void MapAgenda(RouteGroupBuilder api)
    {
        var agenda = api.MapGroup(string.Empty).WithTags("Modèles d'agenda et créneaux (PLA-03)");
        agenda.MapGet("/durees-standard", async (IQueryHandler<ListerDureesStandard, IReadOnlyList<DureeStandardDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerDureesStandard(), ct)).ToHttpResult())
            .WithName("ListerDureesStandard");
        agenda.MapPut("/durees-standard", async (DefinirDureeStandard body, ICommandHandler<DefinirDureeStandard, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Ok(new { id })))
            .RequirePermission(Permissions.PlanificationModelesAgenda).WithName("DefinirDureeStandard")
            .WithSummary("PLA-03 : durée standard d'un type d'acte, globale ou propre à un CPMT (la durée du CPMT l'emporte).");
        agenda.MapGet("/modeles-agenda", async (Guid? ressourceId, IQueryHandler<ListerModelesAgenda, IReadOnlyList<ModeleAgendaDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerModelesAgenda(ressourceId), ct)).ToHttpResult())
            .WithName("ListerModelesAgenda");
        agenda.MapPost("/modeles-agenda", async (CreerModeleAgenda body, ICommandHandler<CreerModeleAgenda, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/modeles-agenda/{id}", new { id })))
            .RequirePermission(Permissions.PlanificationModelesAgenda).WithName("CreerModeleAgenda")
            .WithSummary("PLA-03 : modèle d'agenda (plages par type d'acte, jours de présence) ; clôture le modèle précédent (DAT-04).");
        agenda.MapPost("/modeles-agenda/{id:guid}/creneaux", async (Guid id, CorpsPeriode body, ICommandHandler<GenererCreneaux, GenerationCreneauxDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new GenererCreneaux(id, body.Du, body.Au), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("GenererCreneaux")
            .WithSummary("PLA-03 : génère les créneaux du modèle sur une période (jours ouvrables, idempotent, conflits signalés).");
        agenda.MapGet("/creneaux", async (DateTimeOffset du, DateTimeOffset au, Guid? ressourceId, Guid? lieuId, string? statut,
                IQueryHandler<ListerCreneaux, IReadOnlyList<CreneauDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerCreneaux(du, au, ressourceId, lieuId, statut), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ListerCreneaux");
        agenda.MapDelete("/creneaux/{id:guid}", async (Guid id, ICommandHandler<RetirerCreneau, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new RetirerCreneau(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("RetirerCreneau");
        agenda.MapPost("/replanifications", async (Guid ressourceId, CorpsAbsenceRessource body, ICommandHandler<ReplanifierAbsence, ReplanificationDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ReplanifierAbsence(ressourceId, body.Debut, body.Fin), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("ReplanifierAbsence")
            .WithSummary("PLA-07 : déclare l'absence d'une ressource et replanifie en masse ses rendez-vous (personnes notifiées).");
    }

    private static void MapSessions(RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/sessions").WithTags("Sessions et tournées (PLA-04, PLA-05)");
        sessions.MapGet("/propositions", async (Guid? affilieId, string? zone, DateOnly du, DateOnly au, int? capacite, Guid? lieuDepartId,
                IQueryHandler<ProposerSessions, PropositionsSessionsDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ProposerSessions(affilieId, zone, du, au, capacite ?? 20, lieuDepartId), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("ProposerSessions")
            .WithSummary("PLA-04 : sessions proposées à partir des obligations dues (heuristique documentée dans la réponse).");
        sessions.MapGet("/", async (DateOnly du, DateOnly au, IQueryHandler<ListerSessions, IReadOnlyList<SessionDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerSessions(du, au), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ListerSessions");
        sessions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirSession, SessionDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirSession(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ObtenirSession");
        sessions.MapPost("/", async (CreerSession body, ICommandHandler<CreerSession, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/sessions/{id}", new { id })))
            .RequirePermission(Permissions.PlanificationGerer).WithName("CreerSession")
            .WithSummary("PLA-05 : session ou tournée d'unité mobile (capacité, chauffeur, emplacements, raccordements) et ses créneaux.");
        sessions.MapPost("/{id:guid}/planification", async (Guid id, CorpsPlanificationSession? body, ICommandHandler<PlanifierSession, SessionPlanifieeDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new PlanifierSession(id, body?.Convoquer ?? true, body?.Canal), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("PlanifierSession")
            .WithSummary("PLA-04, SAN-10 : remplit la session avec les personnes de l'affilié dont des obligations sont dues et convoque par lot.");
    }

    private static void MapRendezVous(RouteGroupBuilder api)
    {
        var rdv = api.MapGroup("/rendez-vous").WithTags("Rendez-vous et convocations (SAN-10, SAN-11, SAN-13)");
        rdv.MapGet("/", async (Guid? personneId, Guid? affilieId, Guid? ressourceId, Guid? lieuId, DateTimeOffset? du, DateTimeOffset? au,
                IQueryHandler<ListerRendezVous, IReadOnlyList<RendezVousDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerRendezVous(personneId, affilieId, ressourceId, lieuId, du, au), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ListerRendezVous");
        rdv.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirRendezVous, RendezVousDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ObtenirRendezVous(id), ct)).ToHttpResult())
            .WithName("ObtenirRendezVous");
        rdv.MapPost("/", async (PlanifierRendezVous body, ICommandHandler<PlanifierRendezVous, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/rendez-vous/{id}", new { id })))
            .RequirePermission(Permissions.PlanificationGerer).WithName("PlanifierRendezVous")
            .WithSummary("SAN-10 : rendez-vous dans un créneau (une ou plusieurs obligations) avec convocation, recommandée si exigé (SAN-11).");
        rdv.MapPost("/{id:guid}/annulation", async (Guid id, CorpsAnnulation body, ICommandHandler<AnnulerRendezVous, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new AnnulerRendezVous(id, body.Motif), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("AnnulerRendezVous");
        rdv.MapPost("/{id:guid}/absence", async (Guid id, ICommandHandler<ConstaterAbsence, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new ConstaterAbsence(id), ct)).ToHttpResult())
            .WithName("ConstaterAbsence")
            .WithSummary("SAN-13 : la personne ne s'est pas présentée ; ses obligations redeviennent à planifier.");
        rdv.MapPost("/{id:guid}/reconvocation", async (Guid id, CorpsReconvocation? body, ICommandHandler<Reconvoquer, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(new Reconvoquer(id, body?.CreneauId, body?.Canal), ct)).ToHttpResult(nouveau => Results.Created($"/api/v1/rendez-vous/{nouveau}", new { id = nouveau })))
            .RequirePermission(Permissions.PlanificationGerer).WithName("Reconvoquer")
            .WithSummary("SAN-13 : reconvoque après une absence, dans le créneau indiqué ou le premier créneau libre.");

        api.MapPost("/urgences", async (ReserverUrgence body, ICommandHandler<ReserverUrgence, UrgenceDto> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithTags("Urgences (PLA-06)").WithName("ReserverUrgence")
            .WithSummary("PLA-06 : créneau d'urgence avant l'échéance légale (10 jours ouvrables par défaut, jours fériés exclus).");

        var convocations = api.MapGroup("/convocations").WithTags("Rendez-vous et convocations (SAN-10, SAN-11, SAN-13)");
        convocations.MapGet("/", async (Guid rendezVousId, IQueryHandler<ListerConvocations, IReadOnlyList<ConvocationDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerConvocations(rendezVousId), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationLire).WithName("ListerConvocations");
        convocations.MapPost("/lots", async (ConvoquerParLot body, ICommandHandler<ConvoquerParLot, LotConvocationsDto> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("ConvoquerParLot")
            .WithSummary("SAN-10 : convocation par lot des rendez-vous qui n'ont pas encore été convoqués.");
        convocations.MapPost("/rappels", async (CorpsRappels? body, ICommandHandler<EmettreRappels, RappelsDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new EmettreRappels(body?.Date), ct)).ToHttpResult())
            .RequirePermission(Permissions.PlanificationGerer).WithName("EmettreRappels")
            .WithSummary("SAN-13 : émet les rappels J-7 et J-1 dus (idempotent) ; exécuté chaque jour par la tâche planifiée.");
        api.MapPut("/affilies/{affilieId:guid}/canal-convocation", async (Guid affilieId, CorpsCanal body, ICommandHandler<DefinirPreferenceConvocation, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new DefinirPreferenceConvocation(affilieId, body.Canal), ct)).ToHttpResult())
            .WithTags("Rendez-vous et convocations (SAN-10, SAN-11, SAN-13)").WithName("DefinirCanalConvocation")
            .WithSummary("SAN-10 : canal de convocation préféré d'un affilié (planificateur ou employeur de cet affilié).");
    }

    private static void MapSalleAttente(RouteGroupBuilder api)
    {
        var salle = api.MapGroup("/salle-attente").WithTags("Salle d'attente (PLA-08)").RequirePermission(Permissions.PlanificationSalleAttente);
        salle.MapGet("/{lieuId:guid}", async (Guid lieuId, DateOnly? date, IQueryHandler<ConsulterSalleAttente, SalleAttenteDto> h, CancellationToken ct) =>
                (await h.HandleAsync(new ConsulterSalleAttente(lieuId, date), ct)).ToHttpResult())
            .WithName("ConsulterSalleAttente");
        salle.MapPost("/rendez-vous/{id:guid}/arrivee", async (Guid id, ICommandHandler<EnregistrerArrivee, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new EnregistrerArrivee(id), ct)).ToHttpResult())
            .WithName("EnregistrerArrivee");
        salle.MapPost("/rendez-vous/{id:guid}/appel", async (Guid id, CorpsAppel? body, ICommandHandler<AppelerEnSalle, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new AppelerEnSalle(id, body?.SalleId), ct)).ToHttpResult())
            .WithName("AppelerEnSalle");
        salle.MapPost("/rendez-vous/{id:guid}/fin", async (Guid id, ICommandHandler<TerminerRendezVous, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new TerminerRendezVous(id), ct)).ToHttpResult())
            .WithName("TerminerRendezVous");
    }

    private static void MapReservationEnLigne(RouteGroupBuilder reservations)
    {
        // Routes réservées aux externes (employeur : claim affilie_id ; travailleur : claim personne_id) : permission
        // « planification:reserver », périmètre revérifié par chaque cas d'usage.
        reservations.RequirePermission(Permissions.PlanificationReserver);
        reservations.MapGet("/creneaux", async (Guid affilieId, string typeActe, DateTimeOffset du, DateTimeOffset au, Guid? lieuId,
                IQueryHandler<ListerCreneauxOuverts, IReadOnlyList<CreneauOuvertDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerCreneauxOuverts(affilieId, typeActe, du, au, lieuId), ct)).ToHttpResult())
            .WithName("ListerCreneauxOuverts")
            .WithSummary("SAN-12 : créneaux ouverts à la réservation en ligne (sans ressource ni information interne).");
        reservations.MapPost("/", async (ReserverEnLigne body, ICommandHandler<ReserverEnLigne, Guid> h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/rendez-vous/{id}", new { id })))
            .WithName("ReserverEnLigne")
            .WithSummary("SAN-12 : réservation en ligne ; le rendez-vous couvre les obligations dues de la personne pour ce type d'acte.");
        reservations.MapGet("/rendez-vous", async (Guid? affilieId, IQueryHandler<ListerMesRendezVous, IReadOnlyList<RendezVousDto>> h, CancellationToken ct) =>
                (await h.HandleAsync(new ListerMesRendezVous(affilieId), ct)).ToHttpResult())
            .WithName("ListerMesRendezVous");
        reservations.MapPost("/rendez-vous/{id:guid}/annulation", async (Guid id, ICommandHandler<AnnulerEnLigne, Unit> h, CancellationToken ct) =>
                (await h.HandleAsync(new AnnulerEnLigne(id), ct)).ToHttpResult())
            .WithName("AnnulerEnLigne")
            .WithSummary("SAN-12 : annulation en ligne jusqu'à 24 heures avant le rendez-vous.");
    }
}
