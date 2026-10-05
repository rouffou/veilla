using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Projections;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

namespace Sepp.PostesRisques.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IPosteRepository, IPropositionPosteRisqueRepository, IRisqueRepository,
    ISurchargeFrequenceRepository, IListeNominativeRepository, IPropositionListeRepository, IProjectionRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<Poste> Postes { get; } = [];

    public List<PropositionPosteRisque> Propositions { get; } = [];

    public List<Risque> Risques { get; } = [];

    public List<SurchargeFrequence> Surcharges { get; } = [];

    public List<ListeNominative> Listes { get; } = [];

    public List<PropositionListeNominative> PropositionsListe { get; } = [];

    public List<AffectationPoste> Affectations { get; } = [];

    public List<ExamenRealise> Examens { get; } = [];

    public List<ParametreLegalLocal> Parametres { get; } = [];

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

    Task<Poste?> IPosteRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Postes.SingleOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Poste>> ListAsync(Guid affilieId, StatutPoste? statut, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Poste>>(Postes.Where(p => p.AffilieId == affilieId && (statut is null || p.Statut == statut)).ToList());

    public void Add(Poste poste) => Postes.Add(poste);

    Task<PropositionPosteRisque?> IPropositionPosteRisqueRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Propositions.SingleOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<PropositionPosteRisque>> ListAsync(Guid? affilieId, Guid? posteId, StatutProposition? statut, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PropositionPosteRisque>>(Propositions
            .Where(p => (affilieId is null || p.AffilieId == affilieId) && (posteId is null || p.PosteId == posteId) && (statut is null || p.Statut == statut))
            .ToList());

    public void Add(PropositionPosteRisque proposition) => Propositions.Add(proposition);

    Task<Risque?> IRisqueRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Risques.SingleOrDefault(r => r.Id == id));

    public Task<Risque?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Risques.SingleOrDefault(r => r.Code == code));

    Task<IReadOnlyList<Risque>> IRisqueRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Risque>>(Risques.ToList());

    public void Add(Risque risque) => Risques.Add(risque);

    Task<SurchargeFrequence?> ISurchargeFrequenceRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Surcharges.SingleOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<SurchargeFrequence>> ListAsync(Guid? affilieId, CibleSurcharge? cibleType, Guid? cibleId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SurchargeFrequence>>(Surcharges
            .Where(s => (affilieId is null || s.AffilieId == affilieId) && (cibleType is null || s.CibleType == cibleType) && (cibleId is null || s.CibleId == cibleId))
            .ToList());

    public void Add(SurchargeFrequence surcharge) => Surcharges.Add(surcharge);

    Task<ListeNominative?> IListeNominativeRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Listes.SingleOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<ListeNominative>> ListAsync(Guid affilieId, TypeListeNominative? type, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ListeNominative>>(Listes.Where(l => l.AffilieId == affilieId && (type is null || l.Type == type)).ToList());

    public Task<int> DerniereVersionAsync(Guid affilieId, TypeListeNominative type, CancellationToken cancellationToken) =>
        Task.FromResult(Listes.Where(l => l.AffilieId == affilieId && l.Type == type).Select(l => l.Version).DefaultIfEmpty(0).Max());

    public void Add(ListeNominative liste) => Listes.Add(liste);

    Task<PropositionListeNominative?> IPropositionListeRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(PropositionsListe.SingleOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<PropositionListeNominative>> ListAsync(Guid? affilieId, StatutProposition? statut, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PropositionListeNominative>>(PropositionsListe
            .Where(p => (affilieId is null || p.AffilieId == affilieId) && (statut is null || p.Statut == statut)).ToList());

    public void Add(PropositionListeNominative proposition) => PropositionsListe.Add(proposition);

    public Task<AffectationPoste?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken) =>
        Task.FromResult(Affectations.SingleOrDefault(a => a.AffectationId == affectationId));

    public void Add(AffectationPoste affectation) => Affectations.Add(affectation);

    public Task<IReadOnlyList<AffectationPoste>> ListAffectationsActivesAsync(IReadOnlyCollection<Guid> posteIds, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AffectationPoste>>(Affectations.Where(a => posteIds.Contains(a.PosteId) && a.EstActiveAu(date)).ToList());

    public Task<ExamenRealise?> GetExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        Task.FromResult(Examens.SingleOrDefault(e => e.ExamenId == examenId));

    public void Add(ExamenRealise examen) => Examens.Add(examen);

    public Task<IReadOnlyDictionary<Guid, DateOnly>> DernieresEvaluationsAsync(
        Guid affilieId, IReadOnlyCollection<Guid> personneIds, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateOnly>>(Examens
            .Where(e => e.AffilieId == affilieId && personneIds.Contains(e.PersonneId) && e.Date <= date)
            .GroupBy(e => e.PersonneId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.Date)));

    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.SingleOrDefault(p => p.Code == code && p.ValideDu == valideDu));

    public Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.Where(p => p.Code == code && p.ValideDu <= date).OrderByDescending(p => p.ValideDu).FirstOrDefault());

    public void Add(ParametreLegalLocal parametre) => Parametres.Add(parametre);
}

internal sealed class FakeUser(string userId, params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => userId;

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

/// <summary>Périmètre de test : interne (tous les affiliés) ou externe limité à une liste d'affiliés.</summary>
internal sealed class FakePerimetre(bool externe, params Guid[] affilies) : IPerimetreAffilies
{
    public static FakePerimetre Interne { get; } = new(false);

    public bool EstExterne => externe;

    public bool PeutAcceder(Guid affilieId) => !externe || affilies.Contains(affilieId);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
