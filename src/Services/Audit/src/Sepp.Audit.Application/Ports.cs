using Sepp.Audit.Domain.Journal;

namespace Sepp.Audit.Application;

/// <summary>Critères de recherche dans le journal, déjà restreints aux zones visibles par l'utilisateur.</summary>
public sealed record CritereRecherche(
    IReadOnlyCollection<Zone> Zones,
    string? UtilisateurId,
    string? ObjetType,
    Guid? ObjetId,
    DateTimeOffset? Du,
    DateTimeOffset? Au,
    bool? BrisDeGlace,
    int Page,
    int Taille);

public sealed record PageEntrees(IReadOnlyList<EntreeAudit> Entrees, int Total);

/// <summary>Journal d'audit en ajout seul (NF-04).</summary>
public interface IJournalAuditRepository
{
    /// <summary>
    /// Verrouille la chaîne de la zone jusqu'à la fin de la transaction en cours (obligatoire) et renvoie son dernier
    /// maillon : dernière entrée, sinon sceau de la dernière purge, sinon l'origine. Les écritures concurrentes d'une même
    /// zone sont ainsi sérialisées ; celles de zones différentes restent parallèles.
    /// </summary>
    Task<MaillonChaine> VerrouillerDernierMaillonAsync(Zone zone, CancellationToken cancellationToken);

    Task<bool> ExisteEvenementAsync(Guid evenementId, CancellationToken cancellationToken);

    void Ajouter(EntreeAudit entree);

    Task<EntreeAudit?> ObtenirAsync(Guid id, CancellationToken cancellationToken);

    Task<PageEntrees> RechercherAsync(CritereRecherche critere, CancellationToken cancellationToken);

    /// <summary>Entrées de la zone de numéro strictement supérieur à <paramref name="apresNumero"/>, dans l'ordre de la chaîne.</summary>
    IAsyncEnumerable<EntreeAudit> ParcourirChaineAsync(Zone zone, long apresNumero, CancellationToken cancellationToken);

    Task<SceauPurge?> DernierSceauAsync(Zone zone, CancellationToken cancellationToken);

    /// <summary>
    /// Purge légale : supprime le plus long préfixe de la chaîne de la zone dont toutes les entrées sont antérieures
    /// à <paramref name="limite"/>. La base refuse toute autre suppression et scelle la purge.
    /// </summary>
    Task<int> PurgerAvantAsync(Zone zone, DateTimeOffset limite, CancellationToken cancellationToken);
}
