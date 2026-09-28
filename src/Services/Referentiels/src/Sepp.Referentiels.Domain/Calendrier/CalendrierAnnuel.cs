using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;

namespace Sepp.Referentiels.Domain.Calendrier;

/// <summary>
/// Jours fériés d'une année : les dix jours fériés légaux, calculés, plus les jours supplémentaires
/// paramétrés (jours de remplacement, fêtes des Communautés…). Sert au calcul des délais légaux (DAT-08).
/// </summary>
public sealed class CalendrierAnnuel : AggregateRoot
{
    private readonly List<JourFerieSupplementaire> _joursSupplementaires = [];

    private CalendrierAnnuel()
    {
    }

    private CalendrierAnnuel(Guid id, int annee) : base(id)
    {
        Annee = annee;
    }

    public int Annee { get; private set; }

    public IReadOnlyList<JourFerieSupplementaire> JoursSupplementaires => _joursSupplementaires.AsReadOnly();

    public static CalendrierAnnuel Creer(int annee)
    {
        _ = BelgianPublicHolidays.For(annee); // valide l'année
        return new CalendrierAnnuel(NewId(), annee);
    }

    /// <summary>Tous les jours fériés de l'année, légaux et supplémentaires.</summary>
    public IReadOnlyList<PublicHoliday> JoursFeries() =>
        BelgianPublicHolidays.For(Annee)
            .Concat(_joursSupplementaires.Select(j => new PublicHoliday(j.Date, j.Code, j.Libelle)))
            .OrderBy(j => j.Date)
            .ToList();

    public void AjouterJour(DateOnly date, string code, LocalizedLabel libelle)
    {
        if (date.Year != Annee)
        {
            throw new DomainException($"Le jour {date:yyyy-MM-dd} n'appartient pas à l'année {Annee}.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (JoursFeries().Any(j => j.Date == date || j.Code == normalized))
        {
            throw new DomainException($"Un jour férié existe déjà à la date {date:yyyy-MM-dd} ou avec le code {normalized}.");
        }

        _joursSupplementaires.Add(new JourFerieSupplementaire(NewId(), date, normalized, libelle));
        Raise(new CalendrierModifie(Annee, DateTimeOffset.UtcNow));
    }

    public void RetirerJour(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var jour = _joursSupplementaires.SingleOrDefault(j => j.Code == normalized)
                   ?? throw new DomainException($"Aucun jour supplémentaire '{normalized}' en {Annee} (les jours fériés légaux ne peuvent pas être retirés).");
        _joursSupplementaires.Remove(jour);
        Raise(new CalendrierModifie(Annee, DateTimeOffset.UtcNow));
    }
}

public sealed class JourFerieSupplementaire : Entity
{
    private JourFerieSupplementaire()
    {
    }

    internal JourFerieSupplementaire(Guid id, DateOnly date, string code, LocalizedLabel libelle) : base(id)
    {
        Date = date;
        Code = code;
        Libelle = libelle;
    }

    public DateOnly Date { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public LocalizedLabel Libelle { get; private set; } = null!;
}

public sealed record CalendrierModifie(int Annee, DateTimeOffset OccurredAt) : IDomainEvent;
