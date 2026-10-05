using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Application;

/// <summary>Critères de consultation du journal des flux (INT-02).</summary>
public sealed record CriteresJournal(
    TypeFlux? Flux,
    IReadOnlySet<StatutEchange>? Statuts,
    DateTimeOffset? Depuis,
    DateTimeOffset? Jusqua,
    int Page,
    int Taille);

/// <summary>Ligne minimale pour le calcul des volumes (aucune charge utile).</summary>
public sealed record LigneVolume(TypeFlux Flux, SensFlux Sens, DateTimeOffset RecuLe, StatutEchange Statut, int NombreEnregistrements);

public interface IJournalFluxRepository
{
    Task<EchangeFlux?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExisteAsync(TypeFlux flux, string cleIdempotence, CancellationToken cancellationToken);

    Task<EchangeFlux?> GetParCleAsync(TypeFlux flux, string cleIdempotence, CancellationToken cancellationToken);

    /// <summary>Échanges reçus non encore traités d'un flux, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<EchangeFlux>> ListerEnAttenteAsync(TypeFlux flux, int maximum, CancellationToken cancellationToken);

    Task<(IReadOnlyList<EchangeFlux> Elements, int Total)> RechercherAsync(CriteresJournal criteres, CancellationToken cancellationToken);

    Task<IReadOnlyList<LigneVolume>> VolumesAsync(DateTimeOffset depuis, DateTimeOffset jusqua, CancellationToken cancellationToken);

    /// <summary>Échanges dont la charge utile est encore présente : traités avant <paramref name="traitesAvant"/>, ou en échec avant <paramref name="echecsAvant"/>.</summary>
    Task<IReadOnlyList<EchangeFlux>> ListerChargesAPurgerAsync(DateTimeOffset traitesAvant, DateTimeOffset echecsAvant, int maximum, CancellationToken cancellationToken);

    void Add(EchangeFlux echange);
}

public interface IPositionFluxRepository
{
    Task<PositionFlux?> GetAsync(TypeFlux flux, CancellationToken cancellationToken);

    void Add(PositionFlux position);
}

public interface ICorrespondanceRepository
{
    Task<CorrespondanceIdentifiant?> GetAsync(TypeIdentifiantExterne type, string valeurNormalisee, CancellationToken cancellationToken);

    Task<IReadOnlyList<CorrespondanceIdentifiant>> ListerAsync(TypeIdentifiantExterne? type, Guid? identifiantInterne, int maximum, CancellationToken cancellationToken);

    void Add(CorrespondanceIdentifiant correspondance);
}

public interface IEntrepriseBceRepository
{
    Task<EntrepriseBce?> GetParNumeroAsync(string numeroBce, CancellationToken cancellationToken);

    void Add(EntrepriseBce entreprise);
}

/// <summary>Durées de conservation des charges utiles (minimisation RGPD) ; configuration <c>Integrations:Conservation</c>.</summary>
public sealed class ConservationChargesUtiles
{
    /// <summary>Après un traitement réussi.</summary>
    public TimeSpan ApresTraitement { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Pour un échange rejeté ou en erreur : au-delà, il ne peut plus être relancé.</summary>
    public TimeSpan ApresEchec { get; set; } = TimeSpan.FromDays(30);
}
