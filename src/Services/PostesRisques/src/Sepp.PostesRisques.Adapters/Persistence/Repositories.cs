using Microsoft.EntityFrameworkCore;

using Sepp.PostesRisques.Application;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Projections;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

namespace Sepp.PostesRisques.Adapters.Persistence;

internal sealed class PosteRepository(PostesRisquesDbContext db) : IPosteRepository
{
    public Task<Poste?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Postes.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Poste>> ListAsync(Guid affilieId, StatutPoste? statut, CancellationToken cancellationToken) =>
        await db.Postes.Where(p => p.AffilieId == affilieId && (statut == null || p.Statut == statut)).AsSplitQuery().ToListAsync(cancellationToken);

    public void Add(Poste poste) => db.Postes.Add(poste);
}

internal sealed class PropositionPosteRisqueRepository(PostesRisquesDbContext db) : IPropositionPosteRisqueRepository
{
    public Task<PropositionPosteRisque?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.PropositionsPosteRisque.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PropositionPosteRisque>> ListAsync(
        Guid? affilieId, Guid? posteId, StatutProposition? statut, CancellationToken cancellationToken) =>
        await db.PropositionsPosteRisque
            .Where(p => (affilieId == null || p.AffilieId == affilieId) && (posteId == null || p.PosteId == posteId) && (statut == null || p.Statut == statut))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public void Add(PropositionPosteRisque proposition) => db.PropositionsPosteRisque.Add(proposition);
}

internal sealed class RisqueRepository(PostesRisquesDbContext db) : IRisqueRepository
{
    public Task<Risque?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Risques.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Risque?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Risques.SingleOrDefaultAsync(r => r.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Risque>> ListAsync(CancellationToken cancellationToken) =>
        await db.Risques.AsSplitQuery().ToListAsync(cancellationToken);

    public void Add(Risque risque) => db.Risques.Add(risque);
}

internal sealed class SurchargeFrequenceRepository(PostesRisquesDbContext db) : ISurchargeFrequenceRepository
{
    public Task<SurchargeFrequence?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Surcharges.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SurchargeFrequence>> ListAsync(
        Guid? affilieId, CibleSurcharge? cibleType, Guid? cibleId, CancellationToken cancellationToken) =>
        await db.Surcharges
            .Where(s => (affilieId == null || s.AffilieId == affilieId) && (cibleType == null || s.CibleType == cibleType) && (cibleId == null || s.CibleId == cibleId))
            .ToListAsync(cancellationToken);

    public void Add(SurchargeFrequence surcharge) => db.Surcharges.Add(surcharge);
}

internal sealed class ListeNominativeRepository(PostesRisquesDbContext db) : IListeNominativeRepository
{
    public Task<ListeNominative?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.ListesNominatives.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ListeNominative>> ListAsync(Guid affilieId, TypeListeNominative? type, CancellationToken cancellationToken) =>
        await db.ListesNominatives.Where(l => l.AffilieId == affilieId && (type == null || l.Type == type)).AsSplitQuery().ToListAsync(cancellationToken);

    public async Task<int> DerniereVersionAsync(Guid affilieId, TypeListeNominative type, CancellationToken cancellationToken) =>
        await db.ListesNominatives.IgnoreAutoIncludes()
            .Where(l => l.AffilieId == affilieId && l.Type == type)
            .MaxAsync(l => (int?)l.Version, cancellationToken) ?? 0;

    public void Add(ListeNominative liste) => db.ListesNominatives.Add(liste);
}

internal sealed class PropositionListeRepository(PostesRisquesDbContext db) : IPropositionListeRepository
{
    public Task<PropositionListeNominative?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.PropositionsListe.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PropositionListeNominative>> ListAsync(Guid? affilieId, StatutProposition? statut, CancellationToken cancellationToken) =>
        await db.PropositionsListe
            .Where(p => (affilieId == null || p.AffilieId == affilieId) && (statut == null || p.Statut == statut))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public void Add(PropositionListeNominative proposition) => db.PropositionsListe.Add(proposition);
}

internal sealed class ProjectionRepository(PostesRisquesDbContext db) : IProjectionRepository
{
    public Task<AffectationPoste?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken) =>
        db.Affectations.SingleOrDefaultAsync(a => a.AffectationId == affectationId, cancellationToken);

    public void Add(AffectationPoste affectation) => db.Affectations.Add(affectation);

    public async Task<IReadOnlyList<AffectationPoste>> ListAffectationsActivesAsync(
        IReadOnlyCollection<Guid> posteIds, DateOnly date, CancellationToken cancellationToken) =>
        await db.Affectations
            .Where(a => posteIds.Contains(a.PosteId) && a.DateDebut <= date && (a.DateFin == null || a.DateFin >= date))
            .ToListAsync(cancellationToken);

    public Task<ExamenRealise?> GetExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        db.Examens.SingleOrDefaultAsync(e => e.ExamenId == examenId, cancellationToken);

    public void Add(ExamenRealise examen) => db.Examens.Add(examen);

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> DernieresEvaluationsAsync(
        Guid affilieId, IReadOnlyCollection<Guid> personneIds, DateOnly date, CancellationToken cancellationToken)
    {
        if (personneIds.Count == 0)
        {
            return new Dictionary<Guid, DateOnly>();
        }

        var dernieres = await db.Examens
            .Where(e => e.AffilieId == affilieId && personneIds.Contains(e.PersonneId) && e.Date <= date)
            .GroupBy(e => e.PersonneId)
            .Select(g => new { PersonneId = g.Key, Date = g.Max(e => e.Date) })
            .ToListAsync(cancellationToken);
        return dernieres.ToDictionary(d => d.PersonneId, d => d.Date);
    }

    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.Parametres.SingleOrDefaultAsync(p => p.Code == code && p.ValideDu == valideDu, cancellationToken);

    public Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        db.Parametres.Where(p => p.Code == code && p.ValideDu <= date).OrderByDescending(p => p.ValideDu).FirstOrDefaultAsync(cancellationToken);

    public void Add(ParametreLegalLocal parametre) => db.Parametres.Add(parametre);
}
