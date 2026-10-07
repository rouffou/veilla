namespace Sepp.BuildingBlocks.Domain.Calendar;

/// <summary>
/// Calendrier des jours ouvrables belge utilisé pour les délais légaux (DAT-08).
/// Jours ouvrables : du lundi au vendredi, hors jours fériés.
/// </summary>
public sealed class BusinessCalendar
{
    private readonly HashSet<DateOnly> _holidays;

    public BusinessCalendar(IEnumerable<DateOnly> holidays)
    {
        _holidays = [.. holidays];
    }

    /// <summary>Calendrier limité aux dix jours fériés légaux belges des années données.</summary>
    public static BusinessCalendar Belgian(params int[] years) =>
        new(years.SelectMany(BelgianPublicHolidays.For).Select(h => h.Date));

    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    public bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    /// <summary>
    /// Date obtenue en ajoutant <paramref name="businessDays"/> jours ouvrables à <paramref name="start"/>
    /// (le jour de départ n'est pas compté). Exemple : examen de reprise au plus tard 10 jours ouvrables après la reprise.
    /// </summary>
    public DateOnly AddBusinessDays(DateOnly start, int businessDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(businessDays);

        var date = start;
        var remaining = businessDays;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (IsBusinessDay(date))
            {
                remaining--;
            }
        }

        return date;
    }

    /// <summary>
    /// Date obtenue en retranchant <paramref name="businessDays"/> jours ouvrables à <paramref name="start"/> (le jour de
    /// départ n'est pas compté) : symétrique de <see cref="AddBusinessDays"/>. Sert aux alertes « échéance menacée »
    /// (N jours ouvrables avant la date limite). Avec 0 jour, retourne <paramref name="start"/> même s'il n'est pas ouvrable.
    /// </summary>
    public DateOnly SubtractBusinessDays(DateOnly start, int businessDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(businessDays);

        var date = start;
        var remaining = businessDays;
        while (remaining > 0)
        {
            date = date.AddDays(-1);
            if (IsBusinessDay(date))
            {
                remaining--;
            }
        }

        return date;
    }

    /// <summary>Nombre de jours ouvrables dans l'intervalle ]from, to].</summary>
    public int CountBusinessDays(DateOnly from, DateOnly to)
    {
        if (to <= from)
        {
            return 0;
        }

        var count = 0;
        for (var date = from.AddDays(1); date <= to; date = date.AddDays(1))
        {
            if (IsBusinessDay(date))
            {
                count++;
            }
        }

        return count;
    }
}
