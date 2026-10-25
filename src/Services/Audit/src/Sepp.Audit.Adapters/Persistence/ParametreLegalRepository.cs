using Microsoft.EntityFrameworkCore;

using Sepp.Audit.Application.Journal;
using Sepp.Audit.Domain.Parametres;

namespace Sepp.Audit.Adapters.Persistence;

internal sealed class ParametreLegalRepository(AuditDbContext db) : IParametreLegalRepository
{
    public Task<ParametreLegalLocal?> GetAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.Parametres.SingleOrDefaultAsync(p => p.Code == code && p.ValideDu == valideDu, cancellationToken);

    public Task<ParametreLegalLocal?> ApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        db.Parametres.AsNoTracking()
            .Where(p => p.Code == code && p.ValideDu <= date && (p.ValideJusquAu == null || date < p.ValideJusquAu))
            .OrderByDescending(p => p.ValideDu)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ParametreLegalLocal parametre) => db.Parametres.Add(parametre);
}
