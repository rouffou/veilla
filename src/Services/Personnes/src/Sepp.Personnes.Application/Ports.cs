using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application;

public interface IPersonneRepository
{
    /// <summary>Personne avec ses occupations, affectations et états particuliers.</summary>
    Task<Personne?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Recherche par index aveugle du NISS (DAT-06) : le NISS en clair n'est jamais une clé de requête. Toutes les
    /// empreintes acceptées sont cherchées (<see cref="INissIndex.Empreintes"/>, rotation de la clé, #473) ; si deux lignes
    /// correspondent, celle de la première empreinte (clé courante) l'emporte.
    /// </summary>
    Task<Personne?> GetParEmpreintesNissAsync(IReadOnlyList<string> empreintes, CancellationToken cancellationToken);

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
    /// <summary>Empreinte avec la clé courante : la seule qui soit écrite (création d'une personne).</summary>
    string Calculer(Niss niss);

    /// <summary>
    /// Empreintes acceptées en lecture : clé courante d'abord, puis clé précédente pendant une rotation de la clé (#473).
    /// Toute recherche par NISS et tout contrôle de doublon portent sur toutes ces empreintes.
    /// </summary>
    IReadOnlyList<string> Empreintes(Niss niss);
}

/// <summary>Affilié porté par le jeton d'un utilisateur externe (claim <c>affilie_id</c>), s'il y en a un.</summary>
public interface IContexteAffilie
{
    Guid? AffilieId { get; }
}

/// <summary>Personne du jeton d'un travailleur (claim <c>personne_id</c>, ADR 0005) : seule source de son identité pour « mes occupations ».</summary>
public interface IContextePersonne
{
    /// <summary>Personne du jeton ; <c>null</c> si le claim est absent, invalide ou porte plusieurs personnes.</summary>
    Guid? PersonneId { get; }
}
