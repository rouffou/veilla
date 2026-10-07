using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain.Agenda;

public enum StatutCreneau
{
    Libre,
    Reserve,

    /// <summary>Ressource indisponible (congé, absence, occupation d'agenda externe).</summary>
    Bloque,
}

/// <summary>
/// Créneau d'agenda (§15.3 : id, ressource_id, lieu_id, debut, fin, type_acte, statut, reserve_urgence).
/// Le créneau occupe sa ressource principale et ses ressources associées (salle, appareil, unité mobile) : chaque
/// occupation est une ligne <c>occupation_ressource</c> protégée en base par une contrainte d'exclusion
/// (une ressource ne peut pas être occupée deux fois sur des périodes qui se chevauchent).
/// </summary>
public sealed class Creneau : AggregateRoot
{
    private readonly List<OccupationRessource> _occupations = [];

    private Creneau()
    {
    }

    private Creneau(Guid id) : base(id)
    {
    }

    public Guid RessourceId { get; private set; }

    public Guid LieuId { get; private set; }

    public DateTimeOffset Debut { get; private set; }

    public DateTimeOffset Fin { get; private set; }

    public string TypeActe { get; private set; } = string.Empty;

    public StatutCreneau Statut { get; private set; }

    /// <summary>PLA-06 : créneau réservé aux urgences légales.</summary>
    public bool ReserveUrgence { get; private set; }

    /// <summary>SAN-12 : ouvert à la réservation en ligne.</summary>
    public bool OuvertEnLigne { get; private set; }

    public Guid? ModeleAgendaId { get; private set; }

    /// <summary>PLA-05 : session (tournée d'unité mobile) à laquelle appartient le créneau.</summary>
    public Guid? SessionId { get; private set; }

    public IReadOnlyList<OccupationRessource> Occupations => _occupations.AsReadOnly();

    public IEnumerable<Guid> RessourcesMobilisees => _occupations.Select(o => o.RessourceId);

    public static Creneau Creer(Guid ressourceId, Guid lieuId, DateTimeOffset debut, DateTimeOffset fin, string typeActe, bool reserveUrgence,
        bool ouvertEnLigne, IEnumerable<Guid>? ressourcesAssociees, Guid? modeleAgendaId = null, Guid? sessionId = null)
    {
        if (fin <= debut)
        {
            throw new DomainException("La fin du créneau doit suivre son début.");
        }

        var creneau = new Creneau(NewId())
        {
            RessourceId = ressourceId,
            LieuId = lieuId,
            Debut = debut.ToUniversalTime(),
            Fin = fin.ToUniversalTime(),
            TypeActe = CodeMetier.Normaliser(typeActe, "Type d'acte"),
            Statut = StatutCreneau.Libre,
            ReserveUrgence = reserveUrgence,
            OuvertEnLigne = ouvertEnLigne && !reserveUrgence,
            ModeleAgendaId = modeleAgendaId,
            SessionId = sessionId,
        };
        foreach (var ressource in new[] { ressourceId }.Concat(ressourcesAssociees ?? []).Distinct())
        {
            creneau._occupations.Add(new OccupationRessource(NewId(), ressource, creneau.Debut, creneau.Fin));
        }

        return creneau;
    }

    public bool Chevauche(DateTimeOffset debut, DateTimeOffset fin) => Debut < fin && debut < Fin;

    public bool Mobilise(Guid ressourceId) => _occupations.Any(o => o.RessourceId == ressourceId);

    /// <summary>
    /// Ouvert à une réservation ordinaire : libre, dans le futur et, s'il est réservé aux urgences, libéré parce qu'il
    /// commence dans moins de <paramref name="liberationUrgence"/> (un créneau d'urgence non utilisé n'est pas perdu).
    /// </summary>
    public bool EstOuvertAuxReservations(DateTimeOffset maintenant, TimeSpan liberationUrgence) =>
        Statut == StatutCreneau.Libre && Debut > maintenant && (!ReserveUrgence || Debut - maintenant <= liberationUrgence);

    public void Reserver()
    {
        if (Statut != StatutCreneau.Libre)
        {
            throw new DomainException("Ce créneau n'est plus disponible.");
        }

        Statut = StatutCreneau.Reserve;
    }

    public void Liberer()
    {
        if (Statut == StatutCreneau.Reserve)
        {
            Statut = StatutCreneau.Libre;
        }
    }

    /// <summary>Ressource indisponible : un créneau réservé doit d'abord être replanifié (PLA-07).</summary>
    public void Bloquer()
    {
        if (Statut == StatutCreneau.Reserve)
        {
            throw new DomainException("Un créneau réservé ne peut pas être bloqué : replanifiez d'abord le rendez-vous.");
        }

        Statut = StatutCreneau.Bloque;
    }

    public void Debloquer()
    {
        if (Statut == StatutCreneau.Bloque)
        {
            Statut = StatutCreneau.Libre;
        }
    }

    /// <summary>Retire le créneau de l'agenda : ses occupations sont libérées (la ligne du créneau est supprimée logiquement).</summary>
    public void Retirer()
    {
        if (Statut == StatutCreneau.Reserve)
        {
            throw new DomainException("Un créneau réservé ne peut pas être retiré.");
        }

        _occupations.Clear();
    }
}

/// <summary>Occupation d'une ressource par un créneau sur [Debut, Fin[ (contrainte d'exclusion en base).</summary>
public sealed class OccupationRessource : Entity
{
    private OccupationRessource()
    {
    }

    internal OccupationRessource(Guid id, Guid ressourceId, DateTimeOffset debut, DateTimeOffset fin) : base(id)
    {
        RessourceId = ressourceId;
        Debut = debut;
        Fin = fin;
    }

    public Guid RessourceId { get; private set; }

    public DateTimeOffset Debut { get; private set; }

    public DateTimeOffset Fin { get; private set; }
}
