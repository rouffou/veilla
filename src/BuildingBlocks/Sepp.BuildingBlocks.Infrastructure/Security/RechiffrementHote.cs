using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Mode d'hôte « rechiffrer puis quitter » (#396, ADR 0012), sur le modèle de « migrer puis quitter » (ADR 0010) :
/// <c>Rechiffrement:ExecuterPuisQuitter=true</c> rechiffre toutes les colonnes chiffrées du service avec sa DEK courante,
/// journalise le rapport, puis arrête le processus (code 0 si plus aucune valeur n'utilise une autre clé, 1 sinon).
/// Contrairement au job de migration, ce mode a besoin des clés du service : il s'exécute avec l'identité et la
/// configuration d'exécution du service (droits DML et déballage des DEK).
/// </summary>
public static partial class RechiffrementHote
{
    /// <summary>Active le mode « rechiffrer puis quitter ».</summary>
    public const string CleExecuterPuisQuitter = "Rechiffrement:ExecuterPuisQuitter";

    /// <summary>Section des paramètres (<see cref="OptionsRechiffrement"/>).</summary>
    public const string Section = "Rechiffrement";

    /// <summary>Toutes les valeurs utilisent la DEK courante.</summary>
    public const int CodeComplet = 0;

    /// <summary>Échec, ou valeurs restantes (erreurs, clé non configurée, écriture concurrente avec une autre DEK).</summary>
    public const int CodeIncomplet = 1;

    private const string CategorieJournal = "Sepp.Rechiffrement";

    /// <summary>
    /// Exécute le rechiffrement si le mode est demandé.
    /// </summary>
    /// <param name="host">Hôte construit du service (après le mode de migration et le contrôle des clés).</param>
    /// <param name="chiffreur">Chiffreur des champs du service (DEK courante et anciennes clés configurées).</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns><c>null</c> si le service doit servir les requêtes, sinon le code de sortie du processus.</returns>
    public static async Task<int?> RechiffrerChampsAsync<TContext>(
        this IHost host,
        Func<IServiceProvider, FieldEncryptor> chiffreur,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chiffreur);
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue(CleExecuterPuisQuitter, false))
        {
            return null;
        }

        var journal = host.Services.GetService<ILoggerFactory>()?.CreateLogger(CategorieJournal) ?? NullLogger.Instance;
        try
        {
            var rapport = await ExecuterAsync<TContext>(host.Services, chiffreur, Options(configuration), journal, cancellationToken);
            return rapport.Complet ? CodeComplet : CodeIncomplet;
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
