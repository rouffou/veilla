using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Adapters.Security;
using Sepp.Personnes.Application;

namespace Sepp.Personnes.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Personnes";

    /// <summary>Clé de l'index aveugle du NISS (DAT-06), distincte des clés de chiffrement <c>Encryption:*</c>.</summary>
    public const string BlindIndexKeySetting = "BlindIndex:Key";

    public static IServiceCollection AddPersonnesAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<PersonnesDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // ARC-45 : chiffrement des champs sensibles ; clés lues dans Encryption:CurrentKeyId et Encryption:Keys:<id>.
        services.AddSingleton<IFieldKeyProvider>(sp => new ConfigurationFieldKeyProvider(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<FieldEncryptor>();
        services.AddSingleton(sp => new BlindIndex(LireCleIndexAveugle(sp.GetRequiredService<IConfiguration>())));
        services.AddSingleton<INissIndex, NissIndex>();

        services.AddScoped<IPersonneRepository, PersonneRepository>();
        services.AddScoped<IContexteAffilie, ContexteAffilieJeton>();

        // Énumérations lisibles dans l'API (« Interimaire », « Grossesse »…).
        services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    private static byte[] LireCleIndexAveugle(IConfiguration configuration)
    {
        var valeur = configuration[BlindIndexKeySetting];
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new InvalidOperationException($"{BlindIndexKeySetting} manquant : clé de l'index aveugle du NISS (256 bits en base64).");
        }

        var cle = Convert.FromBase64String(valeur);
        return cle.Length == 32
            ? cle
            : throw new InvalidOperationException($"{BlindIndexKeySetting} doit faire 256 bits.");
    }
}
