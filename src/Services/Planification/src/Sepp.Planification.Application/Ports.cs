using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Ressources;
using Sepp.Planification.Domain.Sessions;

namespace Sepp.Planification.Application;

public interface ILieuRepository
{
    Task<Lieu?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Lieu>> ListAsync(CancellationToken cancellationToken);

    void Add(Lieu lieu);
}

public interface IRessourceRepository
{
    Task<Ressource?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Ressource>> ListAsync(TypeRessource? type, CancellationToken cancellationToken);

    void Add(Ressource ressource);
}

public interface IAbsenceRepository
{
    Task<Absence?> GetParReferenceAsync(SourceAbsence source, string referenceExterne, CancellationToken cancellationToken);

    /// <summary>Indisponibilités qui chevauchent [du, au[ (toutes ressources si <paramref name="ressourceId"/> est nul).</summary>
    Task<IReadOnlyList<Absence>> ListAsync(Guid? ressourceId, DateTimeOffset du, DateTimeOffset au, CancellationToken cancellationToken);

    void Add(Absence absence);
}

public interface IModeleAgendaRepository
{
    Task<ModeleAgenda?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModeleAgenda>> ListAsync(Guid? ressourceId, CancellationToken cancellationToken);

    void Add(ModeleAgenda modele);
}

public interface IDureeStandardRepository
{
    Task<IReadOnlyList<DureeStandard>> ListAsync(string? typeActe, CancellationToken cancellationToken);

    void Add(DureeStandard duree);
}

/// <summary>Critères de recherche de créneaux : période [Du, Au[ sur le début du créneau, filtres facultatifs.</summary>
public sealed record CritereCreneaux(DateTimeOffset Du, DateTimeOffset Au)
{
    /// <summary>Ressource mobilisée par le créneau (principale ou associée).</summary>
    public Guid? Ressource { get; init; }

    public Guid? LieuId { get; init; }

    public Guid? SessionId { get; init; }

    public string? TypeActe { get; init; }

    public StatutCreneau? Statut { get; init; }

    public bool? ReserveUrgence { get; init; }

    public bool? OuvertEnLigne { get; init; }
}

public interface ICreneauRepository
{
    Task<Creneau?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Creneau>> RechercherAsync(CritereCreneaux critere, CancellationToken cancellationToken);

    /// <summary>Créneaux qui mobilisent l'une des ressources et chevauchent [du, au[ (contrôle des conflits).</summary>
    Task<IReadOnlyList<Creneau>> ChevauchantsAsync(IReadOnlyCollection<Guid> ressources, DateTimeOffset du, DateTimeOffset au, CancellationToken cancellationToken);

    void Add(Creneau creneau);

    void Remove(Creneau creneau);
}

public sealed record CritereRendezVous
{
    public Guid? PersonneId { get; init; }

    public Guid? AffilieId { get; init; }

    /// <summary>Ressource principale du rendez-vous.</summary>
    public Guid? RessourceId { get; init; }

    public Guid? LieuId { get; init; }

    /// <summary>Rendez-vous qui commencent dans [Du, Au[.</summary>
    public DateTimeOffset? Du { get; init; }

    public DateTimeOffset? Au { get; init; }

    public IReadOnlyCollection<StatutRendezVous>? Statuts { get; init; }

    public IReadOnlyCollection<Guid>? CreneauIds { get; init; }
}

public interface IRendezVousRepository
{
    Task<RendezVous?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RendezVous>> RechercherAsync(CritereRendezVous critere, CancellationToken cancellationToken);

    void Add(RendezVous rendezVous);
}

public interface IConvocationRepository
{
    Task<Convocation?> GetAsync(Guid convocationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Convocation>> ListAsync(Guid rendezVousId, CancellationToken cancellationToken);

    void Add(Convocation convocation);
}

public interface IPreferenceConvocationRepository
{
    Task<PreferenceConvocation?> GetAsync(Guid affilieId, CancellationToken cancellationToken);

    void Add(PreferenceConvocation preference);
}

public interface ISessionRepository
{
    Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Session>> ListAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken);

    void Add(Session session);
}

public interface IObligationRepository
{
    Task<ObligationAPlanifier?> GetAsync(Guid obligationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ObligationAPlanifier>> ListAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken);

    /// <summary>Obligations sans rendez-vous, dues au plus tard à l'horizon, pour les affiliés donnés (tous si nul).</summary>
    Task<IReadOnlyList<ObligationAPlanifier>> ListerAPlanifierAsync(IReadOnlyCollection<Guid>? affilieIds, DateOnly horizon, CancellationToken cancellationToken);

    Task<IReadOnlyList<ObligationAPlanifier>> ListerParPersonneAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(ObligationAPlanifier obligation);
}

/// <summary>Calendrier local des jours fériés supplémentaires, alimenté par <c>referentiels.jours-feries-modifies</c> (DAT-08).</summary>
public interface ICalendrierLocalRepository
{
    Task<CalendrierLocal?> GetAsync(int annee, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendrierLocal>> ListAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken);

    void Add(CalendrierLocal calendrier);
}

public interface IParametreLocalRepository
{
    Task<ParametreLegalLocal?> GetAsync(string code, DateOnly valideDu, CancellationToken cancellationToken);

    Task<ParametreLegalLocal?> ApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken);

    void Add(ParametreLegalLocal parametre);
}

/// <summary>
/// Périmètre de l'utilisateur issu du jeton (ADR 0005), évalué par ce service : un employeur n'accède qu'aux affiliés
/// de sa revendication <c>affilie_id</c>, un travailleur qu'à lui-même (revendication <c>personne_id</c>).
/// </summary>
public interface IPerimetreUtilisateur
{
    /// <summary>Utilisateur sans aucun rôle interne (employeur, SIPP, travailleur).</summary>
    bool EstExterne { get; }

    bool PeutAccederAffilie(Guid affilieId);

    /// <summary>Personne représentée par un travailleur connecté (revendication <c>personne_id</c>), sinon <c>null</c>.</summary>
    Guid? PersonneId { get; }
}

/// <summary>Congé lu dans l'outil RH existant (PLA-03). La ressource est rapprochée par sa référence (<see cref="Ressource.ReferenceId"/>).</summary>
public sealed record CongeRh(string ReferenceExterne, string ReferenceRessource, DateTimeOffset Debut, DateTimeOffset Fin);

/// <summary>
/// PLA-03 : outil RH existant, en <b>lecture seule</b>. L'outil réel n'est pas connu à ce stade : l'adaptateur lit un
/// export fichier (CSV) ou un simulateur ; un adaptateur d'API se substituera sans toucher au reste du service.
/// </summary>
public interface IOutilRh
{
    Task<IReadOnlyList<CongeRh>> LireCongesAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken);
}

/// <summary>Plage occupée lue dans un agenda externe.</summary>
public sealed record OccupationExterne(string Reference, DateTimeOffset Debut, DateTimeOffset Fin);

/// <summary>PLA-09 : l'agenda externe ne répond pas ou n'est pas configuré ; la synchronisation des autres ressources continue.</summary>
public sealed class AgendaExterneIndisponibleException : Exception
{
    public AgendaExterneIndisponibleException()
    {
    }

    public AgendaExterneIndisponibleException(string message) : base(message)
    {
    }

    public AgendaExterneIndisponibleException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>PLA-09 : agendas Microsoft 365 / Google des ressources humaines (lecture et écriture).</summary>
public interface IAgendaExterne
{
    /// <summary>Crée ou met à jour l'événement ; renvoie sa référence dans l'agenda externe.</summary>
    Task<string> EcrireAsync(FournisseurAgenda fournisseur, string compte, string? reference, EvenementAgenda evenement, CancellationToken cancellationToken);

    Task SupprimerAsync(FournisseurAgenda fournisseur, string compte, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<OccupationExterne>> LireOccupationsAsync(FournisseurAgenda fournisseur, string compte, DateTimeOffset du, DateTimeOffset au,
        CancellationToken cancellationToken);
}
