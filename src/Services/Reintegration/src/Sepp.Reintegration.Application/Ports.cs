using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Projections;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Application;

/// <summary>Dossiers de réintégration (§15.3 <c>incapacite</c>, <c>estimation_potentiel</c>).</summary>
public interface IIncapaciteRepository
{
    Task<Incapacite?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Incapacite>> ListerParPersonneAsync(Guid personneId, CancellationToken cancellationToken);

    /// <summary>Incapacités en cours d'un travailleur chez un affilié (fin à la reprise annoncée).</summary>
    Task<IReadOnlyList<Incapacite>> ListerEnCoursAsync(Guid personneId, Guid affilieId, CancellationToken cancellationToken);

    /// <summary>Incapacités en cours (tableau de bord des délais, SAN-65), éventuellement pour un affilié.</summary>
    Task<IReadOnlyList<Incapacite>> ListerEnCoursAsync(Guid? affilieId, int maximum, CancellationToken cancellationToken);

    /// <summary>ADR 0008 : réserve les dossiers dont une minuterie est échue (<c>FOR UPDATE SKIP LOCKED</c>, dans la transaction de l'appelant).</summary>
    Task<IReadOnlyList<Incapacite>> ReserverEchuesAsync(DateOnly aujourdHui, int maximum, CancellationToken cancellationToken);

    void Add(Incapacite incapacite);
}

public interface ITrajetRepository
{
    Task<Trajet?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Trajet>> ListerParIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken);

    /// <summary>Trajet ouvert d'un travailleur chez un affilié (compteur d'absences, SAN-63).</summary>
    Task<Trajet?> GetOuvertAsync(Guid personneId, Guid affilieId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Trajet>> ListerParAffilieAsync(Guid affilieId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Trajet>> ListerOuvertsAsync(Guid? affilieId, int maximum, CancellationToken cancellationToken);

    /// <summary>Relation de soin : l'utilisateur a évalué un trajet de cette incapacité.</summary>
    Task<bool> ExisteEvaluationParAsync(Guid incapaciteId, string utilisateurId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Trajet>> ReserverEchusAsync(DateOnly aujourdHui, int maximum, CancellationToken cancellationToken);

    void Add(Trajet trajet);
}

public interface IEchangeEHealthRepository
{
    Task<EchangeEHealth?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<EchangeEHealth?> GetParReferenceAsync(string referenceEHealth, CancellationToken cancellationToken);

    Task<IReadOnlyList<EchangeEHealth>> ListerParIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EchangeEHealth>> ListerParTrajetAsync(Guid trajetId, CancellationToken cancellationToken);

    /// <summary>Envois en attente ou en erreur, réservés (<c>FOR UPDATE SKIP LOCKED</c>) dans la transaction de l'appelant.</summary>
    Task<IReadOnlyList<EchangeEHealth>> ReserverAEnvoyerAsync(int maximum, CancellationToken cancellationToken);

    void Add(EchangeEHealth echange);
}

/// <summary>Modèles de lecture alimentés par les événements des autres services (ARC-31).</summary>
public interface IProjectionRepository
{
    Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken);

    Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken);

    void Add(ParametreLegalLocal parametre);

    Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendrierLocal>> ListerCalendriersAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken);

    void Add(CalendrierLocal calendrier);

    Task<ObligationReintegration?> GetObligationAsync(Guid obligationId, CancellationToken cancellationToken);

    Task<bool> ExisteObligationAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken);

    void Add(ObligationReintegration obligation);
}

/// <summary>
/// Contexte d'accès de la requête : motif (accès hors relation de soin, §3.3), bris de glace, identifiants portés par le jeton
/// d'un utilisateur externe (claims <c>affilie_id</c>, <c>personne_id</c>).
/// </summary>
public interface IContexteAcces
{
    string? Motif { get; }

    bool BrisDeGlace { get; }

    Guid? AffilieIdJeton { get; }

    Guid? PersonneIdJeton { get; }
}

/// <summary>Message sortant remis au canal eHealth : le contenu ne quitte la zone médicale que par ce canal sécurisé.</summary>
public sealed record MessageEHealthSortant(Guid EchangeId, DestinataireEHealth Destinataire, string? ReferenceDestinataire, ObjetEchange Objet, Guid PersonneId, string Contenu)
{
    public override string ToString() => $"MessageEHealthSortant {{ EchangeId = {EchangeId}, Destinataire = {Destinataire}, Objet = {Objet}, Contenu = masqué }}";
}

public sealed record ResultatEnvoiEHealth(bool Envoye, string? ReferenceEHealth, string? CodeErreur)
{
    public static ResultatEnvoiEHealth Succes(string reference) => new(true, reference, null);

    public static ResultatEnvoiEHealth Echec(string code) => new(false, null, code);
}

/// <summary>Message reçu par eHealth en réponse à un envoi (référence d'origine = référence eHealth de l'envoi).</summary>
public sealed record MessageEHealthRecu(string ReferenceEHealth, string ReferenceOrigine, string Contenu, DateTimeOffset RecuLe)
{
    public override string ToString() => $"MessageEHealthRecu {{ ReferenceEHealth = {ReferenceEHealth}, Contenu = masqué }}";
}

/// <summary>
/// SAN-64 : échanges sécurisés eHealth (eHealthBox ou service retenu) avec le médecin traitant, le médecin-conseil et le
/// coordinateur Retour au travail. Adaptateur réel hors périmètre (INT-03, INT-04) : simulateur déterministe.
/// </summary>
public interface ICanalEHealth
{
    Task<ResultatEnvoiEHealth> EnvoyerAsync(MessageEHealthSortant message, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageEHealthRecu>> RecevoirAsync(CancellationToken cancellationToken);
}
