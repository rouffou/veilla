using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Ecarts;
using Sepp.Affilies.Domain.Groupes;
using Sepp.Affilies.Domain.Historique;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;

namespace Sepp.Affilies.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IAffilieRepository, IGroupeRepository, IHistoriqueAffilieRepository, IEcartSynchronisationRepository
{
    private readonly List<IntegrationEvent> _pending = [];
    private readonly List<ModificationAffilie> _historiqueEnAttente = [];
    private readonly List<EcartSynchronisation> _ecartsEnAttente = [];

    public List<Affilie> Affilies { get; } = [];

    public List<Groupe> Groupes { get; } = [];

    /// <summary>Historique validé (visible après <see cref="SaveChangesAsync"/>, comme en base).</summary>
    public List<ModificationAffilie> Historique { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Published.AddRange(_pending);
        _pending.Clear();
        Historique.AddRange(_historiqueEnAttente);
        _historiqueEnAttente.Clear();
        Ecarts.AddRange(_ecartsEnAttente);
        _ecartsEnAttente.Clear();
        return Task.CompletedTask;
    }

    public void Add(IntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    public Task<Affilie?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Affilies.SingleOrDefault(a => a.Id == id));

    public Task<Affilie?> GetParBceAsync(NumeroBce numeroBce, CancellationToken cancellationToken) =>
        Task.FromResult(Affilies.SingleOrDefault(a => a.NumeroBce == numeroBce));

    public Task<bool> ExisteAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Affilies.Any(a => a.Id == id));

    public Task<bool> BceUtiliseAsync(NumeroBce numeroBce, CancellationToken cancellationToken) =>
        Task.FromResult(Affilies.Any(a => a.NumeroBce == numeroBce));

    public Task<bool> UniteEtablissementUtiliseeAsync(NumeroUniteEtablissement numero, CancellationToken cancellationToken) =>
        Task.FromResult(Affilies.SelectMany(a => a.UnitesEtablissement).Any(u => u.Numero == numero));

    public Task<PageDto<AffilieResumeDto>> RechercherAsync(CriteresRecherche criteres, CancellationToken cancellationToken)
    {
        var resultat = Affilies
            .Where(a => criteres.NumeroBce is null || a.NumeroBce == criteres.NumeroBce)
            .Where(a => criteres.Denomination is null || a.Denomination.Contains(criteres.Denomination, StringComparison.OrdinalIgnoreCase))
            .Where(a => criteres.Parmi is null || criteres.Parmi.Contains(a.Id))
            .OrderBy(a => a.Denomination, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(new PageDto<AffilieResumeDto>(
            resultat.Skip((criteres.Page - 1) * criteres.Taille).Take(criteres.Taille)
                .Select(a => new AffilieResumeDto(a.Id, a.NumeroBce.Formate, a.Denomination, a.CategorieTarifaire, a.Statut, a.DateAffiliation, a.DateFin))
                .ToList(),
            resultat.Count,
            criteres.Page,
            criteres.Taille));
    }

    public void Add(Affilie affilie) => Affilies.Add(affilie);

    Task<Groupe?> IGroupeRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Groupes.SingleOrDefault(g => g.Id == id));

    public Task<IReadOnlyList<Groupe>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Groupe>>(Groupes);

    public void Add(Groupe groupe) => Groupes.Add(groupe);

    /// <summary>Écarts de synchronisation BCE validés (visibles après <see cref="SaveChangesAsync"/>).</summary>
    public List<EcartSynchronisation> Ecarts { get; } = [];

    public void Add(EcartSynchronisation ecart) => _ecartsEnAttente.Add(ecart);

    Task<EcartSynchronisation?> IEcartSynchronisationRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Ecarts.SingleOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<EcartSynchronisation>> OuvertsParBceAsync(string numeroBce, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EcartSynchronisation>>(Ecarts.Where(e => e.NumeroBce == numeroBce && e.Statut == StatutEcart.Ouvert).ToList());

    public Task<IReadOnlyList<EcartSynchronisation>> ListAsync(bool ouvertsSeulement, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EcartSynchronisation>>(Ecarts.Where(e => !ouvertsSeulement || e.Statut == StatutEcart.Ouvert).ToList());

    public void Add(ModificationAffilie modification) => _historiqueEnAttente.Add(modification);

    public Task<IReadOnlyList<ModificationAffilie>> ListAsync(Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ModificationAffilie>>(Historique.Where(m => m.AffilieId == affilieId).ToList());
}

internal sealed class FakeUser(string[] roles, params Guid[] affilies) : ICurrentUser, IPerimetreUtilisateur
{
    public bool IsAuthenticated => true;

    public string UserId => "test-user";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public IReadOnlySet<Guid> AffiliesAutorises { get; } = affilies.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class HorlogeFixe(DateTimeOffset maintenant) : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = maintenant;

    public override DateTimeOffset GetUtcNow() => Maintenant;
}

/// <summary>Assemble les cas d'usage pour un utilisateur donné.</summary>
internal sealed class Contexte(InMemoryStore store, FakeUser utilisateur, HorlogeFixe horloge)
{
    public ControleAcces Acces { get; } = new(utilisateur, utilisateur);

    public ModificateurAffilie Modificateur => new(store, store, store, store, utilisateur, Acces, horloge);
}
