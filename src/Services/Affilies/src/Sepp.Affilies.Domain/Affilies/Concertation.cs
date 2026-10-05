using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>Organe de concertation (AFF-04) : Comité PPT ou, à défaut, délégation syndicale.</summary>
public enum TypeOrgane
{
    ComitePpt,
    DelegationSyndicale,
}

/// <summary>
/// AFF-04 — Organes de concertation : présence d'un Comité PPT (période d'existence), réunions avec leur ordre du jour
/// (référence vers le service Documents, DAT-02) et participation du SEPP.
/// </summary>
public sealed partial class Affilie
{
    /// <summary>Enregistre l'existence d'un organe à partir d'une date ; un seul organe ouvert par type.</summary>
    public OrganeConcertation InstallerOrgane(TypeOrgane type, DateOnly depuis)
    {
        VerifierModifiable();
        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Type d'organe inconnu : {type}.");
        }

        var validite = new Validity(depuis);
        if (_organesConcertation.Any(o => o.Type == type && o.Validite.Overlaps(validite)))
        {
            throw new DomainException($"Un organe de type {type} existe déjà sur cette période.");
        }

        var organe = new OrganeConcertation(NewId(), type, validite);
        _organesConcertation.Add(organe);
        IncrementerVersion();
        return organe;
    }

    /// <summary>Dissolution de l'organe (fin exclusive) ; les réunions passées sont conservées.</summary>
    public void DissoudreOrgane(Guid organeId, DateOnly fin)
    {
        VerifierModifiable();
        var organe = Organe(organeId);
        var validite = Cloturer(organe.Validite, fin, "L'organe de concertation");
        if (organe.Reunions.Any(r => r.DateReunion >= fin))
        {
            throw new DomainException("Des réunions sont prévues après la date de dissolution.");
        }

        organe.Cloturer(validite);
        IncrementerVersion();
    }

    public ReunionConcertation PlanifierReunion(Guid organeId, DateOnly date, Guid? ordreDuJourDocumentId, bool participationSepp)
    {
        VerifierModifiable();
        var organe = Organe(organeId);
        VerifierDateReunion(organe, date, null);
        var reunion = new ReunionConcertation(NewId(), date, ordreDuJourDocumentId, participationSepp);
        organe.Ajouter(reunion);
        IncrementerVersion();
        return reunion;
    }

    /// <summary>Report, ajout de l'ordre du jour ou de la participation du SEPP.</summary>
    public void ModifierReunion(Guid organeId, Guid reunionId, DateOnly date, Guid? ordreDuJourDocumentId, bool participationSepp)
    {
        VerifierModifiable();
        var organe = Organe(organeId);
        var reunion = organe.Reunions.SingleOrDefault(r => r.Id == reunionId)
                      ?? throw new ElementIntrouvableException($"Réunion {reunionId} inconnue pour cet organe.");
        VerifierDateReunion(organe, date, reunionId);
        reunion.Modifier(date, ordreDuJourDocumentId, participationSepp);
        IncrementerVersion();
    }

    /// <summary>Présence d'un Comité PPT à une date (AFF-04).</summary>
    public bool DisposeDUnComitePptAu(DateOnly date) =>
        _organesConcertation.Any(o => o.Type == TypeOrgane.ComitePpt && o.Validite.Contains(date));

    private OrganeConcertation Organe(Guid organeId) =>
        _organesConcertation.SingleOrDefault(o => o.Id == organeId)
        ?? throw new ElementIntrouvableException($"Organe de concertation {organeId} inconnu pour cet affilié.");

    private static void VerifierDateReunion(OrganeConcertation organe, DateOnly date, Guid? reunionId)
    {
        if (!organe.Validite.Contains(date))
        {
            throw new DomainException($"La réunion du {date:yyyy-MM-dd} est en dehors de la période d'existence de l'organe.");
        }

        if (organe.Reunions.Any(r => r.DateReunion == date && r.Id != reunionId))
        {
            throw new DomainException($"Une réunion est déjà prévue le {date:yyyy-MM-dd} pour cet organe.");
        }
    }
}

public sealed class OrganeConcertation : Entity
{
    private readonly List<ReunionConcertation> _reunions = [];

    private OrganeConcertation()
    {
    }

    internal OrganeConcertation(Guid id, TypeOrgane type, Validity validite) : base(id)
    {
        Type = type;
        Validite = validite;
    }

    public TypeOrgane Type { get; private set; }

    public Validity Validite { get; private set; }

    public IReadOnlyList<ReunionConcertation> Reunions => _reunions.AsReadOnly();

    internal void Ajouter(ReunionConcertation reunion) => _reunions.Add(reunion);

    internal void Cloturer(Validity validite) => Validite = validite;
}

public sealed class ReunionConcertation : Entity
{
    private ReunionConcertation()
    {
    }

    internal ReunionConcertation(Guid id, DateOnly dateReunion, Guid? ordreDuJourDocumentId, bool participationSepp) : base(id)
    {
        Modifier(dateReunion, ordreDuJourDocumentId, participationSepp);
    }

    public DateOnly DateReunion { get; private set; }

    /// <summary>Ordre du jour, document géré par le service Documents (référence par identifiant, DAT-02).</summary>
    public Guid? OrdreDuJourDocumentId { get; private set; }

    /// <summary>Participation d'un conseiller en prévention du SEPP à la réunion.</summary>
    public bool ParticipationSepp { get; private set; }

    internal void Modifier(DateOnly dateReunion, Guid? ordreDuJourDocumentId, bool participationSepp)
    {
        if (ordreDuJourDocumentId == Guid.Empty)
        {
            throw new DomainException("L'identifiant du document d'ordre du jour est invalide.");
        }

        DateReunion = dateReunion;
        OrdreDuJourDocumentId = ordreDuJourDocumentId;
        ParticipationSepp = participationSepp;
    }
}
