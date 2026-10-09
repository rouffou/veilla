using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application;

public interface IPersonneRepository
{
    /// <summary>Personne avec ses occupations, affectations et états particuliers.</summary>
    Task<Personne?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Recherche par index aveugle du NISS (DAT-06) : le NISS en clair n'est jamais une clé de requête.</summary>
    Task<Personne?> GetParNissHashAsync(string nissHash, CancellationToken cancellationToken);

    Task<Personne?> GetParReferenceDimonaAsync(string referenceDimona, CancellationToken cancellationToken);

    /// <summary>Personne dont l'historique contient la mutation du registre national portant cette référence (idempotence).</summary>
    Task<Personne?> GetParReferenceMutationAsync(string referenceMutation, CancellationToken cancellationToken);

    /// <summary>Personnes occupées (ou mises à disposition) chez l'affilié à la date donnée.</summary>
    Task<IReadOnlyList<Personne>> ListerParAffilieAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken);

    void Add(Personne personne);
}

/// <summary>Index de recherche par hachage à clé du NISS (DAT-06) ; la clé est détenue par l'infrastructure.</summary>
public interface INissIndex
{
    string Calculer(Niss niss);
}

/// <summary>Affilié porté par le jeton d'un utilisateur externe (claim <c>affilie_id</c>), s'il y en a un.</summary>
public interface IContexteAffilie
{
    Guid? AffilieId { get; }
}
