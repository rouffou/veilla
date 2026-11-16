using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Adapters.Rattrapage;
using Sepp.Personnes.Adapters.Security;
using Sepp.Personnes.Application;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Personnes";

    /// <summary>
    /// Clé de l'index aveugle du NISS (DAT-06), distincte des clés de chiffrement, enveloppée par la clé racine du service
    /// (<c>&lt;kid&gt;|&lt;base64&gt;</c>, ADR 0012).
    /// </summary>
    public const string BlindIndexWrappedKeySetting = "BlindIndex:CleEnveloppee";

    /// <summary>
    /// Clé précédente de l'index aveugle, enveloppée comme la clé courante : facultative, configurée seulement pendant une
    /// rotation de la clé (#473). La recherche et le contrôle de doublon acceptent alors les deux empreintes.
    /// </summary>
    public const string BlindIndexPreviousWrappedKeySetting = "BlindIndex:CleEnveloppeePrecedente";

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
        services.AddSingleton(sp => CreerIndexAveugle(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ChiffrementChamps>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<INissIndex, NissIndex>();
        services.AddSingleton<AlerteDoublonsNiss>();

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
    /// Index aveugle du NISS (<c>personne.niss_hash</c> sur <c>personne.niss_chiffre</c>) d'après le modèle EF Core : recalculé
    /// et vérifié par les modes « rechiffrer puis quitter » et « vérifier seulement » (#473).
    /// </summary>
    public static IndexAveugleChiffre IndexAveugleNiss(IModel modele, BlindIndex index) =>
        new(ColonneIndexAveugle.Depuis(modele, typeof(Personne), nameof(Personne.Niss), nameof(Personne.NissHash)), index);

    /// <summary>
    /// Clés de l'index aveugle, déballées au démarrage ; leur KCV est journalisée (#514) : la procédure d'incident vérifie
    /// ainsi que le service, le recalcul et la vérification utilisent la nouvelle clé.
    /// </summary>
    private static BlindIndex CreerIndexAveugle(IConfiguration configuration, ChiffrementChamps chiffrement, ILoggerFactory journaux)
    {
        var precedente = configuration[BlindIndexPreviousWrappedKeySetting];
        var index = new BlindIndex(
            LireCleIndexAveugle(configuration, chiffrement),
            string.IsNullOrWhiteSpace(precedente) ? null : chiffrement.Desenvelopper(precedente, BlindIndexPreviousWrappedKeySetting));
        JournalIndexAveugle.JournaliserCles(journaux.CreateLogger(JournalIndexAveugle.Categorie), "personne.niss_hash", index);
        return index;
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
