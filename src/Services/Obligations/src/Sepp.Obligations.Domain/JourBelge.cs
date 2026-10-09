namespace Sepp.Obligations.Domain;

/// <summary>Jour civil belge d'un instant : les échéances sont des dates civiles belges (DAT-08).</summary>
public static class JourBelge
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static DateOnly De(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Bruxelles).DateTime);
}
