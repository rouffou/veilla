using System.Security.Cryptography;
using System.Text;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IPersonneRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<Personne> Personnes { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Published.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }

    public void Add(IntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    public Task<Personne?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Personnes.SingleOrDefault(p => p.Id == id));

    public Task<Personne?> GetParNissHashAsync(string nissHash, CancellationToken cancellationToken) =>
        Task.FromResult(Personnes.SingleOrDefault(p => p.NissHash == nissHash));

    public Task<Personne?> GetParReferenceDimonaAsync(string referenceDimona, CancellationToken cancellationToken) =>
        Task.FromResult(Personnes.SingleOrDefault(p => p.Occupations.Any(o => o.ReferenceDimona == referenceDimona)));

    public Task<Personne?> GetParReferenceMutationAsync(string referenceMutation, CancellationToken cancellationToken) =>
        Task.FromResult(Personnes.SingleOrDefault(p => p.Mutations.Any(m => m.Reference == referenceMutation)));

    public Task<IReadOnlyList<Personne>> ListerParAffilieAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Personne>>(Personnes
            .Where(p => p.Occupations.Any(o => (o.AffilieId == affilieId || o.AffilieUtilisateurId == affilieId) && o.EstActiveAu(date)))
            .ToList());

    public void Add(Personne personne) => Personnes.Add(personne);
}

/// <summary>Index aveugle de test : HMAC avec une clé fixe de test (jamais une vraie clé).</summary>
internal sealed class FakeNissIndex : INissIndex
{
    private static readonly byte[] Cle = Encoding.UTF8.GetBytes("cle-de-test-uniquement-32-octets");

    public string Calculer(Niss niss) => Convert.ToHexString(HMACSHA256.HashData(Cle, Encoding.UTF8.GetBytes(niss.Valeur)));
}

internal sealed class FakeUser(params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => "test-user";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class FakeContexte(Guid? affilieId = null) : IContexteAffilie
{
    public Guid? AffilieId { get; } = affilieId;
}
