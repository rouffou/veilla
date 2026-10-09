using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IJournalFluxRepository, IPositionFluxRepository, ICorrespondanceRepository, IEntrepriseBceRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<EchangeFlux> Journal { get; } = [];

    public List<PositionFlux> Positions { get; } = [];

    public List<CorrespondanceIdentifiant> Correspondances { get; } = [];

    public List<EntrepriseBce> Entreprises { get; } = [];

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

    public Task<EchangeFlux?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Journal.SingleOrDefault(e => e.Id == id));

    public Task<bool> ExisteAsync(TypeFlux flux, string cleIdempotence, CancellationToken cancellationToken) =>
        Task.FromResult(Journal.Any(e => e.Flux == flux && e.CleIdempotence == cleIdempotence));

    public Task<EchangeFlux?> GetParCleAsync(TypeFlux flux, string cleIdempotence, CancellationToken cancellationToken) =>
        Task.FromResult(Journal.SingleOrDefault(e => e.Flux == flux && e.CleIdempotence == cleIdempotence));

    public Task<IReadOnlyList<EchangeFlux>> ListerEnAttenteAsync(TypeFlux flux, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EchangeFlux>>(Journal.Where(e => e.Flux == flux && e.Statut == StatutEchange.Recu).Take(maximum).ToList());

    public Task<(IReadOnlyList<EchangeFlux> Elements, int Total)> RechercherAsync(CriteresJournal criteres, CancellationToken cancellationToken)
    {
        var liste = Journal
            .Where(e => criteres.Flux is null || e.Flux == criteres.Flux)
            .Where(e => criteres.Statuts is not { Count: > 0 } || criteres.Statuts.Contains(e.Statut))
            .Where(e => criteres.Depuis is null || e.RecuLe >= criteres.Depuis)
            .Where(e => criteres.Jusqua is null || e.RecuLe < criteres.Jusqua)
            .ToList();
        return Task.FromResult<(IReadOnlyList<EchangeFlux>, int)>((liste.Skip((criteres.Page - 1) * criteres.Taille).Take(criteres.Taille).ToList(), liste.Count));
    }

    public Task<IReadOnlyList<LigneVolume>> VolumesAsync(DateTimeOffset depuis, DateTimeOffset jusqua, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LigneVolume>>(Journal.Where(e => e.RecuLe >= depuis && e.RecuLe < jusqua)
            .Select(e => new LigneVolume(e.Flux, e.Sens, e.RecuLe, e.Statut, e.NombreEnregistrements)).ToList());

    public Task<IReadOnlyList<EchangeFlux>> ListerChargesAPurgerAsync(DateTimeOffset traitesAvant, DateTimeOffset echecsAvant, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EchangeFlux>>(Journal.Where(e => e.ChargeUtileDisponible
                && ((e.Statut == StatutEchange.Traite && e.TraiteLe < traitesAvant)
                    || (e.Statut is StatutEchange.Rejete or StatutEchange.EnErreur && e.TraiteLe < echecsAvant)))
            .Take(maximum).ToList());

    public void Add(EchangeFlux echange) => Journal.Add(echange);

    public Task<PositionFlux?> GetAsync(TypeFlux flux, CancellationToken cancellationToken) => Task.FromResult(Positions.SingleOrDefault(p => p.Flux == flux));

    public void Add(PositionFlux position) => Positions.Add(position);

    public Task<CorrespondanceIdentifiant?> GetAsync(TypeIdentifiantExterne type, string valeurNormalisee, CancellationToken cancellationToken) =>
        Task.FromResult(Correspondances.SingleOrDefault(c => c.TypeExterne == type && c.ValeurExterne == valeurNormalisee));

    public Task<IReadOnlyList<CorrespondanceIdentifiant>> ListerAsync(TypeIdentifiantExterne? type, Guid? identifiantInterne, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CorrespondanceIdentifiant>>(Correspondances
            .Where(c => type is null || c.TypeExterne == type)
            .Where(c => identifiantInterne is null || c.IdentifiantInterne == identifiantInterne)
            .OrderBy(c => c.ValeurExterne, StringComparer.Ordinal).Take(maximum).ToList());

    public void Add(CorrespondanceIdentifiant correspondance) => Correspondances.Add(correspondance);

    public Task<EntrepriseBce?> GetParNumeroAsync(string numeroBce, CancellationToken cancellationToken) =>
        Task.FromResult(Entreprises.SingleOrDefault(e => e.NumeroBce == numeroBce));

    public void Add(EntrepriseBce entreprise) => Entreprises.Add(entreprise);
}

internal sealed class FakeUser(params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => "test";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

/// <summary>Horloge réglable.</summary>
internal sealed class FakeClock(DateTimeOffset maintenant) : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = maintenant;

    public override DateTimeOffset GetUtcNow() => Maintenant;
}

internal sealed class FakeBce : IRegistreBce
{
    public Dictionary<string, DonneesEntreprise> Entreprises { get; } = [];

    public Task<DonneesEntreprise?> ConsulterEntrepriseAsync(string numeroBce, CancellationToken cancellationToken) =>
        Task.FromResult(Entreprises.GetValueOrDefault(numeroBce));
}

internal sealed class FakeDimona : IFluxDimona
{
    public List<DeclarationDimona> Declarations { get; } = [];

    public Exception? Panne { get; set; }

    public Task<LotFlux<DeclarationDimona>> RecupererDeclarationsAsync(string? position, CancellationToken cancellationToken)
    {
        if (Panne is not null)
        {
            throw Panne;
        }

        // Rejoue tout à chaque lecture : l'idempotence de la réception est à la charge du journal.
        return Task.FromResult(new LotFlux<DeclarationDimona>(Declarations.ToList(), $"pos-{Declarations.Count}"));
    }
}

internal sealed class FakeRegistreNational : IRegistreNational
{
    public Dictionary<string, IdentiteRegistreNational> Identites { get; } = [];

    public List<MutationRegistreNational> Mutations { get; } = [];

    public Task<IdentiteRegistreNational?> ConsulterIdentiteAsync(string niss, CancellationToken cancellationToken) =>
        Task.FromResult(Identites.GetValueOrDefault(niss));

    public Task<LotFlux<MutationRegistreNational>> RecupererMutationsAsync(string? position, CancellationToken cancellationToken) =>
        Task.FromResult(new LotFlux<MutationRegistreNational>(Mutations.ToList(), null));
}

/// <summary>Service Personnes simulé : idempotent sur la référence DIMONA, comme le vrai.</summary>
internal sealed class FakePersonnes : IPersonnesClient
{
    private readonly Dictionary<string, OccupationDimona> _occupations = [];

    public List<EntreeDimonaPersonnes> Entrees { get; } = [];

    public List<(string Reference, DateOnly DateFin)> Sorties { get; } = [];

    public List<string> Mutations { get; } = [];

    /// <summary>Réponse imposée au prochain appel (puis retour au comportement normal).</summary>
    public Func<ResultatAppel<OccupationDimona>?>? ReponseEntree { get; set; }

    public Task<ResultatAppel<OccupationDimona>> EnregistrerEntreeDimonaAsync(EntreeDimonaPersonnes entree, CancellationToken cancellationToken)
    {
        Entrees.Add(entree);
        if (ReponseEntree?.Invoke() is { } imposee)
        {
            return Task.FromResult(imposee);
        }

        if (_occupations.TryGetValue(entree.ReferenceDimona, out var existante))
        {
            return Task.FromResult(ResultatAppel<OccupationDimona>.Succes(existante with { DejaEnregistree = true }));
        }

        var occupation = new OccupationDimona(Guid.CreateVersion7(), Guid.CreateVersion7(), false);
        _occupations[entree.ReferenceDimona] = occupation;
        return Task.FromResult(ResultatAppel<OccupationDimona>.Succes(occupation));
    }

    public Task<ResultatAppel<bool>> EnregistrerSortieDimonaAsync(string referenceDimona, DateOnly dateFin, CancellationToken cancellationToken)
    {
        Sorties.Add((referenceDimona, dateFin));
        return Task.FromResult(_occupations.ContainsKey(referenceDimona)
            ? ResultatAppel<bool>.Succes(true)
            : ResultatAppel<bool>.Erreur("occupation.inconnue", $"Aucune occupation pour la référence DIMONA {referenceDimona}."));
    }

    /// <summary>Réponse imposée au prochain appel de mutation (puis retour au comportement normal).</summary>
    public Func<ResultatAppel<MutationPersonnes>?>? ReponseMutation { get; set; }

    public List<MutationRegistreNational> MutationsRecues { get; } = [];

    /// <summary>Idempotent sur la référence de la mutation, comme le vrai service.</summary>
    public Task<ResultatAppel<MutationPersonnes>> AppliquerMutationAsync(MutationRegistreNational mutation, CancellationToken cancellationToken)
    {
        MutationsRecues.Add(mutation);
        if (ReponseMutation?.Invoke() is { } imposee)
        {
            return Task.FromResult(imposee);
        }

        var deja = Mutations.Contains(mutation.ReferenceMutation);
        if (!deja)
        {
            Mutations.Add(mutation.ReferenceMutation);
        }

        return Task.FromResult(ResultatAppel<MutationPersonnes>.Succes(new MutationPersonnes(Guid.Empty, deja ? "DejaAppliquee" : "Appliquee")));
    }

    public OccupationDimona Occupation(string reference) => _occupations[reference];
}

internal static class Identites
{
    public static IdentiteRegistreNational Dupont => new("Dupont", "Marie", new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr);
}
