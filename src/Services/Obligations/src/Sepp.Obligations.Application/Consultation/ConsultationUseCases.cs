using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Application.Consultation;

/// <summary>Obligations d'un travailleur (SAN-01) ; un profil externe ne voit que son affilié, sans les types confidentiels.</summary>
public sealed record ListerObligationsPersonne(Guid PersonneId, bool OuvertesSeulement, StatutObligation? Statut, Language Langue);

public sealed class ListerObligationsPersonneHandler(
    IObligationRepository obligations, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<ListerObligationsPersonne, IReadOnlyList<ObligationDto>>
{
    public async Task<Result<IReadOnlyList<ObligationDto>>> HandleAsync(ListerObligationsPersonne query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var aujourdHui = clock.AujourdHui();
        return (await obligations.ListParPersonneAsync(query.PersonneId, cancellationToken))
            .Where(o => Acces.PeutVoir(perimetre, o) && (!query.OuvertesSeulement || o.EstOuverte) && (query.Statut is null || o.Statut == query.Statut))
            .OrderBy(o => o.Echeance)
            .ThenBy(o => o.Cle, StringComparer.Ordinal)
            .Select(o => ObligationDto.De(o, aujourdHui, options.HorizonDuesJours, query.Langue))
            .ToList();
    }
}

/// <summary>Obligations d'un affilié, éventuellement d'une catégorie du tableau de bord (POR-02).</summary>
public sealed record ListerObligationsAffilie(
    Guid AffilieId, CategorieObligation? Categorie, TypeObligation? Type, bool OuvertesSeulement, Language Langue);

public sealed class ListerObligationsAffilieHandler(
    IObligationRepository obligations, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<ListerObligationsAffilie, IReadOnlyList<ObligationDto>>
{
    public async Task<Result<IReadOnlyList<ObligationDto>>> HandleAsync(ListerObligationsAffilie query, CancellationToken cancellationToken)
    {
        if (Acces.Affilie(user, perimetre, query.AffilieId, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var aujourdHui = clock.AujourdHui();
        var ouvertesSeulement = query.OuvertesSeulement || query.Categorie is not null;
        return (await obligations.ListParAffilieAsync(query.AffilieId, ouvertesSeulement, cancellationToken))
            .Where(o => Acces.PeutVoir(perimetre, o)
                        && (query.Type is null || o.Type == query.Type)
                        && (query.Categorie is null || Classement.Classer(o, aujourdHui, options.HorizonDuesJours) == query.Categorie))
            .OrderBy(o => o.Echeance)
            .ThenBy(o => o.PersonneId)
            .ThenBy(o => o.Cle, StringComparer.Ordinal)
            .Select(o => ObligationDto.De(o, aujourdHui, options.HorizonDuesJours, query.Langue))
            .ToList();
    }
}

/// <summary>Décomptes dues / en retard / planifiées d'un affilié pour le tableau de bord employeur (POR-02).</summary>
public sealed record SynthetiserObligationsAffilie(Guid AffilieId);

public sealed class SynthetiserObligationsAffilieHandler(
    IObligationRepository obligations, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<SynthetiserObligationsAffilie, SyntheseObligationsDto>
{
    public async Task<Result<SyntheseObligationsDto>> HandleAsync(SynthetiserObligationsAffilie query, CancellationToken cancellationToken)
    {
        if (Acces.Affilie(user, perimetre, query.AffilieId, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var aujourdHui = clock.AujourdHui();
        var ouvertes = (await obligations.ListParAffilieAsync(query.AffilieId, true, cancellationToken))
            .Where(o => Acces.PeutVoir(perimetre, o))
            .Select(o => (Obligation: o, Categorie: Classement.Classer(o, aujourdHui, options.HorizonDuesJours)))
            .ToList();
        int Nombre(CategorieObligation categorie) => ouvertes.Count(o => o.Categorie == categorie);

        return new SyntheseObligationsDto(
            query.AffilieId,
            aujourdHui,
            Nombre(CategorieObligation.EnRetard),
            Nombre(CategorieObligation.Planifiee),
            Nombre(CategorieObligation.Due),
            Nombre(CategorieObligation.AVenir),
            ouvertes.GroupBy(o => o.Obligation.Type.Code()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()));
    }
}

public sealed record ObtenirObligation(Guid Id, Language Langue);

public sealed class ObtenirObligationHandler(
    IObligationRepository obligations, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<ObtenirObligation, ObligationDto>
{
    public async Task<Result<ObligationDto>> HandleAsync(ObtenirObligation query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var obligation = await obligations.GetAsync(query.Id, cancellationToken);
        return obligation is null || !Acces.PeutVoir(perimetre, obligation)
            ? Acces.ObligationInconnue(query.Id)
            : ObligationDto.De(obligation, clock.AujourdHui(), options.HorizonDuesJours, query.Langue);
    }
}

/// <summary>SAN-04 : trace de calcul consultable, du plus récent au plus ancien (pourquoi l'obligation existe, d'où vient sa date).</summary>
public sealed record ObtenirTraceCalcul(Guid ObligationId, Language Langue);

public sealed class ObtenirTraceCalculHandler(
    IObligationRepository obligations, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<ObtenirTraceCalcul, TraceObligationDto>
{
    public async Task<Result<TraceObligationDto>> HandleAsync(ObtenirTraceCalcul query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var obligation = await obligations.GetAsync(query.ObligationId, cancellationToken);
        if (obligation is null || !Acces.PeutVoir(perimetre, obligation))
        {
            return Acces.ObligationInconnue(query.ObligationId);
        }

        var traces = obligation.Traces
            .OrderByDescending(t => t.Numero)
            .Select(t => new TraceCalculDto(t.Numero,
                t.DateCalcul, t.Regle, t.RegleVersion, t.Explication, t.EntreesStructurees().Select(e => new EntreeCalculDto(e.Key, e.Value)).ToList()))
            .ToList();
        return new TraceObligationDto(ObligationDto.De(obligation, clock.AujourdHui(), options.HorizonDuesJours, query.Langue), traces);
    }
}

/// <summary>
/// SAN-03 : obligations regroupables d'un affilié — un seul rendez-vous par travailleur quand plusieurs échéances
/// tombent dans la fenêtre (paramétrable ; <see cref="OptionsObligations.FenetreRegroupementJours"/> par défaut).
/// </summary>
public sealed record ListerRegroupables(Guid AffilieId, int? FenetreJours, Language Langue);

public sealed class ListerRegroupablesHandler(
    IObligationRepository obligations, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock, OptionsObligations options)
    : IQueryHandler<ListerRegroupables, IReadOnlyList<PropositionRendezVousDto>>
{
    public async Task<Result<IReadOnlyList<PropositionRendezVousDto>>> HandleAsync(ListerRegroupables query, CancellationToken cancellationToken)
    {
        if (Acces.Affilie(user, perimetre, query.AffilieId, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var fenetre = query.FenetreJours ?? options.FenetreRegroupementJours;
        if (fenetre is < 0 or > Regroupement.FenetreMaximaleJours)
        {
            return Error.Validation("regroupement.fenetre-invalide", $"La fenêtre de regroupement doit être comprise entre 0 et {Regroupement.FenetreMaximaleJours} jours.");
        }

        var aujourdHui = clock.AujourdHui();
        var visibles = (await obligations.ListParAffilieAsync(query.AffilieId, true, cancellationToken)).Where(o => Acces.PeutVoir(perimetre, o));
        return Regroupement.Proposer(visibles, aujourdHui, fenetre)
            .Select(p => new PropositionRendezVousDto(
                p.PersonneId,
                p.AffilieId,
                p.DateProposee,
                p.DateAuPlusTard,
                p.EstRegroupement,
                p.Obligations.Select(o => ObligationDto.De(o, aujourdHui, options.HorizonDuesJours, query.Langue)).ToList()))
            .ToList();
    }
}

/// <summary>AFF-32 : alertes de non-couverture d'un affilié (travailleur exposé non planifié, poste sans analyse, liste non revue).</summary>
public sealed record ListerAlertes(Guid AffilieId);

public sealed class ListerAlertesHandler(
    IObligationRepository obligations,
    IProjectionRepository projections,
    RecalculObligations recalcul,
    IPerimetreAffilies perimetre,
    ICurrentUser user,
    TimeProvider clock,
    OptionsObligations options,
    IProcessusRepriseRepository reprises) : IQueryHandler<ListerAlertes, IReadOnlyList<AlerteDto>>
{
    public async Task<Result<IReadOnlyList<AlerteDto>>> HandleAsync(ListerAlertes query, CancellationToken cancellationToken)
    {
        if (Acces.Affilie(user, perimetre, query.AffilieId, Permissions.ObligationLire) is { } interdit)
        {
            return interdit;
        }

        var aujourdHui = clock.AujourdHui();
        var alertes = new List<Alerte>();

        var ouvertes = (await obligations.ListParAffilieAsync(query.AffilieId, true, cancellationToken)).Where(o => Acces.PeutVoir(perimetre, o)).ToList();
        alertes.AddRange(DetectionAlertes.TravailleursSansSurveillancePlanifiee(query.AffilieId, ouvertes, aujourdHui, options.HorizonAlertesJours));

        var occupes = await projections.PersonnesOccupeesAsync(query.AffilieId, aujourdHui, cancellationToken);
        var actives = await projections.AffectationsActivesAsync(occupes, aujourdHui, cancellationToken);
        var profils = await projections.ProfilsDesPostesAsync(actives.Select(a => a.PosteId).Distinct().ToList(), cancellationToken);
        alertes.AddRange(DetectionAlertes.PostesSansAnalyseRisques(query.AffilieId, actives, profils.Select(p => p.PosteId).ToHashSet()));

        var (politiques, calendrier) = await recalcul.ReferentielsAsync(aujourdHui, cancellationToken);
        var delai = politiques.Duree(CodesParametres.RevueListesNominatives, aujourdHui);
        alertes.AddRange(DetectionAlertes.ListesNonRevues(
            query.AffilieId, await projections.ListesNominativesAsync(query.AffilieId, cancellationToken), delai, calendrier, aujourdHui));

        // ARC-33, POR-04 : alertes du processus de reprise (échéance menacée, hors délai, convocation non remise, absence…).
        if (user.HasPermission(Permissions.RepriseLire))
        {
            foreach (var p in await reprises.ListActifsParAffilieAsync(query.AffilieId, cancellationToken))
            {
                alertes.AddRange(p.Alertes().Select(a => new Alerte(a.Type, p.AffilieId, p.PersonneId, null, null, a.Depuis, a.Message, p.ObligationId is { } id ? [id] : [])));
            }
        }

        return alertes.Select(AlerteDto.De).ToList();
    }
}
