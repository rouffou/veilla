using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain;

/// <summary>Canal de la convocation (SAN-10), aligné sur le canal préféré du service Personnes.</summary>
public enum CanalConvocation
{
    Courrier,
    Email,
    Sms,
    Portail,
}

/// <summary>Codes métier stables (type d'acte, compétence) : majuscules, chiffres et soulignés, par ex. <c>EXAMEN_REPRISE</c>.</summary>
public static class CodeMetier
{
    public const int LongueurMaximale = 50;

    public static string Normaliser(string? valeur, string nature)
    {
        var code = (valeur ?? string.Empty).Trim().ToUpperInvariant().Replace('-', '_');
        if (code.Length == 0 || code.Length > LongueurMaximale ||
            !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new DomainException($"{nature} invalide : '{valeur}'. Attendu : lettres, chiffres et '_' ({LongueurMaximale} caractères au plus).");
        }

        return code;
    }

    public static IReadOnlyList<string> NormaliserTous(IEnumerable<string>? valeurs, string nature) =>
        (valeurs ?? []).Select(v => Normaliser(v, nature)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}

/// <summary>
/// Heure légale belge : les agendas sont saisis en heure locale (Europe/Brussels) et stockés en UTC.
/// </summary>
public static class HeureBelge
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static DateTimeOffset VersUtc(DateOnly jour, TimeOnly heure)
    {
        var local = jour.ToDateTime(heure, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            // Passage à l'heure d'été : l'heure n'existe pas, on retient l'heure suivante.
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateOnly Jour(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    public static TimeOnly Heure(DateTimeOffset instant) => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}

/// <summary>Position géographique d'un lieu (WGS 84), utilisée par l'optimisation des déplacements (PLA-04).</summary>
public readonly record struct Coordonnees
{
    private const double RayonTerreKm = 6371.0;

    public Coordonnees(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180 || double.IsNaN(latitude) || double.IsNaN(longitude))
        {
            throw new DomainException("Coordonnées invalides : latitude entre -90 et 90, longitude entre -180 et 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>Distance orthodromique (formule de haversine), en kilomètres : approximation explicable du trajet routier.</summary>
    public double DistanceKm(Coordonnees autre)
    {
        static double Rad(double degres) => degres * Math.PI / 180;
        var dLat = Rad(autre.Latitude - Latitude);
        var dLon = Rad(autre.Longitude - Longitude);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(Rad(Latitude)) * Math.Cos(Rad(autre.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * RayonTerreKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
