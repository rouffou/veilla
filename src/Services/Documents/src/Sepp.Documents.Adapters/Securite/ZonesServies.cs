using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

using ZonesDomaine = Sepp.Documents.Domain.Commun.Zones;

namespace Sepp.Documents.Adapters.Securite;

/// <summary>
/// Zones servies par l'instance, lues dans <c>Documents:Zones</c> (liste obligatoire, sans valeur par défaut ; en variable
/// d'environnement, <c>DOCUMENTS__ZONES=medicale</c> ou une liste séparée par des virgules). Décision #356 : hors
/// développement et test, l'instance sert exactement une zone, égale à celle de son environnement (<c>SEPP__ZONE</c>,
/// injectée par Terraform, clé <c>Sepp:Zone</c>) ; en local, elle peut servir les trois zones.
/// </summary>
public sealed class ZonesServies : IZonesServies
{
    /// <summary>Clé de la configuration listant les zones servies.</summary>
    public const string Cle = "Documents:Zones";

    /// <summary>Clé de la zone de l'environnement d'exécution (variable <c>SEPP__ZONE</c>).</summary>
    public const string CleZoneEnvironnement = "Sepp:Zone";

    private readonly HashSet<ZoneDocument> _zones;

    public ZonesServies(IEnumerable<ZoneDocument> zones)
    {
        _zones = [.. zones];
        if (_zones.Count == 0)
        {
            throw new InvalidOperationException($"{Cle} doit lister au moins une zone.");
        }

        Zones = ZonesDomaine.Toutes.Where(_zones.Contains).ToList();
    }

    public IReadOnlyCollection<ZoneDocument> Zones { get; }

    public bool Sert(ZoneDocument zone) => _zones.Contains(zone);

    /// <summary>Lit et contrôle les zones servies ; échoue au démarrage si la configuration est absente ou incohérente.</summary>
    /// <exception cref="InvalidOperationException">Liste absente, zone inconnue ou garde de zone violée.</exception>
    public static ZonesServies Depuis(IConfiguration configuration, IHostEnvironment environnement)
    {
        var zones = Lire(configuration);
        if (!environnement.IsDevelopment() && !environnement.IsEnvironment("Test") && !environnement.IsEnvironment("Testing"))
        {
            VerifierGarde(zones, configuration[CleZoneEnvironnement]);
        }

        return new ZonesServies(zones);
    }

    /// <summary>Hors développement et test : exactement une zone, égale à la zone de l'environnement.</summary>
    public static void VerifierGarde(IReadOnlyCollection<ZoneDocument> zones, string? zoneEnvironnement)
    {
        if (zones.Count != 1)
        {
            throw new InvalidOperationException(
                $"{Cle} doit lister exactement une zone hors développement et test (une instance du service Documents par zone) ; {zones.Count} zone(s) configurée(s).");
        }

        if (string.IsNullOrWhiteSpace(zoneEnvironnement))
        {
            throw new InvalidOperationException($"La zone de l'environnement ({CleZoneEnvironnement}, variable SEPP__ZONE) est absente : impossible de contrôler {Cle}.");
        }

        if (!string.Equals(zones.Single().Code(), zoneEnvironnement.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Cle} ({zones.Single().Code()}) doit être égale à la zone de l'environnement ({CleZoneEnvironnement} = {zoneEnvironnement.Trim()}).");
        }
    }

    private static List<ZoneDocument> Lire(IConfiguration configuration)
    {
        var section = configuration.GetSection(Cle);
        var codes = section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (codes.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
        {
            codes = [.. section.Value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }

        if (codes.Count == 0)
        {
            throw new InvalidOperationException($"{Cle} est obligatoire (liste de zones : standard, medicale, psychosociale) et n'a pas de valeur par défaut.");
        }

        try
        {
            return [.. codes.Select(c => ZonesDomaine.Depuis(c!)).Distinct()];
        }
        catch (DomainException ex)
        {
            throw new InvalidOperationException($"{Cle} : {ex.Message}", ex);
        }
    }
}
