using Sepp.BuildingBlocks.Domain;

namespace Sepp.Personnes.Domain.Personnes;

/// <summary>AFF-23, AFF-24 : états particuliers ouvrant une protection ou une surveillance spécifique.</summary>
public enum TypeEtatParticulier
{
    Grossesse,
    Allaitement,
    TravailDeNuit,
    Jeune,
}

/// <summary>
/// État particulier d'une personne sur une période (§15.3). Le type est chiffré au repos (ARC-45) :
/// une grossesse ou un allaitement est une donnée sensible qui ne sort jamais en clair du service (ARC-06).
/// </summary>
public sealed class EtatParticulier : Entity
{
    private EtatParticulier()
    {
    }

    internal EtatParticulier(Guid id, TypeEtatParticulier type, DateOnly dateDebut, DateOnly? dateFin, Guid? affilieDeclarantId) : base(id)
    {
        if (dateFin is { } fin && fin < dateDebut)
        {
            throw new DomainException("La fin de l'état particulier précède son début.");
        }

        Type = type;
        DateDebut = dateDebut;
        DateFin = dateFin;
        AffilieDeclarantId = affilieDeclarantId;
    }

    public TypeEtatParticulier Type { get; private set; }

    /// <summary>Affilié dont l'employeur a fait la déclaration ; <c>null</c> si déclarée par le SEPP. Limite ce que voit l'employeur.</summary>
    public Guid? AffilieDeclarantId { get; private set; }

    public DateOnly DateDebut { get; private set; }

    /// <summary>Dernier jour inclus ; <c>null</c> si la fin n'est pas encore connue.</summary>
    public DateOnly? DateFin { get; private set; }

    public bool EstActifAu(DateOnly date) => date >= DateDebut && (DateFin is null || date <= DateFin);

    internal bool Chevauche(EtatParticulier autre) =>
        (autre.DateFin is null || DateDebut <= autre.DateFin) && (DateFin is null || autre.DateDebut <= DateFin);

    internal void Terminer(DateOnly dateFin)
    {
        if (dateFin < DateDebut)
        {
            throw new DomainException($"La fin de l'état particulier précède son début ({DateDebut:yyyy-MM-dd}).");
        }

        if (DateFin is { } actuelle && dateFin > actuelle)
        {
            throw new DomainException("Un état particulier terminé ne peut pas être prolongé : déclarer une nouvelle période.");
        }

        DateFin = dateFin;
    }
}
