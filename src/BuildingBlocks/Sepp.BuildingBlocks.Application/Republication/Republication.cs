using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Application.Republication;

/// <summary>
/// Lot d'instantanés lu par une <see cref="ISourceRepublication"/> : événements à écrire dans l'outbox et curseur de reprise.
/// </summary>
/// <param name="Instantanes">Événements « instantané » (état courant), dans l'ordre stable de la clé de la source.</param>
/// <param name="CurseurSuivant">Position après le dernier élément lu ; <c>null</c> quand la source est épuisée.</param>
public sealed record LotRepublication(IReadOnlyList<IntegrationEvent> Instantanes, string? CurseurSuivant);

/// <summary>
/// Rattrapage des projections (ADR 0011, ARC-31) : état courant d'un type d'objet d'un producteur, relu par lots dans l'ordre
/// d'une clé stable et traduit en événements « instantané » dédiés. Implémentée par l'adaptateur de persistance du producteur.
/// Un instantané ne porte que ce qu'ARC-06 autorise dans un événement et l'horodatage de l'état (<c>EtatDu</c>).
/// </summary>
public interface ISourceRepublication
{
    /// <summary>Type d'état republié, en kebab-case (par ex. <c>occupations</c>), choisi par l'exploitant.</summary>
    string Type { get; }

    /// <summary>Lit au plus <paramref name="taille"/> instantanés après <paramref name="curseur"/> (<c>null</c> : depuis le début).</summary>
    Task<LotRepublication> LireAsync(string? curseur, int taille, CancellationToken cancellationToken);
}

/// <summary>Suivi d'une republication, sans aucune donnée métier : type, statut et compteurs.</summary>
/// <param name="Statut"><c>EnCours</c>, <c>Terminee</c> ou <c>Echouee</c>.</param>
/// <param name="Publies">Nombre d'instantanés écrits dans l'outbox (publiés ensuite par le processeur de l'outbox).</param>
public sealed record RepublicationDto(
    Guid Id,
    string Type,
    string Statut,
    int Publies,
    int Lots,
    string DemandeePar,
    DateTimeOffset DemandeeLe,
    DateTimeOffset? TermineeLe,
    string? Erreur);

/// <summary>
/// Cas d'usage d'exploitation (ADR 0011) : demander la republication d'un type d'état et en suivre l'avancement. Réservé à la
/// permission <c>plateforme:republier</c>, vérifiée par l'implémentation (ARC-41).
/// </summary>
public interface IRepublications
{
    /// <summary>Types d'état que le service sait republier.</summary>
    IReadOnlyList<string> Types { get; }

    /// <summary>Crée une republication en cours ; refusée si une autre du même type est déjà en cours (conflit).</summary>
    Task<Result<RepublicationDto>> DemanderAsync(string type, CancellationToken cancellationToken);

    Task<Result<RepublicationDto>> ConsulterAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Republications les plus récentes d'abord.</summary>
    Task<Result<IReadOnlyList<RepublicationDto>>> ListerAsync(CancellationToken cancellationToken);
}
