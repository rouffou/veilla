using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Ressources;
using Sepp.Planification.Domain.Sessions;

namespace Sepp.Planification.Application.Sessions;

public sealed record SessionProposeeDto(
    int Ordre,
    DateOnly Date,
    Guid LieuId,
    string NomLieu,
    IReadOnlyList<Guid> AffilieIds,
    IReadOnlyList<Guid> PersonneIds,
    IReadOnlyList<Guid> ObligationIds,
    double? DistanceDepuisPrecedentKm,
    bool HorsDelai);

public sealed record PropositionsSessionsDto(IReadOnlyList<SessionProposeeDto> Sessions, double DistanceTotaleKm, int PersonnesSansLieu, string Methode);

/// <summary>
/// PLA-04 : propositions de sessions par affilié ou par zone géographique (préfixe de code postal) à partir des obligations
/// dues, regroupées par site, ordonnées pour limiter les déplacements (<see cref="PlanificateurSessions"/>).
/// Les urgences légales sont exclues : elles passent par les créneaux d'urgence (PLA-06). Le site d'un affilié est son
/// cabinet ou l'emplacement de l'unité mobile chez lui ; à défaut, le lieu de départ (centre) s'il est précisé.
/// </summary>
public sealed record ProposerSessions(Guid? AffilieId, string? Zone, DateOnly Du, DateOnly Au, int CapaciteParSession, Guid? LieuDepartId);

public sealed class ProposerSessionsHandler(
    IObligationRepository obligations,
    ILieuRepository lieux,
    ParametresPlanification parametres,
    ICurrentUser user) : IQueryHandler<ProposerSessions, PropositionsSessionsDto>
{
    public const string Methode =
        "Regroupement par personne puis par site ; ordre de visite du plus proche voisin depuis le lieu de départ ; " +
        "une session par jour ouvrable ; signalement des sessions postérieures à une date limite.";

    public async Task<Result<PropositionsSessionsDto>> HandleAsync(ProposerSessions query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        if ((query.AffilieId is null) == string.IsNullOrWhiteSpace(query.Zone))
        {
            return Error.Validation("propositions.critere", "Précisez soit un affilié, soit une zone géographique (début de code postal).");
        }

        if (query.Au < query.Du || query.Au.DayNumber - query.Du.DayNumber > 366 || query.CapaciteParSession is < 1 or > 200)
        {
            return Error.Validation("propositions.parametres", "Horizon d'au plus 366 jours et capacité par session de 1 à 200.");
        }

        var tousLieux = (await lieux.ListAsync(cancellationToken)).Where(l => l.Actif).ToList();
        Lieu? depart = null;
        if (query.LieuDepartId is { } departId && (depart = tousLieux.FirstOrDefault(l => l.Id == departId)) is null)
        {
            return Error.Validation("lieu.inconnu", "Lieu de départ inconnu.");
        }

        var sitesAffilies = tousLieux
            .Where(l => l.AffilieId is not null && l.Type is TypeLieu.CabinetEntreprise or TypeLieu.UniteMobile)
            .Where(l => query.Zone is null || l.EstDansZone(query.Zone))
            .GroupBy(l => l.AffilieId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Nom, StringComparer.Ordinal).First());
        List<Guid> affilies = query.AffilieId is { } affilieId ? [affilieId] : sitesAffilies.Keys.ToList();
        if (affilies.Count == 0)
        {
            return new PropositionsSessionsDto([], 0, 0, Methode);
        }

        var dues = (await obligations.ListerAPlanifierAsync(affilies, query.Au, cancellationToken))
            .Where(o => !parametres.EstUrgence(o.TypeExamen))
            .ToList();
        var parSite = new Dictionary<Guid, List<BesoinPersonne>>();
        var sansLieu = 0;
        foreach (var personne in dues.GroupBy(o => (o.PersonneId, o.AffilieId)))
        {
            var site = sitesAffilies.GetValueOrDefault(personne.Key.AffilieId) ?? depart;
            if (site is null)
            {
                sansLieu++;
                continue;
            }

            if (!parSite.TryGetValue(site.Id, out var liste))
            {
                parSite[site.Id] = liste = [];
            }

            liste.Add(new BesoinPersonne(personne.Key.PersonneId, personne.Key.AffilieId, personne.Select(o => o.ObligationId).Order().ToList(),
                personne.Select(Echeance).Min()));
        }

        var calendrier = await parametres.CalendrierAsync(query.Du, query.Au, cancellationToken);
        var besoins = parSite.Select(kv => new BesoinSite(kv.Key, tousLieux.First(l => l.Id == kv.Key).Position, kv.Value));
        var propositions = PlanificateurSessions.Proposer(besoins, depart?.Position, query.Du, query.CapaciteParSession, calendrier.IsBusinessDay);
        var noms = tousLieux.ToDictionary(l => l.Id, l => l.Nom);
        return new PropositionsSessionsDto(
            propositions.Select(p => new SessionProposeeDto(p.Ordre, p.Date, p.LieuId, noms[p.LieuId],
                p.Personnes.Select(x => x.AffilieId).Distinct().ToList(),
                p.Personnes.Select(x => x.PersonneId).ToList(),
                p.Personnes.SelectMany(x => x.ObligationIds).ToList(),
                p.DistanceDepuisPrecedentKm is { } d ? Math.Round(d, 1) : null,
                p.HorsDelai)).ToList(),
            Math.Round(PlanificateurSessions.DistanceTotaleKm(propositions), 1),
            sansLieu,
            Methode);
    }

    private static DateOnly Echeance(ObligationAPlanifier o) => o.DateLimite ?? o.DateDue;
}

public sealed record EtapeTourneeDto(int Ordre, string Emplacement, TimeOnly? HeureArrivee, bool RaccordementElectrique, bool RaccordementEau, bool RaccordementReseau);

public sealed record SessionDto(
    Guid Id,
    Guid LieuId,
    Guid? AffilieId,
    DateOnly Date,
    Guid ConseillerId,
    Guid? UniteMobileId,
    Guid? ChauffeurId,
    int CapaciteJournaliere,
    string TypeActe,
    TimeOnly HeureDebut,
    int DureeMinutes,
    StatutSession Statut,
    IReadOnlyList<EtapeTourneeDto> Itineraire);

internal static class SessionMapping
{
    public static SessionDto ToDto(this Session s) => new(
        s.Id, s.LieuId, s.AffilieId, s.Date, s.ConseillerId, s.UniteMobileId, s.ChauffeurId, s.CapaciteJournaliere, s.TypeActe, s.HeureDebut,
        s.DureeMinutes, s.Statut,
        s.Itineraire.OrderBy(e => e.Ordre)
            .Select(e => new EtapeTourneeDto(e.Ordre, e.Emplacement, e.HeureArrivee, e.RaccordementElectrique, e.RaccordementEau, e.RaccordementReseau))
            .ToList());
}

public sealed record NouvelleEtape(string Emplacement, string? HeureArrivee, bool RaccordementElectrique, bool RaccordementEau, bool RaccordementReseau);

/// <summary>
/// PLA-05 : crée une session (tournée d'unité mobile ou journée en cabinet) et ses créneaux : capacité journalière,
/// chauffeur, itinéraire avec emplacements et raccordements. Les créneaux mobilisent le conseiller et, pour une tournée,
/// l'unité mobile : un conflit avec un autre créneau de ces ressources refuse la session.
/// </summary>
public sealed record CreerSession(
    Guid LieuId,
    Guid? AffilieId,
    DateOnly Date,
    Guid ConseillerId,
    Guid? UniteMobileId,
    Guid? ChauffeurId,
    int CapaciteJournaliere,
    string TypeActe,
    string HeureDebut,
    int? DureeMinutes,
    IReadOnlyList<NouvelleEtape>? Itineraire);

public sealed class CreerSessionHandler(
    ISessionRepository sessions,
    ILieuRepository lieux,
    IRessourceRepository ressources,
    ICreneauRepository creneaux,
    IDureeStandardRepository durees,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<CreerSession, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerSession command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        var lieu = await lieux.GetAsync(command.LieuId, cancellationToken);
        if (lieu is null)
        {
            return Error.Validation("lieu.inconnu", "Lieu inconnu.");
        }

        if (lieu.Type == TypeLieu.UniteMobile && command.UniteMobileId is null)
        {
            return Error.Validation("session.unite-mobile", "Une session dans une unité mobile précise l'unité et son chauffeur.");
        }

        if (await Verifier(command.ConseillerId, TypeRessource.Conseiller, TypeRessource.Infirmier, cancellationToken) is { } e1)
        {
            return e1;
        }

        if (command.UniteMobileId is { } unite && await Verifier(unite, TypeRessource.UniteMobile, null, cancellationToken) is { } e2)
        {
            return e2;
        }

        if (command.ChauffeurId is { } chauffeur && await Verifier(chauffeur, TypeRessource.Chauffeur, null, cancellationToken) is { } e3)
        {
            return e3;
        }

        if (!(await parametres.CalendrierAsync(command.Date, command.Date, cancellationToken)).IsBusinessDay(command.Date))
        {
            return Error.Validation("session.jour-non-ouvrable", "Une session se tient un jour ouvrable.");
        }

        if (!TimeOnly.TryParseExact(command.HeureDebut, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var heure))
        {
            return Error.Validation("session.heure-invalide", "Heure de début au format HH:mm.");
        }

        var typeActe = Domaine.Essayer(() => CodeMetier.Normaliser(command.TypeActe, "Type d'acte"));
        if (!typeActe.IsSuccess)
        {
            return typeActe.Error!;
        }

        var duree = command.DureeMinutes ?? DureeStandard.Resoudre(await durees.ListAsync(typeActe.Value, cancellationToken), typeActe.Value, command.ConseillerId);
        if (duree is null)
        {
            return Error.Validation("session.duree-inconnue", $"Aucune durée standard pour {typeActe.Value} : précisez la durée.");
        }

        var session = Domaine.Essayer(() =>
        {
            var s = Session.Creer(command.LieuId, command.AffilieId, command.Date, command.ConseillerId, command.UniteMobileId, command.ChauffeurId,
                command.CapaciteJournaliere, typeActe.Value, heure, duree.Value);
            s.DefinirItineraire((command.Itineraire ?? []).Select(e => (e.Emplacement, Heure(e.HeureArrivee), e.RaccordementElectrique, e.RaccordementEau,
                e.RaccordementReseau)));
            return s;
        });
        if (!session.IsSuccess)
        {
            return session.Error!;
        }

        var associees = new[] { command.UniteMobileId, command.ChauffeurId }.OfType<Guid>().ToList();
        var horaires = session.Value.Horaires().ToList();
        var conflits = await creneaux.ChevauchantsAsync([command.ConseillerId, .. associees], horaires[0].Debut, horaires[^1].Fin, cancellationToken);
        if (conflits.Count > 0)
        {
            return Error.Conflict("session.conflit-ressources",
                $"{conflits.Count} créneau(x) existant(s) occupent déjà le conseiller, l'unité mobile ou le chauffeur ce jour-là.");
        }

        sessions.Add(session.Value);
        foreach (var (debut, fin) in horaires)
        {
            creneaux.Add(Creneau.Creer(command.ConseillerId, command.LieuId, debut, fin, typeActe.Value, false, false, associees, null, session.Value.Id));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session.Value.Id;
    }

    private static TimeOnly? Heure(string? valeur) =>
        TimeOnly.TryParseExact(valeur ?? string.Empty, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : null;

    private async Task<Error?> Verifier(Guid id, TypeRessource type, TypeRessource? autreType, CancellationToken cancellationToken)
    {
        var ressource = await ressources.GetAsync(id, cancellationToken);
        return ressource is { Active: true } && (ressource.Type == type || ressource.Type == autreType)
            ? null
            : Error.Validation("session.ressource-invalide", $"La ressource {id} n'est pas un(e) {type} actif(ve).");
    }
}

public sealed record ListerSessions(DateOnly Du, DateOnly Au);

public sealed class ListerSessionsHandler(ISessionRepository sessions, ICurrentUser user) : IQueryHandler<ListerSessions, IReadOnlyList<SessionDto>>
{
    public async Task<Result<IReadOnlyList<SessionDto>>> HandleAsync(ListerSessions query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        return (await sessions.ListAsync(query.Du, query.Au, cancellationToken)).OrderBy(s => s.Date).Select(s => s.ToDto()).ToList();
    }
}

public sealed record ObtenirSession(Guid Id);

public sealed class ObtenirSessionHandler(ISessionRepository sessions, ICurrentUser user) : IQueryHandler<ObtenirSession, SessionDto>
{
    public async Task<Result<SessionDto>> HandleAsync(ObtenirSession query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        var session = await sessions.GetAsync(query.Id, cancellationToken);
        return session is null ? Error.NotFound("session.inconnue", "Session inconnue.") : session.ToDto();
    }
}

public sealed record SessionPlanifieeDto(Guid? LotId, int RendezVous, int PersonnesNonPlacees, int CreneauxRestants);

/// <summary>
/// PLA-04, SAN-03, SAN-10 : remplit les créneaux libres d'une session avec les personnes de l'affilié dont des obligations
/// non urgentes sont dues dans la fenêtre de regroupement (un rendez-vous couvre toutes leurs obligations), échéances les
/// plus proches d'abord, et émet les convocations par lot.
/// </summary>
public sealed record PlanifierSession(Guid SessionId, bool Convoquer, string? Canal);

public sealed class PlanifierSessionHandler(
    ISessionRepository sessions,
    ICreneauRepository creneaux,
    IObligationRepository obligations,
    PriseDeRendezVous prise,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<PlanifierSession, SessionPlanifieeDto>
{
    public async Task<Result<SessionPlanifieeDto>> HandleAsync(PlanifierSession command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        var canal = Enumerations.Optionnel<CanalConvocation>(command.Canal, "Canal");
        if (!canal.IsSuccess)
        {
            return canal.Error!;
        }

        var session = await sessions.GetAsync(command.SessionId, cancellationToken);
        if (session is null)
        {
            return Error.NotFound("session.inconnue", "Session inconnue.");
        }

        if (session is not { Statut: StatutSession.Prevue, AffilieId: { } affilieId })
        {
            return Error.Validation("session.sans-affilie", "Seule une session prévue chez un affilié se remplit automatiquement.");
        }

        var libres = (await creneaux.RechercherAsync(
                new CritereCreneaux(HeureBelge.VersUtc(session.Date, TimeOnly.MinValue), HeureBelge.VersUtc(session.Date.AddDays(1), TimeOnly.MinValue))
                {
                    SessionId = session.Id,
                    Statut = StatutCreneau.Libre,
                },
                cancellationToken))
            .OrderBy(c => c.Debut)
            .ToList();

        var personnes = (await obligations.ListerAPlanifierAsync([affilieId], session.Date.AddDays(parametres.Options.FenetreRegroupementJours), cancellationToken))
            .Where(o => !parametres.EstUrgence(o.TypeExamen))
            .GroupBy(o => o.PersonneId)
            .OrderBy(g => g.Min(o => o.DateLimite ?? o.DateDue))
            .ThenBy(g => g.Key)
            .ToList();

        var lotId = command.Convoquer ? Guid.CreateVersion7() : (Guid?)null;
        var planifies = 0;
        var nonPlacees = 0;
        foreach (var personne in personnes)
        {
            Creneau? retenu = null;
            foreach (var creneau in libres)
            {
                if (!await prise.ChevaucheRendezVousDeLaPersonneAsync(personne.Key, creneau.Debut, creneau.Fin, null, cancellationToken))
                {
                    retenu = creneau;
                    break;
                }
            }

            if (retenu is null)
            {
                nonPlacees++;
                continue;
            }

            var rdv = await prise.PlanifierAsync(retenu,
                new DemandeRendezVous(personne.Key, affilieId, personne.Select(o => o.ObligationId).ToList(), OrigineRendezVous.Session, false)
                {
                    Convoquer = command.Convoquer,
                    Canal = canal.Value,
                    LotId = lotId,
                },
                cancellationToken);
            if (rdv.IsSuccess)
            {
                libres.Remove(retenu);
                planifies++;
            }
            else
            {
                nonPlacees++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SessionPlanifieeDto(planifies > 0 ? lotId : null, planifies, nonPlacees, libres.Count);
    }
}
