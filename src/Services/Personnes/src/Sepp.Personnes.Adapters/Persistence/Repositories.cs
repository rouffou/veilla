using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Application;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Adapters.Persistence;

internal sealed class PersonneRepository(PersonnesDbContext db, AlerteDoublonsNiss alerte) : IPersonneRepository
{
    public Task<Personne?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Personnes.AsSplitQuery().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Personne?> GetParEmpreintesNissAsync(IReadOnlyList<string> empreintes, CancellationToken cancellationToken)
    {
        // Index unique sur niss_hash : au plus une ligne par empreinte. Deux lignes (une par clé) ne se rencontrent que
        // pour un doublon créé hors de la procédure de rotation (#473) : la ligne de la clé courante l'emporte, le doublon
        // est signalé (journal et métrique, #514) et le recalcul de l'index signale la collision.
        var trouvees = await db.Personnes.AsSplitQuery()
            .Where(p => empreintes.Contains(p.NissHash))
            .OrderBy(p => p.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        var retenue = trouvees.OrderBy(p => IndexOf(empreintes, p.NissHash)).FirstOrDefault();
        if (trouvees.Count > 1)
        {
            alerte.Signaler(retenue!.Id, trouvees.Single(p => p.Id != retenue.Id).Id);
        }

        return retenue;
    }

    public Task<Personne?> GetParReferenceDimonaAsync(string referenceDimona, CancellationToken cancellationToken) =>
        db.Personnes.AsSplitQuery().SingleOrDefaultAsync(p => p.Occupations.Any(o => o.ReferenceDimona == referenceDimona), cancellationToken);

    public Task<Personne?> GetParReferenceMutationAsync(string referenceMutation, CancellationToken cancellationToken) =>
        db.Personnes.AsSplitQuery().SingleOrDefaultAsync(p => p.Mutations.Any(m => m.Reference == referenceMutation), cancellationToken);

    public async Task<IReadOnlyList<Personne>> ListerParAffilieAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        await db.Personnes.AsNoTracking().IgnoreAutoIncludes()
            .Where(p => p.Occupations.Any(o => (o.AffilieId == affilieId || o.AffilieUtilisateurId == affilieId)
                                               && o.DateDebut <= date && (o.DateFin == null || o.DateFin >= date)))
            .ToListAsync(cancellationToken);

    public void Add(Personne personne) => db.Personnes.Add(personne);

    private static int IndexOf(IReadOnlyList<string> empreintes, string empreinte)
    {
        for (var i = 0; i < empreintes.Count; i++)
        {
            if (empreintes[i] == empreinte)
            {
                return i;
            }
        }

        return empreintes.Count;
    }
}

/// <summary>
/// Index aveugle HMAC-SHA256 du NISS (DAT-06), clé distincte des clés de chiffrement (<c>BlindIndex:CleEnveloppee</c>) ;
/// pendant une rotation (#473), la clé précédente (<c>BlindIndex:CleEnveloppeePrecedente</c>) est acceptée en lecture.
/// </summary>
internal sealed class NissIndex(BlindIndex index) : INissIndex
{
    public string Calculer(Niss niss) => index.Compute(niss.Valeur);

    public IReadOnlyList<string> Empreintes(Niss niss) => index.Empreintes(niss.Valeur);
}
