using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;

using Sepp.Audit.Application;
using Sepp.Audit.Domain.Journal;

namespace Sepp.Audit.Adapters.Persistence;

internal sealed class JournalAuditRepository(AuditDbContext db) : IJournalAuditRepository
{
    private const int TailleLot = 1000;

    /// <summary>Clé du verrou consultatif PostgreSQL de la chaîne d'une zone (« SEPPAUD » + rang de la zone).</summary>
    public static long CleVerrou(Zone zone) => 0x5345_5050_4155_4400L + (long)zone;

    public async Task<MaillonChaine> VerrouillerDernierMaillonAsync(Zone zone, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Le chaînage d'une entrée d'audit exige une transaction (inbox).");
        }

        // Verrou transactionnel : libéré au commit ou à l'annulation ; une chaîne par zone, donc un verrou par zone.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CleVerrou(zone)})", cancellationToken);

        var dernier = await db.Entrees.AsNoTracking()
            .Where(e => e.Zone == zone)
            .OrderByDescending(e => e.Numero)
            .Select(e => new { e.Numero, e.Empreinte })
            .FirstOrDefaultAsync(cancellationToken);
        if (dernier is not null)
        {
            return new MaillonChaine(dernier.Numero, dernier.Empreinte);
        }

        return (await DernierSceauAsync(zone, cancellationToken))?.Maillon ?? MaillonChaine.Origine;
    }

    public Task<bool> ExisteEvenementAsync(Guid evenementId, CancellationToken cancellationToken) =>
        db.Entrees.AnyAsync(e => e.EvenementSourceId == evenementId, cancellationToken);

    public void Ajouter(EntreeAudit entree) => db.Entrees.Add(entree);

    public Task<EntreeAudit?> ObtenirAsync(Guid id, CancellationToken cancellationToken) =>
        db.Entrees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<PageEntrees> RechercherAsync(CritereRecherche critere, CancellationToken cancellationToken)
    {
        var zones = critere.Zones.ToList();
        var query = db.Entrees.AsNoTracking().Where(e => zones.Contains(e.Zone));
        if (critere.UtilisateurId is { } utilisateur)
        {
            query = query.Where(e => e.UtilisateurId == utilisateur);
        }

        if (critere.ObjetType is { } objetType)
        {
            query = query.Where(e => e.ObjetType == objetType);
        }

        if (critere.ObjetId is { } objetId)
        {
            query = query.Where(e => e.ObjetId == objetId);
        }

        if (critere.Du is { } du)
        {
            var debut = du.ToUniversalTime();
            query = query.Where(e => e.Horodatage >= debut);
        }

        if (critere.Au is { } au)
        {
            var fin = au.ToUniversalTime();
            query = query.Where(e => e.Horodatage <= fin);
        }

        if (critere.BrisDeGlace is { } brisDeGlace)
        {
            query = query.Where(e => e.BrisDeGlace == brisDeGlace);
        }

        var total = await query.CountAsync(cancellationToken);
        var entrees = await query
            .OrderByDescending(e => e.Horodatage).ThenBy(e => e.Id)
            .Skip((critere.Page - 1) * critere.Taille)
            .Take(critere.Taille)
            .ToListAsync(cancellationToken);
        return new PageEntrees(entrees, total);
    }

    public async IAsyncEnumerable<EntreeAudit> ParcourirChaineAsync(Zone zone, long apresNumero, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var curseur = apresNumero;
        while (true)
        {
            var lot = await db.Entrees.AsNoTracking()
                .Where(e => e.Zone == zone && e.Numero > curseur)
                .OrderBy(e => e.Numero)
                .Take(TailleLot)
                .ToListAsync(cancellationToken);
            foreach (var entree in lot)
            {
                yield return entree;
            }

            if (lot.Count < TailleLot)
            {
                yield break;
            }

            curseur = lot[^1].Numero;
        }
    }

    public Task<SceauPurge?> DernierSceauAsync(Zone zone, CancellationToken cancellationToken) =>
        db.Sceaux.AsNoTracking()
            .Where(s => s.Zone == zone)
            .OrderByDescending(s => s.NumeroFinal)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> PurgerAvantAsync(Zone zone, DateTimeOffset limite, CancellationToken cancellationToken)
    {
        var code = zone.ToString();
        var borne = limite.ToUniversalTime();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CleVerrou(zone)})", ct);

            // Autorisation de purge limitée à cette transaction ; le déclencheur exige en outre 10 ans d'ancienneté
            // par ligne et une suppression par préfixe de chaîne, puis écrit le sceau de purge.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL sepp.audit_purge = 'on'", ct);
            var supprimees = await db.Database.ExecuteSqlAsync($"""
                DELETE FROM entree_audit
                WHERE zone = {code}
                  AND numero <= COALESCE(
                      (SELECT min(numero) - 1 FROM entree_audit WHERE zone = {code} AND horodatage >= {borne}),
                      (SELECT max(numero) FROM entree_audit WHERE zone = {code}))
                """, ct);
            await transaction.CommitAsync(ct);
            return supprimees;
        }, cancellationToken);
    }
}
