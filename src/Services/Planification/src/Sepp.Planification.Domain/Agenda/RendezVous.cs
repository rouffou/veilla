using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain.Agenda;

public enum StatutRendezVous
{
    Planifie,

    /// <summary>PLA-08 : arrivé, enregistré à la borne ou à l'accueil, en file d'attente.</summary>
    Arrive,

    /// <summary>PLA-08 : appelé en salle.</summary>
    EnSalle,
    Termine,

    /// <summary>SAN-13 : ne s'est pas présenté.</summary>
    Absent,
    Annule,
}

/// <summary>Comment le rendez-vous a été pris.</summary>
public enum OrigineRendezVous
{
    Planificateur,

    /// <summary>SAN-12 : réservation en ligne par le travailleur ou l'employeur.</summary>
    ReservationEnLigne,

    /// <summary>PLA-06 : créneau d'urgence réservé automatiquement (saga de reprise, §14.6).</summary>
    Urgence,

    /// <summary>PLA-04 : affectation automatique dans une session.</summary>
    Session,

    /// <summary>SAN-13 : reconvocation après une absence.</summary>
    Reconvocation,
}

/// <summary>Motif d'annulation : un code, jamais un texte libre (ARC-06, aucune donnée de santé).</summary>
public enum MotifAnnulation
{
    DemandeTravailleur,
    DemandeEmployeur,

    /// <summary>PLA-07 : ressource absente et aucun créneau de remplacement.</summary>
    AbsenceRessource,
    ObligationLevee,
    Autre,
}

/// <summary>
/// Rendez-vous (§15.3 : id, creneau_id, personne_id, affilie_id, statut, motif_annulation). Couvre une ou plusieurs
/// obligations (SAN-03). Le créneau, la ressource, le lieu et les horaires sont recopiés pour la consultation.
/// </summary>
public sealed class RendezVous : AggregateRoot
{
    private static readonly StatutRendezVous[] Actifs = [StatutRendezVous.Planifie, StatutRendezVous.Arrive, StatutRendezVous.EnSalle];

    private RendezVous()
    {
    }

    private RendezVous(Guid id) : base(id)
    {
    }

    public Guid CreneauId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public Guid RessourceId { get; private set; }

    public Guid LieuId { get; private set; }

    public DateTimeOffset Debut { get; private set; }

    public DateTimeOffset Fin { get; private set; }

    public string TypeActe { get; private set; } = string.Empty;

    public StatutRendezVous Statut { get; private set; }

    public MotifAnnulation? MotifAnnulation { get; private set; }

    public IReadOnlyList<Guid> ObligationIds { get; private set; } = [];

    public OrigineRendezVous Origine { get; private set; }

    public bool Urgent { get; private set; }

    public DateTimeOffset PlanifieLe { get; private set; }

    public DateTimeOffset? ArriveeA { get; private set; }

    public DateTimeOffset? AppeleA { get; private set; }

    /// <summary>Salle ou cabine dans laquelle la personne est appelée (PLA-08).</summary>
    public Guid? SalleId { get; private set; }

    /// <summary>SAN-13 : premier rappel (J-7 par défaut) émis.</summary>
    public DateTimeOffset? Rappel1EmisLe { get; private set; }

    /// <summary>SAN-13 : second rappel (J-1 par défaut) émis.</summary>
    public DateTimeOffset? Rappel2EmisLe { get; private set; }

    /// <summary>Rendez-vous manqué que celui-ci remplace (reconvocation, SAN-13).</summary>
    public Guid? ReconvocationDeId { get; private set; }

    /// <summary>PLA-09 : identifiant de l'événement dans l'agenda externe de la ressource.</summary>
    public string? ReferenceAgendaExterne { get; private set; }

    public bool EstActif => Actifs.Contains(Statut);

    public static IReadOnlyList<StatutRendezVous> StatutsActifs => Actifs;

    /// <summary>Réserve le créneau et crée le rendez-vous dans la même transaction.</summary>
    public static RendezVous Planifier(Creneau creneau, Guid personneId, Guid affilieId, IEnumerable<Guid> obligationIds, OrigineRendezVous origine,
        bool urgent, DateTimeOffset maintenant, Guid? reconvocationDeId = null)
    {
        if (creneau.Debut <= maintenant)
        {
            throw new DomainException("Un rendez-vous ne peut pas être planifié dans le passé.");
        }

        creneau.Reserver();
        return new RendezVous(NewId())
        {
            CreneauId = creneau.Id,
            PersonneId = personneId,
            AffilieId = affilieId,
            RessourceId = creneau.RessourceId,
            LieuId = creneau.LieuId,
            Debut = creneau.Debut,
            Fin = creneau.Fin,
            TypeActe = creneau.TypeActe,
            Statut = StatutRendezVous.Planifie,
            ObligationIds = obligationIds.Distinct().ToList(),
            Origine = origine,
            Urgent = urgent,
            PlanifieLe = maintenant,
            ReconvocationDeId = reconvocationDeId,
        };
    }

    public void Annuler(MotifAnnulation motif, Creneau creneau, DateTimeOffset maintenant)
    {
        if (Statut != StatutRendezVous.Planifie)
        {
            throw new DomainException($"Un rendez-vous {Statut} ne peut pas être annulé.");
        }

        Statut = StatutRendezVous.Annule;
        MotifAnnulation = motif;
        if (creneau.Id == CreneauId && creneau.Debut > maintenant)
        {
            creneau.Liberer();
        }
    }

    /// <summary>PLA-07 : déplace le rendez-vous vers un autre créneau ; l'ancien est libéré.</summary>
    public DateTimeOffset Deplacer(Creneau ancien, Creneau nouveau, DateTimeOffset maintenant)
    {
        if (Statut != StatutRendezVous.Planifie)
        {
            throw new DomainException($"Un rendez-vous {Statut} ne peut pas être déplacé.");
        }

        if (ancien.Id != CreneauId || nouveau.Id == CreneauId)
        {
            throw new DomainException("Le déplacement doit partir du créneau du rendez-vous vers un autre créneau.");
        }

        if (nouveau.Debut <= maintenant)
        {
            throw new DomainException("Un rendez-vous ne peut pas être déplacé dans le passé.");
        }

        nouveau.Reserver();
        ancien.Liberer();
        var ancienDebut = Debut;
        CreneauId = nouveau.Id;
        RessourceId = nouveau.RessourceId;
        LieuId = nouveau.LieuId;
        Debut = nouveau.Debut;
        Fin = nouveau.Fin;
        Rappel1EmisLe = null;
        Rappel2EmisLe = null;
        return ancienDebut;
    }

    /// <summary>PLA-08 : enregistrement à l'arrivée (borne ou accueil), le jour du rendez-vous.</summary>
    public void EnregistrerArrivee(DateTimeOffset maintenant)
    {
        if (Statut != StatutRendezVous.Planifie)
        {
            throw new DomainException($"Arrivée impossible : le rendez-vous est {Statut}.");
        }

        if (HeureBelge.Jour(maintenant) != HeureBelge.Jour(Debut))
        {
            throw new DomainException("L'arrivée s'enregistre le jour du rendez-vous.");
        }

        Statut = StatutRendezVous.Arrive;
        ArriveeA = maintenant;
    }

    /// <summary>PLA-08 : appel en salle.</summary>
    public void Appeler(Guid? salleId, DateTimeOffset maintenant)
    {
        if (Statut != StatutRendezVous.Arrive)
        {
            throw new DomainException("Seule une personne en salle d'attente peut être appelée.");
        }

        Statut = StatutRendezVous.EnSalle;
        AppeleA = maintenant;
        SalleId = salleId;
    }

    public void Terminer()
    {
        if (Statut != StatutRendezVous.EnSalle)
        {
            throw new DomainException("Seul un rendez-vous en cours peut être terminé.");
        }

        Statut = StatutRendezVous.Termine;
    }

    /// <summary>SAN-13 : la personne ne s'est pas présentée.</summary>
    public void ConstaterAbsence(DateTimeOffset maintenant)
    {
        if (Statut != StatutRendezVous.Planifie)
        {
            throw new DomainException($"Absence impossible : le rendez-vous est {Statut}.");
        }

        if (Debut > maintenant)
        {
            throw new DomainException("L'absence se constate après l'heure du rendez-vous.");
        }

        Statut = StatutRendezVous.Absent;
    }

    /// <summary>
    /// SAN-13 : numéro du rappel à émettre aujourd'hui (1 ou 2), ou <c>null</c>. Le rappel <c>k</c> est dû à partir de
    /// J-<c>delaiK</c> (jours calendrier, heure belge) et jusqu'à la veille du rendez-vous ; il est sans objet si le
    /// rendez-vous a été pris après cette date (la convocation vient d'être envoyée). Le second rappel rend le premier
    /// sans objet.
    /// </summary>
    public int? RappelDu(DateOnly aujourdhui, int delai1, int delai2)
    {
        if (Statut != StatutRendezVous.Planifie)
        {
            return null;
        }

        var jourRdv = HeureBelge.Jour(Debut);
        var jourPlanification = HeureBelge.Jour(PlanifieLe);
        if (aujourdhui >= jourRdv)
        {
            return null;
        }

        bool Du(int delai) => delai > 0 && aujourdhui >= jourRdv.AddDays(-delai) && jourPlanification < jourRdv.AddDays(-delai);

        if (Rappel2EmisLe is null && Du(delai2))
        {
            return 2;
        }

        return Rappel1EmisLe is null && Rappel2EmisLe is null && Du(delai1) && !(delai2 > 0 && aujourdhui >= jourRdv.AddDays(-delai2)) ? 1 : null;
    }

    public void MarquerRappel(int numero, DateTimeOffset maintenant)
    {
        switch (numero)
        {
            case 1:
                Rappel1EmisLe = maintenant;
                break;
            case 2:
                Rappel2EmisLe = maintenant;
                break;
            default:
                throw new DomainException("Seuls deux rappels sont prévus.");
        }
    }

    public void LierAgendaExterne(string? reference) =>
        ReferenceAgendaExterne = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
}
