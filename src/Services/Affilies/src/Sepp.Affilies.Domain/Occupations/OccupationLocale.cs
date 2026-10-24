namespace Sepp.Affilies.Domain.Occupations;

/// <summary>
/// Occupation d'un travailleur chez un affilié, projetée depuis <c>personnes.occupation-debutee</c> et
/// <c>personnes.occupation-terminee</c> (POR-11, #340). Les deux événements complètent la même ligne, quel que soit leur
/// ordre d'arrivée. Seuls des identifiants sont conservés (ARC-06) : ni identité, ni NISS, ni type de contrat.
/// <see cref="DateFin"/> est le dernier jour d'occupation (inclus, comme DIMONA).
/// </summary>
public sealed class OccupationLocale
{
    private OccupationLocale()
    {
    }

    public OccupationLocale(Guid occupationId, Guid personneId, Guid affilieId)
    {
        OccupationId = occupationId;
        PersonneId = personneId;
        AffilieId = affilieId;
    }

    public Guid OccupationId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    /// <summary><c>null</c> tant que l'événement de début n'est pas reçu : l'occupation n'est alors pas considérée active.</summary>
    public DateOnly? DateDebut { get; private set; }

    /// <summary>Dernier jour d'occupation (inclus), <c>null</c> tant que la fin n'est pas connue.</summary>
    public DateOnly? DateFin { get; private set; }

    public DateTimeOffset? FinRecueDu { get; private set; }

    /// <summary>Occupation commencée et non terminée à la date.</summary>
    public bool EstActiveAu(DateOnly date) => DateDebut is { } debut && debut <= date && (DateFin is null || DateFin >= date);

    public void Debuter(DateOnly dateDebut) => DateDebut = dateDebut;

    /// <summary>Enregistre la fin ; ignore un événement plus ancien que celui déjà appliqué (désordre de livraison).</summary>
    public bool Terminer(DateOnly dateFin, DateTimeOffset evenementDu)
    {
        if (FinRecueDu is { } connue && evenementDu < connue)
        {
            return false;
        }

        DateFin = dateFin;
        FinRecueDu = evenementDu;
        return true;
    }
}
