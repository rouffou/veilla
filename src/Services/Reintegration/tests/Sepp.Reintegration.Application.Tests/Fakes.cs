using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Projections;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IIncapaciteRepository, ITrajetRepository, IEchangeEHealthRepository, IProjectionRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<Incapacite> Incapacites { get; } = [];

    public List<Trajet> Trajets { get; } = [];

    public List<EchangeEHealth> Echanges { get; } = [];

    public List<ParametreLegalLocal> Parametres { get; } = [];

    public List<CalendrierLocal> Calendriers { get; } = [];

    public List<ObligationReintegration> Obligations { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Published.AddRange(_pending);
        _pending.Clear();
        foreach (var agregat in Incapacites.Cast<BuildingBlocks.Domain.AggregateRoot>().Concat(Trajets).Concat(Echanges))
        {
            agregat.ClearDomainEvents();
        }

        return Task.CompletedTask;
    }

    public void Add(IntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    // Incapacités
    Task<Incapacite?> IIncapaciteRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Incapacites.SingleOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<Incapacite>> ListerParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Incapacite>>([.. Incapacites.Where(i => i.PersonneId == personneId)]);

    public Task<IReadOnlyList<Incapacite>> ListerEnCoursAsync(Guid personneId, Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Incapacite>>([.. Incapacites.Where(i => i.PersonneId == personneId && i.AffilieId == affilieId && i.EstEnCours)]);

    public Task<IReadOnlyList<Incapacite>> ListerEnCoursAsync(Guid? affilieId, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Incapacite>>([.. Incapacites.Where(i => i.EstEnCours && (affilieId == null || i.AffilieId == affilieId)).Take(maximum)]);

    public Task<IReadOnlyList<Incapacite>> ReserverEchuesAsync(DateOnly aujourdHui, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Incapacite>>([.. Incapacites.Where(i => i.ProchaineEcheance <= aujourdHui).Take(maximum)]);

    public void Add(Incapacite incapacite) => Incapacites.Add(incapacite);

    // Trajets
    Task<Trajet?> ITrajetRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Trajets.SingleOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<Trajet>> ListerParIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Trajet>>([.. Trajets.Where(t => t.IncapaciteId == incapaciteId)]);

    public Task<Trajet?> GetOuvertAsync(Guid personneId, Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult(Trajets.LastOrDefault(t => t.PersonneId == personneId && t.AffilieId == affilieId && !t.EstClos));

    public Task<IReadOnlyList<Trajet>> ListerParAffilieAsync(Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Trajet>>([.. Trajets.Where(t => t.AffilieId == affilieId)]);

    public Task<IReadOnlyList<Trajet>> ListerOuvertsAsync(Guid? affilieId, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Trajet>>([.. Trajets.Where(t => !t.EstClos && (affilieId == null || t.AffilieId == affilieId)).Take(maximum)]);

    public Task<bool> ExisteEvaluationParAsync(Guid incapaciteId, string utilisateurId, CancellationToken cancellationToken) =>
        Task.FromResult(Trajets.Any(t => t.IncapaciteId == incapaciteId && t.EvaluateurId == utilisateurId));

    public Task<IReadOnlyList<Trajet>> ReserverEchusAsync(DateOnly aujourdHui, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Trajet>>([.. Trajets.Where(t => t.ProchaineEcheance <= aujourdHui).Take(maximum)]);

    public void Add(Trajet trajet) => Trajets.Add(trajet);

    // Échanges eHealth
    Task<EchangeEHealth?> IEchangeEHealthRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Echanges.SingleOrDefault(e => e.Id == id));

    public Task<EchangeEHealth?> GetParReferenceAsync(string referenceEHealth, CancellationToken cancellationToken) =>
        Task.FromResult(Echanges.SingleOrDefault(e => e.ReferenceEHealth == referenceEHealth));

    Task<IReadOnlyList<EchangeEHealth>> IEchangeEHealthRepository.ListerParIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EchangeEHealth>>([.. Echanges.Where(e => e.IncapaciteId == incapaciteId)]);

    public Task<IReadOnlyList<EchangeEHealth>> ListerParTrajetAsync(Guid trajetId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EchangeEHealth>>([.. Echanges.Where(e => e.TrajetId == trajetId)]);

    public Task<IReadOnlyList<EchangeEHealth>> ReserverAEnvoyerAsync(int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EchangeEHealth>>([.. Echanges.Where(e => e.EstAEnvoyer).Take(maximum)]);

    public void Add(EchangeEHealth echange) => Echanges.Add(echange);

    // Projections
    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.SingleOrDefault(p => p.Code == code && p.ValideDu == valideDu));

    public Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.Where(p => p.Code == code && p.EstApplicableAu(date)).OrderByDescending(p => p.ValideDu).FirstOrDefault());

    public void Add(ParametreLegalLocal parametre) => Parametres.Add(parametre);

    public Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken) => Task.FromResult(Calendriers.SingleOrDefault(c => c.Annee == annee));

    public Task<IReadOnlyList<CalendrierLocal>> ListerCalendriersAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendrierLocal>>([.. Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin)]);

    public void Add(CalendrierLocal calendrier) => Calendriers.Add(calendrier);

    public Task<ObligationReintegration?> GetObligationAsync(Guid obligationId, CancellationToken cancellationToken) =>
        Task.FromResult(Obligations.SingleOrDefault(o => o.ObligationId == obligationId));

    public Task<bool> ExisteObligationAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken) =>
        Task.FromResult(Obligations.Any(o => obligationIds.Contains(o.ObligationId)));

    public void Add(ObligationReintegration obligation) => Obligations.Add(obligation);
}

internal sealed record Trace(ActionAudit Action, string ObjetType, Guid ObjetId, string? Motif, bool BrisDeGlace);

internal sealed class FakeAudit(InMemoryStore store) : IAuditTrail
{
    public List<Trace> Traces { get; } = [];

    public void Enregistrer(ActionAudit action, string objetType, Guid objetId, string? motif = null, bool brisDeGlace = false)
    {
        if (MotifAcces.Verifier(motif, brisDeGlace) is { } erreur)
        {
            throw new ArgumentException(erreur.Message, nameof(motif));
        }

        Traces.Add(new Trace(action, objetType, objetId, motif, brisDeGlace));
    }

    public async Task EnregistrerLectureAsync(string objetType, Guid objetId, string? motif, bool brisDeGlace, CancellationToken cancellationToken)
    {
        Traces.Add(new Trace(ActionAudit.Lecture, objetType, objetId, motif, brisDeGlace));
        await store.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class FakeUser(string userId, params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId { get; } = userId;

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class FakeContexte : IContexteAcces
{
    public string? Motif { get; set; }

    public bool BrisDeGlace { get; set; }

    public Guid? AffilieIdJeton { get; set; }

    public Guid? PersonneIdJeton { get; set; }
}

internal sealed class FakeHorloge(DateTimeOffset maintenant) : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = maintenant;

    public override DateTimeOffset GetUtcNow() => Maintenant;
}

internal sealed class FakeCanal : ICanalEHealth
{
    public List<MessageEHealthSortant> Envoyes { get; } = [];

    public Queue<MessageEHealthRecu> AReceptionner { get; } = new();

    public bool EnPanne { get; set; }

    public Task<ResultatEnvoiEHealth> EnvoyerAsync(MessageEHealthSortant message, CancellationToken cancellationToken)
    {
        if (EnPanne)
        {
            return Task.FromResult(ResultatEnvoiEHealth.Echec("ehealth.indisponible"));
        }

        Envoyes.Add(message);
        return Task.FromResult(ResultatEnvoiEHealth.Succes($"EH-{message.EchangeId:N}"));
    }

    public Task<IReadOnlyList<MessageEHealthRecu>> RecevoirAsync(CancellationToken cancellationToken)
    {
        var recus = AReceptionner.ToList();
        AReceptionner.Clear();
        return Task.FromResult<IReadOnlyList<MessageEHealthRecu>>(recus);
    }
}
