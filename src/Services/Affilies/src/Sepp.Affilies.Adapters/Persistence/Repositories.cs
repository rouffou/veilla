using Microsoft.EntityFrameworkCore;

using Sepp.Affilies.Application;
using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Ecarts;
using Sepp.Affilies.Domain.Groupes;
using Sepp.Affilies.Domain.Historique;

namespace Sepp.Affilies.Adapters.Persistence;

internal sealed class AffilieRepository(AffiliesDbContext db) : IAffilieRepository
{
    public Task<Affilie?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Affilies.AsSplitQuery().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Affilie?> GetParBceAsync(NumeroBce numeroBce, CancellationToken cancellationToken) =>
        db.Affilies.AsSplitQuery().SingleOrDefaultAsync(a => a.NumeroBce == numeroBce, cancellationToken);

    public Task<bool> ExisteAsync(Guid id, CancellationToken cancellationToken) =>
        db.Affilies.AnyAsync(a => a.Id == id, cancellationToken);

    public Task<bool> BceUtiliseAsync(NumeroBce numeroBce, CancellationToken cancellationToken) =>
        db.Affilies.AnyAsync(a => a.NumeroBce == numeroBce, cancellationToken);

    public Task<bool> UniteEtablissementUtiliseeAsync(NumeroUniteEtablissement numero, CancellationToken cancellationToken) =>
        db.Set<UniteEtablissement>().AnyAsync(u => u.Numero == numero, cancellationToken);

    public async Task<PageDto<AffilieResumeDto>> RechercherAsync(CriteresRecherche criteres, CancellationToken cancellationToken)
    {
        var query = db.Affilies.IgnoreAutoIncludes().AsNoTracking();
        if (criteres.NumeroBce is { } numero)
        {
            query = query.Where(a => a.NumeroBce == numero);
        }

        if (criteres.Denomination is { } denomination)
        {
            var motif = "%" + denomination.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(a => EF.Functions.ILike(a.Denomination, motif));
        }

        if (criteres.Parmi is { } parmi)
        {
            var ids = parmi.ToList();
            query = query.Where(a => ids.Contains(a.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var elements = await query
            .OrderBy(a => a.Denomination).ThenBy(a => a.Id)
            .Skip((criteres.Page - 1) * criteres.Taille)
            .Take(criteres.Taille)
            .ToListAsync(cancellationToken);
        return new PageDto<AffilieResumeDto>(
            elements.Select(a => new AffilieResumeDto(a.Id, a.NumeroBce.Formate, a.Denomination, a.CategorieTarifaire, a.Statut, a.DateAffiliation, a.DateFin)).ToList(),
            total,
            criteres.Page,
            criteres.Taille);
    }

    public void Add(Affilie affilie) => db.Affilies.Add(affilie);
}

internal sealed class GroupeRepository(AffiliesDbContext db) : IGroupeRepository
{
    public Task<Groupe?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Groupes.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Groupe>> ListAsync(CancellationToken cancellationToken) =>
        await db.Groupes.ToListAsync(cancellationToken);

    public void Add(Groupe groupe) => db.Groupes.Add(groupe);
}

internal sealed class HistoriqueAffilieRepository(AffiliesDbContext db) : IHistoriqueAffilieRepository
{
    public void Add(ModificationAffilie modification) => db.Historique.Add(modification);

    public async Task<IReadOnlyList<ModificationAffilie>> ListAsync(Guid affilieId, CancellationToken cancellationToken) =>
        await db.Historique.AsNoTracking().Where(m => m.AffilieId == affilieId).OrderBy(m => m.NumeroVersion).ToListAsync(cancellationToken);
}

internal sealed class EcartSynchronisationRepository(AffiliesDbContext db) : IEcartSynchronisationRepository
{
    public Task<EcartSynchronisation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.EcartsSynchronisation.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EcartSynchronisation>> OuvertsParBceAsync(string numeroBce, CancellationToken cancellationToken) =>
        await db.EcartsSynchronisation.Where(e => e.NumeroBce == numeroBce && e.Statut == StatutEcart.Ouvert).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EcartSynchronisation>> ListAsync(bool ouvertsSeulement, CancellationToken cancellationToken) =>
        await db.EcartsSynchronisation.AsNoTracking()
            .Where(e => !ouvertsSeulement || e.Statut == StatutEcart.Ouvert)
            .OrderByDescending(e => e.DerniereDetectionLe)
            .Take(500)
            .ToListAsync(cancellationToken);

    public void Add(EcartSynchronisation ecart) => db.EcartsSynchronisation.Add(ecart);
}
