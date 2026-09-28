using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Referentiels.Domain.Calendrier;
using Sepp.Referentiels.Domain.Nomenclatures;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IParametreLegalRepository, INomenclatureRepository, ICalendrierRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<ParametreLegal> Parametres { get; } = [];

    public List<Nomenclature> Nomenclatures { get; } = [];

    public List<CalendrierAnnuel> Calendriers { get; } = [];

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

    public Task<ParametreLegal?> GetAsync(CodeParametre code, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.SingleOrDefault(p => p.Code == code));

    Task<IReadOnlyList<ParametreLegal>> IParametreLegalRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ParametreLegal>>(Parametres);

    public void Add(ParametreLegal parametre) => Parametres.Add(parametre);

    public Task<Nomenclature?> GetAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Nomenclatures.SingleOrDefault(n => n.Code == code));

    Task<IReadOnlyList<Nomenclature>> INomenclatureRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Nomenclature>>(Nomenclatures);

    public void Add(Nomenclature nomenclature) => Nomenclatures.Add(nomenclature);

    public Task<CalendrierAnnuel?> GetAsync(int annee, CancellationToken cancellationToken) =>
        Task.FromResult(Calendriers.SingleOrDefault(c => c.Annee == annee));

    public Task<IReadOnlyList<CalendrierAnnuel>> ListAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendrierAnnuel>>(Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin).ToList());

    public void Add(CalendrierAnnuel calendrier) => Calendriers.Add(calendrier);
}

internal sealed class FakeUser(params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => "test-user";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}
