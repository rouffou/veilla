using Microsoft.EntityFrameworkCore;

using Sepp.Obligations.Application;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Adapters.Persistence;

internal sealed class ProcessusRepriseRepository(ObligationsDbContext db) : IProcessusRepriseRepository
{
    public Task<ProcessusReprise?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Processus.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<ProcessusReprise?> GetActifAsync(Guid personneId, Guid affilieId, DateOnly dateReprise, CancellationToken cancellationToken) =>
        db.Processus.SingleOrDefaultAsync(
            p => p.PersonneId == personneId && p.AffilieId == affilieId && p.DateReprise == dateReprise && p.AnnuleeLe == null, cancellationToken);

    public Task<ProcessusReprise?> GetParObligationAsync(Guid obligationId, CancellationToken cancellationToken) =>
        db.Processus.FirstOrDefaultAsync(p => p.ObligationId == obligationId && p.AnnuleeLe == null, cancellationToken);

    public Task<ProcessusReprise?> GetParExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        db.Processus.FirstOrDefaultAsync(p => p.ExamenId == examenId, cancellationToken);

    public Task<ProcessusReprise?> GetParDecisionAsync(Guid decisionId, CancellationToken cancellationToken) =>
        db.Processus.FirstOrDefaultAsync(p => p.DecisionId == decisionId, cancellationToken);

    public async Task<IReadOnlyList<ProcessusReprise>> ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Processus.Where(p => p.PersonneId == personneId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProcessusReprise>> ListAsync(
        Guid? affilieId, StatutReprise? statut, DateOnly? echeanceAvant, int nombreMaximum, CancellationToken cancellationToken) =>
        await db.Processus
            .Where(p => (affilieId == null || p.AffilieId == affilieId)
                        && (statut == null || p.Statut == statut)
                        && (echeanceAvant == null || (p.DateLimite != null && p.DateLimite <= echeanceAvant)))
            .OrderBy(p => p.DateLimite)
            .ThenBy(p => p.Id)
            .Take(nombreMaximum)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProcessusReprise>> ListActifsParAffilieAsync(Guid affilieId, CancellationToken cancellationToken) =>
        await db.Processus
            .Where(p => p.AffilieId == affilieId && p.AnnuleeLe == null && !p.ExamenNonRequis && !p.SansObjet)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProcessusReprise>> ReserverEchusAsync(DateOnly aujourdHui, int nombreMaximum, CancellationToken cancellationToken) =>
        await db.Processus
            .FromSql($"""
                SELECT * FROM processus_reprise
                WHERE prochaine_echeance <= {aujourdHui} AND annulee_le IS NULL AND deleted_at IS NULL
                ORDER BY prochaine_echeance, id
                LIMIT {nombreMaximum}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> PersonnesNonSynchroniseesAsync(CancellationToken cancellationToken) =>
        await db.Processus.Where(p => p.Statut == StatutReprise.Annoncee && p.AnnuleeLe == null).Select(p => p.PersonneId).Distinct().ToListAsync(cancellationToken);

    public void Add(ProcessusReprise processus) => db.Processus.Add(processus);

    public void AbandonnerChangements() => db.ChangeTracker.Clear();
}

internal sealed class DecisionRecueRepository(ObligationsDbContext db) : IDecisionRecueRepository
{
    public Task<DecisionRecue?> GetAsync(Guid examenId, CancellationToken cancellationToken) =>
        db.DecisionsRecues.SingleOrDefaultAsync(d => d.ExamenId == examenId, cancellationToken);

    public void Add(DecisionRecue decision) => db.DecisionsRecues.Add(decision);
}
