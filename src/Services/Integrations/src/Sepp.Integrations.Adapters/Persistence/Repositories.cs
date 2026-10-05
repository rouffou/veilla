using Microsoft.EntityFrameworkCore;

using Sepp.Integrations.Application;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Adapters.Persistence;

internal sealed class JournalFluxRepository(IntegrationsDbContext db) : IJournalFluxRepository
{
    public Task<EchangeFlux?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Journal.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<bool> ExisteAsync(TypeFlux flux, string cleIdempotence, CancellationToken cancellationToken) =>
        db.Journal.Local.Any(e => e.Flux == flux && e.CleIdempotence == cleIdempotence)
        || await db.Journal.AnyAsync(e => e.Flux == flux && e.CleIdempotence == cleIdempotence, cancellationToken);

    public Task<EchangeFlux?> GetParCleAsync(TypeFlux flux, string cleIdempotence, CancellationToken cancellationToken) =>
        db.Journal.SingleOrDefaultAsync(e => e.Flux == flux && e.CleIdempotence == cleIdempotence, cancellationToken);

    public async Task<IReadOnlyList<EchangeFlux>> ListerEnAttenteAsync(TypeFlux flux, int maximum, CancellationToken cancellationToken) =>
        await db.Journal.Where(e => e.Flux == flux && e.Statut == StatutEchange.Recu)
            .OrderBy(e => e.RecuLe).ThenBy(e => e.Id)
            .Take(maximum)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<EchangeFlux> Elements, int Total)> RechercherAsync(CriteresJournal criteres, CancellationToken cancellationToken)
    {
        var requete = db.Journal.AsNoTracking();
        if (criteres.Flux is { } flux)
        {
            requete = requete.Where(e => e.Flux == flux);
        }

        if (criteres.Statuts is { Count: > 0 } statuts)
        {
            var liste = statuts.ToList();
            requete = requete.Where(e => liste.Contains(e.Statut));
        }

        if (criteres.Depuis is { } depuis)
        {
            requete = requete.Where(e => e.RecuLe >= depuis);
        }

        if (criteres.Jusqua is { } jusqua)
        {
            requete = requete.Where(e => e.RecuLe < jusqua);
        }

        var total = await requete.CountAsync(cancellationToken);
        var elements = await requete.OrderByDescending(e => e.RecuLe).ThenByDescending(e => e.Id)
            .Skip((criteres.Page - 1) * criteres.Taille).Take(criteres.Taille)
            .ToListAsync(cancellationToken);
        return (elements, total);
    }

    public async Task<IReadOnlyList<LigneVolume>> VolumesAsync(DateTimeOffset depuis, DateTimeOffset jusqua, CancellationToken cancellationToken) =>
        await db.Journal.AsNoTracking()
            .Where(e => e.RecuLe >= depuis && e.RecuLe < jusqua)
            .Select(e => new LigneVolume(e.Flux, e.Sens, e.RecuLe, e.Statut, e.NombreEnregistrements))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EchangeFlux>> ListerChargesAPurgerAsync(DateTimeOffset traitesAvant, DateTimeOffset echecsAvant, int maximum, CancellationToken cancellationToken) =>
        await db.Journal
            .Where(e => e.ChargeUtile != null
                        && ((e.Statut == StatutEchange.Traite && e.TraiteLe < traitesAvant)
                            || ((e.Statut == StatutEchange.Rejete || e.Statut == StatutEchange.EnErreur) && e.TraiteLe < echecsAvant)))
            .OrderBy(e => e.TraiteLe)
            .Take(maximum)
            .ToListAsync(cancellationToken);

    public void Add(EchangeFlux echange) => db.Journal.Add(echange);
}

internal sealed class PositionFluxRepository(IntegrationsDbContext db) : IPositionFluxRepository
{
    public Task<PositionFlux?> GetAsync(TypeFlux flux, CancellationToken cancellationToken) =>
        db.Positions.SingleOrDefaultAsync(p => p.Flux == flux, cancellationToken);

    public void Add(PositionFlux position) => db.Positions.Add(position);
}

internal sealed class CorrespondanceRepository(IntegrationsDbContext db) : ICorrespondanceRepository
{
    public async Task<CorrespondanceIdentifiant?> GetAsync(TypeIdentifiantExterne type, string valeurNormalisee, CancellationToken cancellationToken) =>
        db.Correspondances.Local.SingleOrDefault(c => c.TypeExterne == type && c.ValeurExterne == valeurNormalisee)
        ?? await db.Correspondances.SingleOrDefaultAsync(c => c.TypeExterne == type && c.ValeurExterne == valeurNormalisee, cancellationToken);

    public async Task<IReadOnlyList<CorrespondanceIdentifiant>> ListerAsync(TypeIdentifiantExterne? type, Guid? identifiantInterne, int maximum, CancellationToken cancellationToken)
    {
        var requete = db.Correspondances.AsNoTracking();
        if (type is { } t)
        {
            requete = requete.Where(c => c.TypeExterne == t);
        }

        if (identifiantInterne is { } id)
        {
            requete = requete.Where(c => c.IdentifiantInterne == id);
        }

        return await requete.OrderBy(c => c.TypeExterne).ThenBy(c => c.ValeurExterne).Take(maximum).ToListAsync(cancellationToken);
    }

    public void Add(CorrespondanceIdentifiant correspondance) => db.Correspondances.Add(correspondance);
}

internal sealed class EntrepriseBceRepository(IntegrationsDbContext db) : IEntrepriseBceRepository
{
    public async Task<EntrepriseBce?> GetParNumeroAsync(string numeroBce, CancellationToken cancellationToken) =>
        db.Entreprises.Local.SingleOrDefault(e => e.NumeroBce == numeroBce)
        ?? await db.Entreprises.SingleOrDefaultAsync(e => e.NumeroBce == numeroBce, cancellationToken);

    public void Add(EntrepriseBce entreprise) => db.Entreprises.Add(entreprise);
}
