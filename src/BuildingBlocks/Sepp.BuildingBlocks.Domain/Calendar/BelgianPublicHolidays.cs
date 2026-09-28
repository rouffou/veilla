namespace Sepp.BuildingBlocks.Domain.Calendar;

/// <summary>Jour férié légal.</summary>
public sealed record PublicHoliday(DateOnly Date, string Code, LocalizedLabel Label);

/// <summary>
/// Les dix jours fériés légaux belges (loi du 4 janvier 1974). Les jours supplémentaires
/// (fêtes des Communautés, jours de remplacement) sont paramétrés dans le service Référentiels.
/// </summary>
public static class BelgianPublicHolidays
{
    public static IReadOnlyList<PublicHoliday> For(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1900);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 2200);

        var easter = EasterSunday(year);
        return
        [
            new(new DateOnly(year, 1, 1), "NOUVEL_AN", new("Jour de l'an", "Nieuwjaar", "Neujahr", "New Year's Day")),
            new(easter.AddDays(1), "LUNDI_PAQUES", new("Lundi de Pâques", "Paasmaandag", "Ostermontag", "Easter Monday")),
            new(new DateOnly(year, 5, 1), "FETE_TRAVAIL", new("Fête du travail", "Dag van de Arbeid", "Tag der Arbeit", "Labour Day")),
            new(easter.AddDays(39), "ASCENSION", new("Ascension", "Onze-Lieve-Heer-Hemelvaart", "Christi Himmelfahrt", "Ascension Day")),
            new(easter.AddDays(50), "LUNDI_PENTECOTE", new("Lundi de Pentecôte", "Pinkstermaandag", "Pfingstmontag", "Whit Monday")),
            new(new DateOnly(year, 7, 21), "FETE_NATIONALE", new("Fête nationale", "Nationale feestdag", "Nationalfeiertag", "National Day")),
            new(new DateOnly(year, 8, 15), "ASSOMPTION", new("Assomption", "Onze-Lieve-Vrouw-Hemelvaart", "Mariä Himmelfahrt", "Assumption Day")),
            new(new DateOnly(year, 11, 1), "TOUSSAINT", new("Toussaint", "Allerheiligen", "Allerheiligen", "All Saints' Day")),
            new(new DateOnly(year, 11, 11), "ARMISTICE", new("Armistice", "Wapenstilstand", "Waffenstillstand", "Armistice Day")),
            new(new DateOnly(year, 12, 25), "NOEL", new("Noël", "Kerstmis", "Weihnachten", "Christmas Day")),
        ];
    }

    /// <summary>Dimanche de Pâques (calendrier grégorien, algorithme de Meeus/Jones/Butcher).</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
