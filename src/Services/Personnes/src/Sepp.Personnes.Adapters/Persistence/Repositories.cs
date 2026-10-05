using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Application;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Adapters.Persistence;

internal sealed class PersonneRepository(PersonnesDbContext db) : IPersonneRepository
{
    public Task<Personne?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Personnes.AsSplitQuery().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Personne?> GetParNissHashAsync(string nissHash, CancellationToken cancellationToken) =>
        db.Personnes.AsSplitQuery().SingleOrDefaultAsync(p => p.NissHash == nissHash, cancellationToken);

    public Task<Personne?> GetParReferenceDimonaAsync(string referenceDimona, CancellationToken cancellationToken) =>
        db.Personnes.AsSplitQuery().SingleOrDefaultAsync(p => p.Occupations.Any(o => o.ReferenceDimona == referenceDimona), cancellationToken);

    public async Task<IReadOnlyList<Personne>> ListerParAffilieAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        await db.Personnes.AsNoTracking().IgnoreAutoIncludes()
            .Where(p => p.Occupations.Any(o => (o.AffilieId == affilieId || o.AffilieUtilisateurId == affilieId)
                                               && o.DateDebut <= date && (o.DateFin == null || o.DateFin >= date)))
            .ToListAsync(cancellationToken);

    public void Add(Personne personne) => db.Personnes.Add(personne);
}

/// <summary>Index aveugle HMAC-SHA256 du NISS (DAT-06), clé distincte des clés de chiffrement (<c>BlindIndex:Key</c>).</summary>
internal sealed class NissIndex(BlindIndex index) : INissIndex
{
    public string Calculer(Niss niss) => index.Compute(niss.Valeur);
}
