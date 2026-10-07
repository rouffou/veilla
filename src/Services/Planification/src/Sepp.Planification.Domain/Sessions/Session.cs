using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain.Sessions;

public enum StatutSession
{
    Prevue,
    Annulee,
}

/// <summary>
/// Session de consultations chez un affilié ou sur un site, notamment tournée d'unité mobile (PLA-05, §15.3 :
/// id, lieu_id, affilie_id, date, itineraire). Porte le chauffeur, l'unité mobile, les emplacements et leurs
/// raccordements, et la capacité journalière (nombre de créneaux de la session).
/// </summary>
public sealed class Session : AggregateRoot
{
    private readonly List<EtapeTournee> _itineraire = [];

    private Session()
    {
    }

    private Session(Guid id) : base(id)
    {
    }

    public Guid LieuId { get; private set; }

    public Guid? AffilieId { get; private set; }

    public DateOnly Date { get; private set; }

    public Guid ConseillerId { get; private set; }

    public Guid? UniteMobileId { get; private set; }

    public Guid? ChauffeurId { get; private set; }

    public int CapaciteJournaliere { get; private set; }

    public string TypeActe { get; private set; } = string.Empty;

    public TimeOnly HeureDebut { get; private set; }

    public int DureeMinutes { get; private set; }

    public StatutSession Statut { get; private set; }

    public IReadOnlyList<EtapeTournee> Itineraire => _itineraire.AsReadOnly();

    public static Session Creer(Guid lieuId, Guid? affilieId, DateOnly date, Guid conseillerId, Guid? uniteMobileId, Guid? chauffeurId,
        int capaciteJournaliere, string typeActe, TimeOnly heureDebut, int dureeMinutes)
    {
        if (capaciteJournaliere is < 1 or > 200)
        {
            throw new DomainException("La capacité journalière d'une session est comprise entre 1 et 200 consultations.");
        }

        if (dureeMinutes is < 5 or > 480)
        {
            throw new DomainException("La durée d'une consultation est comprise entre 5 et 480 minutes.");
        }

        if ((uniteMobileId is null) != (chauffeurId is null))
        {
            throw new DomainException("Une tournée d'unité mobile précise l'unité et son chauffeur.");
        }

        if (heureDebut.Add(TimeSpan.FromMinutes((double)capaciteJournaliere * dureeMinutes), out var joursEnPlus) <= heureDebut || joursEnPlus > 0)
        {
            throw new DomainException("Les consultations de la session doivent tenir dans la journée.");
        }

        return new Session(NewId())
        {
            LieuId = lieuId,
            AffilieId = affilieId,
            Date = date,
            ConseillerId = conseillerId,
            UniteMobileId = uniteMobileId,
            ChauffeurId = chauffeurId,
            CapaciteJournaliere = capaciteJournaliere,
            TypeActe = CodeMetier.Normaliser(typeActe, "Type d'acte"),
            HeureDebut = heureDebut,
            DureeMinutes = dureeMinutes,
            Statut = StatutSession.Prevue,
        };
    }

    public bool EstTournee => UniteMobileId is not null;

    /// <summary>Remplace l'itinéraire : étapes ordonnées, emplacement, heure d'arrivée et raccordements nécessaires.</summary>
    public void DefinirItineraire(IEnumerable<(string Emplacement, TimeOnly? HeureArrivee, bool Electricite, bool Eau, bool Reseau)> etapes)
    {
        _itineraire.Clear();
        var ordre = 1;
        foreach (var etape in etapes)
        {
            _itineraire.Add(new EtapeTournee(NewId(), ordre++, etape.Emplacement, etape.HeureArrivee, etape.Electricite, etape.Eau, etape.Reseau));
        }
    }

    /// <summary>Horaires des créneaux de la session, en heure belge, convertis en UTC.</summary>
    public IEnumerable<(DateTimeOffset Debut, DateTimeOffset Fin)> Horaires()
    {
        var duree = TimeSpan.FromMinutes(DureeMinutes);
        for (var i = 0; i < CapaciteJournaliere; i++)
        {
            var debut = HeureBelge.VersUtc(Date, HeureDebut.Add(duree * i));
            yield return (debut, debut.Add(duree));
        }
    }

    public void Annuler() => Statut = StatutSession.Annulee;
}

/// <summary>Étape de l'itinéraire d'une tournée : emplacement de stationnement et raccordements (PLA-05).</summary>
public sealed class EtapeTournee : Entity
{
    private EtapeTournee()
    {
    }

    internal EtapeTournee(Guid id, int ordre, string emplacement, TimeOnly? heureArrivee, bool electricite, bool eau, bool reseau) : base(id)
    {
        if (string.IsNullOrWhiteSpace(emplacement) || emplacement.Trim().Length > 300)
        {
            throw new DomainException("L'emplacement d'une étape est obligatoire (300 caractères au plus).");
        }

        Ordre = ordre;
        Emplacement = emplacement.Trim();
        HeureArrivee = heureArrivee;
        RaccordementElectrique = electricite;
        RaccordementEau = eau;
        RaccordementReseau = reseau;
    }

    public int Ordre { get; private set; }

    /// <summary>Adresse ou description du point de stationnement chez l'affilié (jamais de donnée personnelle).</summary>
    public string Emplacement { get; private set; } = string.Empty;

    public TimeOnly? HeureArrivee { get; private set; }

    public bool RaccordementElectrique { get; private set; }

    public bool RaccordementEau { get; private set; }

    public bool RaccordementReseau { get; private set; }
}
