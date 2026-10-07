using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain.Agenda;

/// <summary>
/// PLA-03 : modèle d'agenda d'une ressource dans un lieu — plages par type d'acte avec leur durée standard,
/// jours de présence (jours portant au moins une plage). Historisé (DAT-04) : un nouveau modèle clôture le précédent.
/// </summary>
public sealed class ModeleAgenda : AggregateRoot
{
    private readonly List<PlageModele> _plages = [];

    private ModeleAgenda()
    {
    }

    private ModeleAgenda(Guid id) : base(id)
    {
    }

    public Guid RessourceId { get; private set; }

    public Guid LieuId { get; private set; }

    public Validity Validite { get; private set; }

    public IReadOnlyList<PlageModele> Plages => _plages.AsReadOnly();

    public IReadOnlyList<DayOfWeek> JoursPresence => _plages.Select(p => p.Jour).Distinct().Order().ToList();

    public static ModeleAgenda Creer(Guid ressourceId, Guid lieuId, Validity validite) =>
        new(NewId()) { RessourceId = ressourceId, LieuId = lieuId, Validite = validite };

    public PlageModele AjouterPlage(DayOfWeek jour, TimeOnly debut, TimeOnly fin, string typeActe, int dureeMinutes, bool reserveUrgence,
        bool ouvertEnLigne, IEnumerable<Guid>? ressourcesAssociees)
    {
        var plage = new PlageModele(NewId(), jour, debut, fin, CodeMetier.Normaliser(typeActe, "Type d'acte"), dureeMinutes, reserveUrgence,
            ouvertEnLigne && !reserveUrgence, (ressourcesAssociees ?? []).Where(r => r != RessourceId).Distinct().ToList());
        if (_plages.Any(p => p.Jour == jour && p.Debut < fin && debut < p.Fin))
        {
            throw new DomainException($"La plage du {jour} {debut:HH\\:mm}-{fin:HH\\:mm} chevauche une plage existante du modèle.");
        }

        _plages.Add(plage);
        return plage;
    }

    /// <summary>Clôture le modèle à une date (exclusive) : les créneaux ultérieurs relèvent du modèle suivant.</summary>
    public void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);

    /// <summary>
    /// Créneaux prévus par le modèle sur [du, au] (dates incluses), hors jours non ouvrables (week-ends et jours fériés).
    /// Chaque plage est découpée en créneaux successifs de sa durée standard ; un reliquat trop court est ignoré.
    /// </summary>
    public IEnumerable<CreneauPrevu> Projeter(DateOnly du, DateOnly au, Func<DateOnly, bool> estOuvrable)
    {
        for (var jour = du; jour <= au; jour = jour.AddDays(1))
        {
            if (!Validite.Contains(jour) || !estOuvrable(jour))
            {
                continue;
            }

            foreach (var plage in _plages.Where(p => p.Jour == jour.DayOfWeek).OrderBy(p => p.Debut))
            {
                var duree = TimeSpan.FromMinutes(plage.DureeMinutes);
                for (var debut = plage.Debut; debut.Add(duree) <= plage.Fin && debut.Add(duree) > debut; debut = debut.Add(duree))
                {
                    var debutUtc = HeureBelge.VersUtc(jour, debut);
                    yield return new CreneauPrevu(debutUtc, debutUtc.Add(duree), plage.TypeActe, plage.ReserveUrgence, plage.OuvertEnLigne,
                        plage.RessourcesAssociees);
                }
            }
        }
    }
}

/// <summary>Plage hebdomadaire d'un modèle d'agenda : un type d'acte, une durée standard, en heure belge.</summary>
public sealed class PlageModele : Entity
{
    private PlageModele()
    {
    }

    internal PlageModele(Guid id, DayOfWeek jour, TimeOnly debut, TimeOnly fin, string typeActe, int dureeMinutes, bool reserveUrgence,
        bool ouvertEnLigne, IReadOnlyList<Guid> ressourcesAssociees) : base(id)
    {
        if (fin <= debut)
        {
            throw new DomainException("La fin de la plage doit suivre son début.");
        }

        if (dureeMinutes is < 5 or > 480 || TimeSpan.FromMinutes(dureeMinutes) > fin - debut)
        {
            throw new DomainException("La durée d'un créneau est comprise entre 5 et 480 minutes et tient dans la plage.");
        }

        Jour = jour;
        Debut = debut;
        Fin = fin;
        TypeActe = typeActe;
        DureeMinutes = dureeMinutes;
        ReserveUrgence = reserveUrgence;
        OuvertEnLigne = ouvertEnLigne;
        RessourcesAssociees = ressourcesAssociees;
    }

    public DayOfWeek Jour { get; private set; }

    public TimeOnly Debut { get; private set; }

    public TimeOnly Fin { get; private set; }

    public string TypeActe { get; private set; } = string.Empty;

    public int DureeMinutes { get; private set; }

    /// <summary>PLA-06 : créneaux réservés aux urgences légales (reprise, consultation spontanée, pré-reprise).</summary>
    public bool ReserveUrgence { get; private set; }

    /// <summary>SAN-12 : créneaux ouverts à la réservation en ligne par le travailleur ou l'employeur.</summary>
    public bool OuvertEnLigne { get; private set; }

    /// <summary>Salle, cabine ou appareil mobilisés avec la ressource principale.</summary>
    public IReadOnlyList<Guid> RessourcesAssociees { get; private set; } = [];
}

/// <summary>Créneau calculé à partir d'un modèle, avant création.</summary>
public sealed record CreneauPrevu(
    DateTimeOffset Debut,
    DateTimeOffset Fin,
    string TypeActe,
    bool ReserveUrgence,
    bool OuvertEnLigne,
    IReadOnlyList<Guid> RessourcesAssociees);

/// <summary>
/// PLA-03 : durée standard d'un type d'acte, globale ou propre à un CPMT (ressource). La durée propre au CPMT
/// l'emporte sur la durée globale.
/// </summary>
public sealed class DureeStandard : AggregateRoot
{
    private DureeStandard()
    {
    }

    private DureeStandard(Guid id) : base(id)
    {
    }

    public string TypeActe { get; private set; } = string.Empty;

    /// <summary>Ressource (CPMT) concernée, ou <c>null</c> pour la durée par défaut du type d'acte.</summary>
    public Guid? RessourceId { get; private set; }

    public int DureeMinutes { get; private set; }

    public static DureeStandard Creer(string typeActe, Guid? ressourceId, int dureeMinutes)
    {
        var duree = new DureeStandard(NewId()) { TypeActe = CodeMetier.Normaliser(typeActe, "Type d'acte"), RessourceId = ressourceId };
        duree.Modifier(dureeMinutes);
        return duree;
    }

    public void Modifier(int dureeMinutes)
    {
        if (dureeMinutes is < 5 or > 480)
        {
            throw new DomainException("La durée standard est comprise entre 5 et 480 minutes.");
        }

        DureeMinutes = dureeMinutes;
    }

    /// <summary>Durée applicable pour une ressource : la durée propre à la ressource, sinon la durée globale.</summary>
    public static int? Resoudre(IEnumerable<DureeStandard> durees, string typeActe, Guid ressourceId)
    {
        var candidates = durees.Where(d => d.TypeActe == typeActe).ToList();
        return (candidates.FirstOrDefault(d => d.RessourceId == ressourceId) ?? candidates.FirstOrDefault(d => d.RessourceId is null))?.DureeMinutes;
    }
}
