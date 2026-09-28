using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Projections;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

namespace Sepp.PostesRisques.Application;

public interface IPosteRepository
{
    Task<Poste?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Poste>> ListAsync(Guid affilieId, StatutPoste? statut, CancellationToken cancellationToken);

    void Add(Poste poste);
}

public interface IPropositionPosteRisqueRepository
{
    Task<PropositionPosteRisque?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PropositionPosteRisque>> ListAsync(Guid? affilieId, Guid? posteId, StatutProposition? statut, CancellationToken cancellationToken);

    void Add(PropositionPosteRisque proposition);
}

public interface IRisqueRepository
{
    Task<Risque?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Risque?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Risque>> ListAsync(CancellationToken cancellationToken);

    void Add(Risque risque);
}

public interface ISurchargeFrequenceRepository
{
    Task<SurchargeFrequence?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SurchargeFrequence>> ListAsync(Guid? affilieId, CibleSurcharge? cibleType, Guid? cibleId, CancellationToken cancellationToken);

    void Add(SurchargeFrequence surcharge);
}

public interface IListeNominativeRepository
{
    Task<ListeNominative?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ListeNominative>> ListAsync(Guid affilieId, TypeListeNominative? type, CancellationToken cancellationToken);

    /// <summary>Dernier numéro de version pour un affilié et un type (0 s'il n'y a encore aucune liste).</summary>
    Task<int> DerniereVersionAsync(Guid affilieId, TypeListeNominative type, CancellationToken cancellationToken);

    void Add(ListeNominative liste);
}

public interface IPropositionListeRepository
{
    Task<PropositionListeNominative?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PropositionListeNominative>> ListAsync(Guid? affilieId, StatutProposition? statut, CancellationToken cancellationToken);

    void Add(PropositionListeNominative proposition);
}

/// <summary>Modèles de lecture alimentés par les événements des autres services (ARC-31).</summary>
public interface IProjectionRepository
{
    Task<AffectationPoste?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken);

    void Add(AffectationPoste affectation);

    /// <summary>Affectations en cours à une date sur les postes donnés.</summary>
    Task<IReadOnlyList<AffectationPoste>> ListAffectationsActivesAsync(IReadOnlyCollection<Guid> posteIds, DateOnly date, CancellationToken cancellationToken);

    Task<ExamenRealise?> GetExamenAsync(Guid examenId, CancellationToken cancellationToken);

    void Add(ExamenRealise examen);

    /// <summary>Date du dernier examen clôturé (au plus tard à la date donnée) par travailleur, chez un affilié.</summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> DernieresEvaluationsAsync(
        Guid affilieId, IReadOnlyCollection<Guid> personneIds, DateOnly date, CancellationToken cancellationToken);

    Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken);

    /// <summary>Valeur la plus récente en vigueur à une date (date d'effet maximale inférieure ou égale).</summary>
    Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken);

    void Add(ParametreLegalLocal parametre);
}

/// <summary>
/// Périmètre de l'utilisateur (ADR 0005) : un utilisateur interne voit tous les affiliés ; un employeur ou un SIPP
/// ne voit que les affiliés pour lesquels il est habilité (revendication du jeton), vérifié par ce service.
/// </summary>
public interface IPerimetreAffilies
{
    /// <summary>Utilisateur externe (employeur, SIPP) : ses propositions viennent du portail.</summary>
    bool EstExterne { get; }

    bool PeutAcceder(Guid affilieId);
}
