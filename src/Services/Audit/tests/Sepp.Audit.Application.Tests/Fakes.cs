using System.Runtime.CompilerServices;

using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;

namespace Sepp.Audit.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryJournal : IJournalAuditRepository, IIntegrationEventOutbox
{
    public List<EntreeAudit> Entrees { get; } = [];

    public List<SceauPurge> Sceaux { get; } = [];

    public List<IntegrationEvent> Evenements { get; } = [];

    public List<(Zone Zone, DateTimeOffset Limite)> Purges { get; } = [];

    public void Add(IntegrationEvent integrationEvent) => Evenements.Add(integrationEvent);

    public Task<MaillonChaine> VerrouillerDernierMaillonAsync(Zone zone, CancellationToken cancellationToken) =>
        Task.FromResult(
            Entrees.Where(e => e.Zone == zone).MaxBy(e => e.Numero)?.Maillon
            ?? Sceaux.Where(s => s.Zone == zone).MaxBy(s => s.NumeroFinal)?.Maillon
            ?? MaillonChaine.Origine);

    public Task<bool> ExisteEvenementAsync(Guid evenementId, CancellationToken cancellationToken) =>
        Task.FromResult(Entrees.Any(e => e.EvenementSourceId == evenementId));

    public void Ajouter(EntreeAudit entree) => Entrees.Add(entree);

    public Task<EntreeAudit?> ObtenirAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Entrees.SingleOrDefault(e => e.Id == id));

    public Task<PageEntrees> RechercherAsync(CritereRecherche critere, CancellationToken cancellationToken)
    {
        var resultat = Entrees
            .Where(e => critere.Zones.Contains(e.Zone))
            .Where(e => critere.UtilisateurId is null || e.UtilisateurId == critere.UtilisateurId)
            .Where(e => critere.ObjetType is null || e.ObjetType == critere.ObjetType)
            .Where(e => critere.ObjetId is null || e.ObjetId == critere.ObjetId)
            .Where(e => critere.Du is null || e.Horodatage >= critere.Du)
            .Where(e => critere.Au is null || e.Horodatage <= critere.Au)
            .Where(e => critere.BrisDeGlace is null || e.BrisDeGlace == critere.BrisDeGlace)
            .OrderByDescending(e => e.Horodatage)
            .ToList();
        return Task.FromResult(new PageEntrees(
            resultat.Skip((critere.Page - 1) * critere.Taille).Take(critere.Taille).ToList(), resultat.Count));
    }

    public async IAsyncEnumerable<EntreeAudit> ParcourirChaineAsync(Zone zone, long apresNumero, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var entree in Entrees.Where(e => e.Zone == zone && e.Numero > apresNumero).OrderBy(e => e.Numero))
        {
            await Task.Yield();
            yield return entree;
        }
    }

    public Task<SceauPurge?> DernierSceauAsync(Zone zone, CancellationToken cancellationToken) =>
        Task.FromResult(Sceaux.Where(s => s.Zone == zone).MaxBy(s => s.NumeroFinal));

    public Task<int> PurgerAvantAsync(Zone zone, DateTimeOffset limite, CancellationToken cancellationToken)
    {
        Purges.Add((zone, limite));
        return Task.FromResult(0);
    }
}

internal sealed class FakeUser(params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => "test-user";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
