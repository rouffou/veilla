using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Chiffrement;

/// <summary>
/// Configuration <c>Documents:Enveloppe</c> (ADR 0009) :
/// <list type="bullet">
/// <item><c>Type</c> : <c>KeyVault</c> (Key Vault ou Managed HSM, obligatoire hors développement et test) ou <c>Local</c>
/// (clé racine de configuration, développement et tests uniquement) ; aucun défaut implicite.</item>
/// <item><c>CleKek</c> : pour <c>KeyVault</c>, URI de la clé racine <b>sans version</b>
/// (<c>https://&lt;coffre&gt;.vault.azure.net/keys/documents-kek</c> ou <c>https://&lt;hsm&gt;.managedhsm.azure.net/keys/documents-kek</c>),
/// injecté par Terraform ; pour <c>Local</c>, une section <c>Courante</c> et <c>Versions:&lt;version&gt;</c> (256 bits en base64).</item>
/// </list>
/// Une instance Azure sert une seule zone et lit <c>CleKek</c> ; une instance locale qui sert plusieurs zones lit
/// <c>CleKek:&lt;zone&gt;</c> pour chacune.
/// </summary>
public static class ConfigurationEnveloppe
{
    public const string Section = "Documents:Enveloppe";
    public const string KeyVault = "KeyVault";
    public const string Local = "Local";

    public static void AjouterEnveloppe(IServiceCollection services, IConfiguration configuration, IHostEnvironment environnement, IZonesServies zones)
    {
        var type = configuration[$"{Section}:Type"];
        switch (type)
        {
            case KeyVault:
                var cles = zones.Zones.ToDictionary(z => z, z => EnveloppeKeyVault.LireUriCle(SectionCle(configuration, zones, z).Value, z));
                services.AddSingleton<IOperationsCleRacine, OperationsCleRacineAzure>();
                services.AddSingleton<IEnveloppeCles>(sp => new EnveloppeKeyVault(cles, sp.GetRequiredService<IOperationsCleRacine>()));
                break;
            case Local:
                if (!environnement.IsDevelopment() && !environnement.IsEnvironment("Test") && !environnement.IsEnvironment("Testing"))
                {
                    throw new InvalidOperationException(
                        $"{Section}:Type « {Local} » (clé racine en configuration) est réservé au développement et aux tests : utiliser « {KeyVault} » (ARC-44).");
                }

                var locale = EnveloppeLocale.Depuis(zones.Zones.ToDictionary(z => z, z => SectionCle(configuration, zones, z)));
                services.AddSingleton<IEnveloppeCles>(locale);
                break;
            default:
                throw new InvalidOperationException(
                    $"{Section}:Type doit valoir « {KeyVault} » (clé racine HSM) ou « {Local} » (développement) ; valeur actuelle : « {type} ».");
        }
    }

    /// <summary>Section de la clé racine d'une zone : <c>CleKek:&lt;zone&gt;</c>, ou <c>CleKek</c> si l'instance sert une seule zone.</summary>
    public static IConfigurationSection SectionCle(IConfiguration configuration, IZonesServies zones, ZoneDocument zone)
    {
        var parZone = configuration.GetSection($"{Section}:CleKek:{zone.Code()}");
        if (parZone.Exists())
        {
            return parZone;
        }

        if (zones.Zones.Count == 1)
        {
            var unique = configuration.GetSection($"{Section}:CleKek");
            if (unique.Exists())
            {
                return unique;
            }
        }

        throw new InvalidOperationException(zones.Zones.Count == 1
            ? $"{Section}:CleKek manquant : clé racine de la zone {zone.Code()} (ARC-44)."
            : $"{Section}:CleKek:{zone.Code()} manquant : une instance qui sert plusieurs zones configure une clé racine par zone.");
    }
}
