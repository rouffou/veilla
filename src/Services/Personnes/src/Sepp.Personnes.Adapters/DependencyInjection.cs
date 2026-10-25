using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Adapters.Rattrapage;
using Sepp.Personnes.Adapters.Security;
using Sepp.Personnes.Application;

namespace Sepp.Personnes.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Personnes";

    /// <summary>
    /// Clé de l'index aveugle du NISS (DAT-06), distincte des clés de chiffrement, enveloppée par la clé racine du service
    /// (<c>&lt;kid&gt;|&lt;base64&gt;</c>, ADR 0012).
    /// </summary>
    public const string BlindIndexWrappedKeySetting = "BlindIndex:CleEnveloppee";

    /// <summary>Clé de l'index aveugle en clair : développement et tests seulement (clé racine locale).</summary>
    public const string BlindIndexKeySetting = "BlindIndex:Key";

    public static IServiceCollection AddPersonnesAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<PersonnesDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // ARC-44, ARC-45, ADR 0012 : chiffrement des champs sensibles par enveloppe (section Encryption) ; DEK et clé de
        // l'index aveugle enveloppées par la clé racine du service dans le coffre de sa zone, déballées au démarrage.
        services.AddSingleton(sp => ChiffrementChamps.Charger(
            sp.GetRequiredService<IConfiguration>().GetSection("Encryption"), sp.GetRequiredService<IHostEnvironment>(), sp.GetService<IOperationsCleRacine>()));
        services.AddSingleton(sp => sp.GetRequiredService<ChiffrementChamps>().Cles);
        services.AddSingleton<FieldEncryptor>();
        services.AddSingleton(sp => new BlindIndex(LireCleIndexAveugle(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ChiffrementChamps>())));
        services.AddSingleton<INissIndex, NissIndex>();

        services.AddScoped<IPersonneRepository, PersonneRepository>();
        services.AddScoped<IContexteAffilie, ContexteAffilieJeton>();
        services.AddScoped<IContextePersonne, ContextePersonneJeton>();

        // ADR 0011 : rattrapage des projections (langue des personnes, occupations), déclenché par l'exploitation.
        services.AddSeppRepublication<PersonnesDbContext>(configuration);
        services.AddRepublicationSource<RepublicationPersonnes>();
        services.AddRepublicationSource<RepublicationOccupations>();

        // Énumérations lisibles dans l'API (« Interimaire », « Grossesse »…).
        services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    /// <summary>
    /// Clé de l'index aveugle : déballée par la clé racine du service. La même clé, enveloppée, donne le même index : la
    /// recherche par NISS n'est pas affectée par le passage à l'enveloppe. La clé en clair n'est admise qu'avec la clé
    /// racine locale (développement, tests).
    /// </summary>
    private static byte[] LireCleIndexAveugle(IConfiguration configuration, ChiffrementChamps chiffrement)
    {
        var enveloppee = configuration[BlindIndexWrappedKeySetting];
        if (!string.IsNullOrWhiteSpace(enveloppee))
        {
            return chiffrement.Desenvelopper(enveloppee, BlindIndexWrappedKeySetting);
        }

        var valeur = configuration[BlindIndexKeySetting];
        if (string.IsNullOrWhiteSpace(valeur) || !chiffrement.EstLocal)
        {
            throw new InvalidOperationException(
                $"{BlindIndexWrappedKeySetting} manquant : clé de l'index aveugle du NISS enveloppée par la clé racine du service (ADR 0012).");
        }

        var cle = Convert.FromBase64String(valeur);
        return cle.Length == 32
            ? cle
            : throw new InvalidOperationException($"{BlindIndexKeySetting} doit faire 256 bits.");
    }
}
