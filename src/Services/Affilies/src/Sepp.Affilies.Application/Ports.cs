using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Groupes;
using Sepp.Affilies.Domain.Historique;

namespace Sepp.Affilies.Application;

/// <summary>Critères de recherche d'affiliés ; <see cref="Parmi"/> restreint au périmètre d'un utilisateur externe.</summary>
public sealed record CriteresRecherche(NumeroBce? NumeroBce, string? Denomination, IReadOnlySet<Guid>? Parmi, int Page, int Taille);

public interface IAffilieRepository
{
    /// <summary>Agrégat complet (hiérarchie, contacts, organes, opérations).</summary>
    Task<Affilie?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Affilie?> GetParBceAsync(NumeroBce numeroBce, CancellationToken cancellationToken);

    Task<bool> ExisteAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> BceUtiliseAsync(NumeroBce numeroBce, CancellationToken cancellationToken);

    /// <summary>Une unité d'établissement n'appartient qu'à un seul affilié.</summary>
    Task<bool> UniteEtablissementUtiliseeAsync(NumeroUniteEtablissement numero, CancellationToken cancellationToken);

    Task<PageDto<AffilieResumeDto>> RechercherAsync(CriteresRecherche criteres, CancellationToken cancellationToken);

    void Add(Affilie affilie);
}

public interface IGroupeRepository
{
    Task<Groupe?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Groupe>> ListAsync(CancellationToken cancellationToken);

    void Add(Groupe groupe);
}

/// <summary>AFF-05 — Historique des modifications, alimenté dans la transaction de la modification.</summary>
public interface IHistoriqueAffilieRepository
{
    void Add(ModificationAffilie modification);

    Task<IReadOnlyList<ModificationAffilie>> ListAsync(Guid affilieId, CancellationToken cancellationToken);
}

/// <summary>
/// Périmètre de l'utilisateur externe (employeur, SIPP) : affiliés qu'il représente, lus dans le jeton
/// (claim <c>affilie_id</c>, une valeur par affilié). Vide pour un utilisateur interne.
/// </summary>
public interface IPerimetreUtilisateur
{
    IReadOnlySet<Guid> AffiliesAutorises { get; }
}
