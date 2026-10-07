using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Application;

public interface IObligationRepository
{
    Task<Obligation?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Toutes les obligations d'un travailleur, avec leurs traces de calcul.</summary>
    Task<IReadOnlyList<Obligation>> ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken);

    /// <summary>Obligations d'un affilié (sans les traces) ; seulement les ouvertes si demandé.</summary>
    Task<IReadOnlyList<Obligation>> ListParAffilieAsync(Guid affilieId, bool ouvertesSeulement, CancellationToken cancellationToken);

    /// <summary>Travailleurs ayant au moins une obligation ouverte.</summary>
    Task<IReadOnlyList<Guid>> PersonnesAvecObligationsOuvertesAsync(CancellationToken cancellationToken);

    /// <summary>Obligations ouvertes dont la date limite est dépassée et pas encore signalée (ObligationEchue).</summary>
    Task<IReadOnlyList<Obligation>> ListEchuesNonSignaleesAsync(DateOnly aujourdHui, int nombreMaximum, CancellationToken cancellationToken);

    void Add(Obligation obligation);
}

public interface IDemandeRepository
{
    Task<IReadOnlyList<DemandeTravailleur>> ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> PersonnesAsync(CancellationToken cancellationToken);

    void Add(DemandeTravailleur demande);
}

/// <summary>Modèles de lecture alimentés par les événements des autres services (ARC-31).</summary>
public interface IProjectionRepository
{
    Task<AffectationLocale?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AffectationLocale>> AffectationsDeAsync(Guid personneId, CancellationToken cancellationToken);

    /// <summary>Affectations en cours à la date des travailleurs donnés.</summary>
    Task<IReadOnlyList<AffectationLocale>> AffectationsActivesAsync(IReadOnlyCollection<Guid> personneIds, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Travailleurs ayant (eu) une affectation sur l'un des postes.</summary>
    Task<IReadOnlyList<Guid>> PersonnesAffecteesAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken);

    void Add(AffectationLocale affectation);

    Task<ProfilRisquePosteLocal?> GetProfilAsync(Guid posteId, DateOnly valideDu, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfilRisquePosteLocal>> ProfilsDesPostesAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken);

    /// <summary>Postes dont un profil (actuel ou passé) contient le risque.</summary>
    Task<IReadOnlyList<Guid>> PostesExposesAuRisqueAsync(string codeRisque, CancellationToken cancellationToken);

    void Add(ProfilRisquePosteLocal profil);

    Task<RegleSurveillanceLocale?> GetRegleAsync(string codeRisque, int version, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegleSurveillanceLocale>> ReglesAsync(IReadOnlyCollection<string> codesRisques, CancellationToken cancellationToken);

    void Add(RegleSurveillanceLocale regle);

    Task<SurchargeFrequenceLocale?> GetSurchargeAsync(Guid surchargeId, CancellationToken cancellationToken);

    /// <summary>Surcharges visant le travailleur ou l'un des postes.</summary>
    Task<IReadOnlyList<SurchargeFrequenceLocale>> SurchargesAsync(Guid personneId, IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken);

    void Add(SurchargeFrequenceLocale surcharge);

    Task<OccupationLocale?> GetOccupationAsync(Guid occupationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OccupationLocale>> OccupationsDeAsync(Guid personneId, CancellationToken cancellationToken);

    /// <summary>Travailleurs occupés (occupation en cours) chez l'affilié à la date.</summary>
    Task<IReadOnlyList<Guid>> PersonnesOccupeesAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken);

    void Add(OccupationLocale occupation);

    Task<EtatParticulierLocal?> GetEtatParticulierAsync(Guid etatParticulierId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EtatParticulierLocal>> EtatsParticuliersDeAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(EtatParticulierLocal etat);

    Task<ExamenLocal?> GetExamenAsync(Guid examenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExamenLocal>> ExamensDeAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(ExamenLocal examen);

    Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken);

    Task<IReadOnlyList<ParametreLegalLocal>> ParametresAsync(CancellationToken cancellationToken);

    void Add(ParametreLegalLocal parametre);

    Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendrierLocal>> CalendriersAsync(CancellationToken cancellationToken);

    void Add(CalendrierLocal calendrier);

    Task<RendezVousLocal?> GetRendezVousAsync(Guid rendezVousId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RendezVousLocal>> RendezVousDeAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(RendezVousLocal rendezVous);

    Task<RepriseLocale?> GetRepriseAsync(Guid personneId, Guid affilieId, DateOnly dateReprise, CancellationToken cancellationToken);

    Task<IReadOnlyList<RepriseLocale>> ReprisesDeAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(RepriseLocale reprise);

    Task<IncapaciteLocale?> GetIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncapaciteLocale>> IncapacitesDeAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(IncapaciteLocale incapacite);

    Task<TrajetLocal?> GetTrajetAsync(Guid trajetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrajetLocal>> TrajetsDeAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(TrajetLocal trajet);

    /// <summary>Travailleurs ayant une reprise, une incapacité ou un trajet : leurs échéances dépendent des délais légaux.</summary>
    Task<IReadOnlyList<Guid>> PersonnesAvecEvenementsAsync(CancellationToken cancellationToken);

    Task<ListeNominativeLocale?> GetListeNominativeAsync(Guid listeNominativeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ListeNominativeLocale>> ListesNominativesAsync(Guid affilieId, CancellationToken cancellationToken);

    void Add(ListeNominativeLocale liste);
}

/// <summary>
/// Périmètre de l'utilisateur (ADR 0005) : un utilisateur interne voit tous les affiliés ; un employeur ou un SIPP
/// ne voit que les affiliés de sa revendication <c>affilie_id</c>, vérifiée par ce service.
/// </summary>
public interface IPerimetreAffilies
{
    bool EstExterne { get; }

    bool PeutAcceder(Guid affilieId);
}
