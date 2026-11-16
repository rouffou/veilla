using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Modes d'hôte « rechiffrer puis quitter » et « vérifier seulement » (#396, #482, ADR 0012), sur le modèle de « migrer
/// puis quitter » (ADR 0010) :
/// <list type="bullet">
/// <item><c>Rechiffrement:ExecuterPuisQuitter=true</c> rechiffre toutes les colonnes chiffrées du service avec sa DEK
/// courante, vérifie toute la base, journalise le rapport, puis arrête le processus ;</item>
/// <item><c>Rechiffrement:VerifierSeulement=true</c> vérifie seulement, sans rien écrire (garde-fou avant le retrait d'une
/// clé, étape bloquante du pipeline : #474). Il l'emporte sur le mode précédent si les deux sont demandés.</item>
/// </list>
/// Un service qui porte un index aveugle (Personnes, #473) le déclare : il est recalculé avec sa clé courante après le
/// rechiffrement, ou seulement vérifié par recalcul (<see cref="RecalculIndexAveugle"/>).
/// Code de sortie : 0 si plus aucune valeur n'utilise une autre clé que la DEK courante, qu'aucune valeur chiffrée n'est
/// hors du modèle et que chaque empreinte d'index aveugle est celle de la clé courante, 1 sinon (y compris en cas d'échec). Contrairement au job de migration, ces modes ont besoin de la
/// configuration des clés du service : ils s'exécutent avec l'identité et la configuration d'exécution du service.
/// </summary>
public static partial class RechiffrementHote
{
    /// <summary>Active le mode « rechiffrer puis quitter ».</summary>
    public const string CleExecuterPuisQuitter = "Rechiffrement:ExecuterPuisQuitter";

    /// <summary>Active le mode « vérifier seulement » (aucune écriture).</summary>
    public const string CleVerifierSeulement = "Rechiffrement:VerifierSeulement";

    /// <summary>Section des paramètres (<see cref="OptionsRechiffrement"/>).</summary>
    public const string Section = "Rechiffrement";

    /// <summary>Toutes les valeurs utilisent la DEK courante, aucune valeur chiffrée hors du modèle.</summary>
    public const int CodeComplet = 0;

    /// <summary>Échec, ou valeurs restantes (erreurs, clé non configurée, écriture concurrente avec une autre DEK, colonne hors modèle).</summary>
    public const int CodeIncomplet = 1;

    private const string CategorieJournal = "Sepp.Rechiffrement";

    /// <summary>
    /// Exécute le rechiffrement ou la vérification si l'un des modes est demandé.
    /// </summary>
    /// <param name="host">Hôte construit du service (après le mode de migration et le contrôle des clés).</param>
    /// <param name="chiffreur">Chiffreur des champs du service (DEK courante et anciennes clés configurées).</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns><c>null</c> si le service doit servir les requêtes, sinon le code de sortie du processus.</returns>
    public static Task<int?> RechiffrerChampsAsync<TContext>(
        this IHost host,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        CancellationToken cancellationToken = default)
        where TContext : DbContext =>
        host.RechiffrerChampsAsync<TContext>(chiffreur, static (_, _) => [], cancellationToken);

    /// <summary>
    /// Exécute le rechiffrement ou la vérification si l'un des modes est demandé, en traitant aussi les index aveugles
    /// du service (#473) : recalcul avec la clé courante puis vérification, ou vérification seule.
    /// </summary>
    /// <param name="host">Hôte construit du service (après le mode de migration et le contrôle des clés).</param>
    /// <param name="chiffreur">Chiffreur des champs du service (DEK courante et anciennes clés configurées).</param>
    /// <param name="indexAveugles">Index aveugles du service, d'après le modèle EF Core du contexte.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns><c>null</c> si le service doit servir les requêtes, sinon le code de sortie du processus.</returns>
    public static async Task<int?> RechiffrerChampsAsync<TContext>(
        this IHost host,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        Func<IServiceProvider, IModel, IReadOnlyList<IndexAveugleChiffre>> indexAveugles,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(indexAveugles);
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var verifier = configuration.GetValue(CleVerifierSeulement, false);
        if (!verifier && !configuration.GetValue(CleExecuterPuisQuitter, false))
        {
            return null;
        }

        var journal = host.Services.GetService<ILoggerFactory>()?.CreateLogger(CategorieJournal) ?? NullLogger.Instance;
        try
        {
            var options = Options(configuration);
            bool complet;
            if (verifier)
            {
                var chiffrement = await VerifierAsync<TContext>(host.Services, chiffreur, options, journal, cancellationToken);
                var index = await VerifierIndexAveuglesAsync<TContext>(host.Services, chiffreur, indexAveugles, options, journal, cancellationToken);
                complet = chiffrement.Complet && index.All(i => i.Complet);
            }
            else
            {
                var chiffrement = await ExecuterAsync<TContext>(host.Services, chiffreur, options, journal, cancellationToken);
                var index = await RecalculerIndexAveuglesAsync<TContext>(host.Services, chiffreur, indexAveugles, options, journal, cancellationToken);
                complet = chiffrement.Complet && index.All(i => i.Complet);
            }

            return complet ? CodeComplet : CodeIncomplet;
        }
        catch (Exception erreur) when (erreur is not OutOfMemoryException)
        {
            // Type et message de l'exception la plus interne seulement : ni valeur, ni clé, ni paramètre SQL.
            var racine = erreur.GetBaseException();
            LogEchec(journal, typeof(TContext).Name, racine.GetType().FullName ?? racine.GetType().Name, racine.Message.Length <= 2000 ? racine.Message : racine.Message[..2000]);
            return CodeIncomplet;
        }
    }

    /// <summary>Rechiffre les colonnes chiffrées du contexte (utilisable aussi par les tests).</summary>
    public static async Task<RapportRechiffrement> ExecuterAsync<TContext>(
        IServiceProvider services,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        OptionsRechiffrement options,
        ILogger journal,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var contexte = scope.ServiceProvider.GetRequiredService<TContext>();
        var colonnes = ColonnesChiffrees.Depuis(contexte.Model);
        await contexte.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await RechiffrementChamps.ExecuterAsync(
                contexte.Database.GetDbConnection(), chiffreur(scope.ServiceProvider), colonnes, options, journal,
                scope.ServiceProvider.GetService<TimeProvider>(), cancellationToken);
        }
        finally
        {
            await contexte.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Vérifie, sans rien écrire, que toutes les valeurs chiffrées de la base utilisent la DEK courante (utilisable aussi par les tests).</summary>
    public static async Task<RapportVerificationChiffrement> VerifierAsync<TContext>(
        IServiceProvider services,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        OptionsRechiffrement options,
        ILogger journal,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(options);
        await using var scope = services.CreateAsyncScope();
        var contexte = scope.ServiceProvider.GetRequiredService<TContext>();
        var colonnes = ColonnesChiffrees.Depuis(contexte.Model);
        await contexte.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await VerificationChiffrement.VerifierAsync(
                contexte.Database.GetDbConnection(), chiffreur(scope.ServiceProvider).PrefixeCourant, colonnes, options.DelaiVerrou, journal, cancellationToken);
        }
        finally
        {
            await contexte.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Recalcule les index aveugles du contexte avec leur clé courante, puis les vérifie (#473 ; utilisable aussi par les tests).
    /// </summary>
    public static async Task<IReadOnlyList<RapportRecalculIndexAveugle>> RecalculerIndexAveuglesAsync<TContext>(
        IServiceProvider services,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        Func<IServiceProvider, IModel, IReadOnlyList<IndexAveugleChiffre>> indexAveugles,
        OptionsRechiffrement options,
        ILogger journal,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(indexAveugles);
        await using var scope = services.CreateAsyncScope();
        var contexte = scope.ServiceProvider.GetRequiredService<TContext>();
        var index = indexAveugles(scope.ServiceProvider, contexte.Model);
        if (index.Count == 0)
        {
            return [];
        }

        var rapports = new List<RapportRecalculIndexAveugle>(index.Count);
        await contexte.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var indexAveugle in index)
            {
                rapports.Add(await RecalculIndexAveugle.ExecuterAsync(
                    contexte.Database.GetDbConnection(), chiffreur(scope.ServiceProvider), indexAveugle, options, journal,
                    scope.ServiceProvider.GetService<TimeProvider>(), cancellationToken));
            }
        }
        finally
        {
            await contexte.Database.CloseConnectionAsync();
        }

        return rapports;
    }

    /// <summary>
    /// Vérifie, sans rien écrire, que chaque empreinte des index aveugles du contexte est calculée avec la clé courante
    /// (#473 ; utilisable aussi par les tests).
    /// </summary>
    public static async Task<IReadOnlyList<RapportVerificationIndexAveugle>> VerifierIndexAveuglesAsync<TContext>(
        IServiceProvider services,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        Func<IServiceProvider, IModel, IReadOnlyList<IndexAveugleChiffre>> indexAveugles,
        OptionsRechiffrement options,
        ILogger journal,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(indexAveugles);
        ArgumentNullException.ThrowIfNull(options);
        await using var scope = services.CreateAsyncScope();
        var contexte = scope.ServiceProvider.GetRequiredService<TContext>();
        var index = indexAveugles(scope.ServiceProvider, contexte.Model);
        if (index.Count == 0)
        {
            return [];
        }

        var rapports = new List<RapportVerificationIndexAveugle>(index.Count);
        await contexte.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var indexAveugle in index)
            {
                rapports.Add(await RecalculIndexAveugle.VerifierAsync(
                    contexte.Database.GetDbConnection(), chiffreur(scope.ServiceProvider), indexAveugle, options.DelaiVerrou, journal, cancellationToken));
            }
        }
        finally
        {
            await contexte.Database.CloseConnectionAsync();
        }

        return rapports;
    }

    /// <summary>Paramètres facultatifs : <c>Rechiffrement:TailleLot</c> et <c>Rechiffrement:PauseEntreLots</c>.</summary>
    public static OptionsRechiffrement Options(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var defaut = new OptionsRechiffrement();
        var taille = configuration.GetValue($"{Section}:TailleLot", defaut.TailleLot);
        if (taille is < 1 or > 10_000)
        {
            throw new InvalidOperationException($"{Section}:TailleLot doit être compris entre 1 et 10000.");
        }

        return defaut with { TailleLot = taille, PauseEntreLots = configuration.GetValue($"{Section}:PauseEntreLots", defaut.PauseEntreLots) };
    }

    [LoggerMessage(EventId = 5105, Level = LogLevel.Error, Message = "Échec du rechiffrement des champs de {Contexte} : {TypeErreur} : {Message}. Relancer le traitement : il reprend les valeurs restantes.")]
    private static partial void LogEchec(ILogger journal, string contexte, string typeErreur, string message);
}
