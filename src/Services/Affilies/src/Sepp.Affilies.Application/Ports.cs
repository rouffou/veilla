using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Ecarts;
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

/// <summary>Écarts de synchronisation BCE (AFF-01, AFF-02, INT-04) consignés pour le gestionnaire de dossiers.</summary>
public interface IEcartSynchronisationRepository
{
    Task<EcartSynchronisation?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Écarts encore ouverts d'un numéro BCE (suivis, mis à jour ou clos par la synchronisation).</summary>
    Task<IReadOnlyList<EcartSynchronisation>> OuvertsParBceAsync(string numeroBce, CancellationToken cancellationToken);

    Task<IReadOnlyList<EcartSynchronisation>> ListAsync(bool ouvertsSeulement, CancellationToken cancellationToken);

    void Add(EcartSynchronisation ecart);
}

/// <summary>
/// Lecture des données d'entreprise de la BCE chez Intégrations (<c>GET /api/v1/bce/entreprises/{numeroBce}</c>) : l'événement
/// <c>donnees-bce-recues</c> ne porte ni adresses ni dénominations d'unités (ARC-06).
/// </summary>
public interface IEntrepriseBceClient
{
    /// <summary>Dernières données reçues, ou <c>null</c> si Intégrations n'en a pas. Une panne technique lève <see cref="EntrepriseBceIndisponibleException"/>.</summary>
    Task<EntrepriseBce?> LireAsync(string numeroBce, CancellationToken cancellationToken);
}

public sealed record EntrepriseBce(string NumeroBce, string Denomination, string FormeJuridique, string CodeNace, DateOnly DateExtraction, IReadOnlyList<UniteBce> Unites);

/// <summary>Échec technique (indisponibilité, accès refusé) : le message est repris plus tard, il n'est pas consigné comme écart.</summary>
public sealed class EntrepriseBceIndisponibleException : Exception
{
    public EntrepriseBceIndisponibleException(string message) : base(message)
    {
    }

    public EntrepriseBceIndisponibleException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public EntrepriseBceIndisponibleException()
    {
    }
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
