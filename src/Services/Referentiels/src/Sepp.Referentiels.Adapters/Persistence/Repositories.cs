using Microsoft.EntityFrameworkCore;
using Sepp.Referentiels.Application;
using Sepp.Referentiels.Domain.Calendrier;
using Sepp.Referentiels.Domain.Nomenclatures;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Adapters.Persistence;

internal sealed class ParametreLegalRepository(ReferentielsDbContext db) : IParametreLegalRepository
{
    public Task<ParametreLegal?> GetAsync(CodeParametre code, CancellationToken cancellationToken) =>
        db.ParametresLegaux.SingleOrDefaultAsync(p => p.Code == code, cancellationToken);

    public async Task<IReadOnlyList<ParametreLegal>> ListAsync(CancellationToken cancellationToken) =>
        await db.ParametresLegaux.AsSplitQuery().ToListAsync(cancellationToken);

    public void Add(ParametreLegal parametre) => db.ParametresLegaux.Add(parametre);
}

internal sealed class NomenclatureRepository(ReferentielsDbContext db) : INomenclatureRepository
{
    public Task<Nomenclature?> GetAsync(string code, CancellationToken cancellationToken) =>
        db.Nomenclatures.SingleOrDefaultAsync(n => n.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Nomenclature>> ListAsync(CancellationToken cancellationToken) =>
        await db.Nomenclatures.IgnoreAutoIncludes().ToListAsync(cancellationToken);

    public void Add(Nomenclature nomenclature) => db.Nomenclatures.Add(nomenclature);
}

internal sealed class CalendrierRepository(ReferentielsDbContext db) : ICalendrierRepository
{
    public Task<CalendrierAnnuel?> GetAsync(int annee, CancellationToken cancellationToken) =>
        db.Calendriers.SingleOrDefaultAsync(c => c.Annee == annee, cancellationToken);

    public async Task<IReadOnlyList<CalendrierAnnuel>> ListAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        await db.Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin).ToListAsync(cancellationToken);

    public void Add(CalendrierAnnuel calendrier) => db.Calendriers.Add(calendrier);
}
