using Microsoft.EntityFrameworkCore;

using Sepp.Reintegration.Application;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Projections;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Adapters.Persistence;

internal sealed class IncapaciteRepository(ReintegrationDbContext db) : IIncapaciteRepository
{
    public Task<Incapacite?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Incapacites.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Incapacite>> ListerParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Incapacites.Where(i => i.PersonneId == personneId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Incapacite>> ListerEnCoursAsync(Guid personneId, Guid affilieId, CancellationToken cancellationToken) =>
        await db.Incapacites.Where(i => i.PersonneId == personneId && i.AffilieId == affilieId && i.Statut == StatutIncapacite.EnCours).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Incapacite>> ListerEnCoursAsync(Guid? affilieId, int maximum, CancellationToken cancellationToken) =>
        await db.Incapacites
            .Where(i => i.Statut == StatutIncapacite.EnCours && (affilieId == null || i.AffilieId == affilieId))
            .OrderBy(i => i.DateDebut).ThenBy(i => i.Id)
            .Take(maximum)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Incapacite>> ReserverEchuesAsync(DateOnly aujourdHui, int maximum, CancellationToken cancellationToken) =>
        await db.Incapacites
            .FromSql($"""
                SELECT * FROM incapacite
                WHERE prochaine_echeance <= {aujourdHui} AND deleted_at IS NULL
                ORDER BY prochaine_echeance, id
                LIMIT {maximum}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

    public void Add(Incapacite incapacite) => db.Incapacites.Add(incapacite);
}

internal sealed class TrajetRepository(ReintegrationDbContext db) : ITrajetRepository
{
    public Task<Trajet?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Trajets.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Trajet>> ListerParIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken) =>
        await db.Trajets.Where(t => t.IncapaciteId == incapaciteId).ToListAsync(cancellationToken);

    public Task<Trajet?> GetOuvertAsync(Guid personneId, Guid affilieId, CancellationToken cancellationToken) =>
        db.Trajets
            .Where(t => t.PersonneId == personneId && t.AffilieId == affilieId && t.Statut != StatutTrajet.Termine && t.Statut != StatutTrajet.Annule)
            .OrderByDescending(t => t.DateDemande)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Trajet>> ListerParAffilieAsync(Guid affilieId, CancellationToken cancellationToken) =>
        await db.Trajets.Where(t => t.AffilieId == affilieId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Trajet>> ListerOuvertsAsync(Guid? affilieId, int maximum, CancellationToken cancellationToken) =>
        await db.Trajets
            .Where(t => t.Statut != StatutTrajet.Termine && t.Statut != StatutTrajet.Annule && (affilieId == null || t.AffilieId == affilieId))
            .OrderBy(t => t.DateDemande).ThenBy(t => t.Id)
            .Take(maximum)
            .ToListAsync(cancellationToken);

    public Task<bool> ExisteEvaluationParAsync(Guid incapaciteId, string utilisateurId, CancellationToken cancellationToken) =>
        db.Trajets.AnyAsync(t => t.IncapaciteId == incapaciteId && t.EvaluateurId == utilisateurId, cancellationToken);

    public async Task<IReadOnlyList<Trajet>> ReserverEchusAsync(DateOnly aujourdHui, int maximum, CancellationToken cancellationToken) =>
        await db.Trajets
            .FromSql($"""
                SELECT * FROM trajet
                WHERE prochaine_echeance <= {aujourdHui} AND deleted_at IS NULL
                ORDER BY prochaine_echeance, id
                LIMIT {maximum}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

    public void Add(Trajet trajet) => db.Trajets.Add(trajet);
}

internal sealed class EchangeEHealthRepository(ReintegrationDbContext db) : IEchangeEHealthRepository
{
    public Task<EchangeEHealth?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Echanges.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<EchangeEHealth?> GetParReferenceAsync(string referenceEHealth, CancellationToken cancellationToken) =>
        db.Echanges.SingleOrDefaultAsync(e => e.ReferenceEHealth == referenceEHealth, cancellationToken);

    public async Task<IReadOnlyList<EchangeEHealth>> ListerParIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken) =>
        await db.Echanges.Where(e => e.IncapaciteId == incapaciteId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EchangeEHealth>> ListerParTrajetAsync(Guid trajetId, CancellationToken cancellationToken) =>
        await db.Echanges.Where(e => e.TrajetId == trajetId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EchangeEHealth>> ReserverAEnvoyerAsync(int maximum, CancellationToken cancellationToken) =>
        await db.Echanges
            .FromSql($"""
                SELECT * FROM echange_ehealth
                WHERE sens = 'Sortant' AND statut IN ('AEnvoyer', 'EnErreur') AND deleted_at IS NULL
                ORDER BY cree_le, id
                LIMIT {maximum}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

    public void Add(EchangeEHealth echange) => db.Echanges.Add(echange);
}

internal sealed class ProjectionRepository(ReintegrationDbContext db) : IProjectionRepository
{
    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.Parametres.SingleOrDefaultAsync(p => p.Code == code && p.ValideDu == valideDu, cancellationToken);

    public Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        db.Parametres
            .Where(p => p.Code == code && p.ValideDu <= date && (p.ValideJusquAu == null || date < p.ValideJusquAu))
            .OrderByDescending(p => p.ValideDu)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ParametreLegalLocal parametre) => db.Parametres.Add(parametre);

    public Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken) =>
        db.Calendriers.SingleOrDefaultAsync(c => c.Annee == annee, cancellationToken);

    public async Task<IReadOnlyList<CalendrierLocal>> ListerCalendriersAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        await db.Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin).ToListAsync(cancellationToken);

    public void Add(CalendrierLocal calendrier) => db.Calendriers.Add(calendrier);

    public Task<ObligationReintegration?> GetObligationAsync(Guid obligationId, CancellationToken cancellationToken) =>
        db.Obligations.SingleOrDefaultAsync(o => o.ObligationId == obligationId, cancellationToken);

    public Task<bool> ExisteObligationAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken) =>
        db.Obligations.AnyAsync(o => obligationIds.Contains(o.ObligationId), cancellationToken);

    public void Add(ObligationReintegration obligation) => db.Obligations.Add(obligation);
}
