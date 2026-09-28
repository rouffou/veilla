namespace Sepp.BuildingBlocks.Domain;

/// <summary>
/// Période de validité [ValidFrom, ValidTo[ (DAT-04). Une période ouverte n'a pas de fin.
/// On clôture une période au lieu de modifier la ligne.
/// </summary>
public readonly record struct Validity
{
    public Validity(DateOnly validFrom, DateOnly? validTo = null)
    {
        if (validTo is { } end && end <= validFrom)
        {
            throw new DomainException($"La fin de validité ({end:yyyy-MM-dd}) doit être postérieure au début ({validFrom:yyyy-MM-dd}).");
        }

        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public DateOnly ValidFrom { get; }

    /// <summary>Borne exclusive ; <c>null</c> pour une période ouverte.</summary>
    public DateOnly? ValidTo { get; }

    public bool IsOpen => ValidTo is null;

    public bool Contains(DateOnly date) => date >= ValidFrom && (ValidTo is null || date < ValidTo);

    public bool Overlaps(Validity other) =>
        (other.ValidTo is null || ValidFrom < other.ValidTo) && (ValidTo is null || other.ValidFrom < ValidTo);

    /// <summary>Clôture la période à la date donnée (exclusive).</summary>
    public Validity CloseAt(DateOnly end) => new(ValidFrom, end);
}
