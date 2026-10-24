using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sepp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Ce que le service fait de son schéma au démarrage (ADR 0010).</summary>
public enum ModeMigration
{
    /// <summary>Aucune migration : le service sert les requêtes (Azure, révision d'exécution).</summary>
    Servir,

    /// <summary>Migration puis service des requêtes (développement local, tests : <c>Database:MigrateOnStartup</c>).</summary>
    MigrerPuisServir,

    /// <summary>Migration puis arrêt avec un code de sortie (job de migration Azure : <c>Database:MigrateAndExit</c>).</summary>
    MigrerPuisQuitter,
}

/// <summary>
/// Migrations de schéma EF Core communes à tous les services (ARC-50, CTR-16, ADR 0010).
/// <list type="bullet">
/// <item>En Azure, le job Container Apps de migration du service exécute la même image avec
/// <c>Database__MigrateAndExit=true</c> et l'identité managée de migration (droits DDL) : migrations, données de départ,
/// droits DML de l'identité d'exécution (<c>Database:RuntimeRole</c>), puis arrêt (code 0, ou 1 en cas d'échec).</item>
/// <item>En local et dans les tests, <c>Database:MigrateOnStartup=true</c> migre puis sert les requêtes.</item>
/// <item>Sinon (révision d'exécution en Azure), rien n'est migré : l'identité d'exécution n'a que les droits DML.</item>
/// </list>
/// À appeler juste après <c>builder.Build()</c>, avant toute vérification de clé de chiffrement : le job de migration ne
/// reçoit aucun secret.
/// </summary>
public static partial class MigrationSchema
{
    /// <summary>Migration au démarrage puis service des requêtes (local, tests).</summary>
    public const string CleMigrerAuDemarrage = "Database:MigrateOnStartup";

    /// <summary>Migration puis arrêt du processus (job de migration).</summary>
    public const string CleMigrerPuisQuitter = "Database:MigrateAndExit";

    /// <summary>Rôle PostgreSQL de l'identité d'exécution, qui reçoit les droits DML sur les objets migrés.</summary>
    public const string CleRoleExecution = "Database:RuntimeRole";

    /// <summary>Code de sortie d'une migration réussie.</summary>
    public const int CodeSucces = 0;

    /// <summary>Code de sortie d'une migration en échec : le job échoue et la révision n'est pas déployée.</summary>
    public const int CodeEchec = 1;

    private const string CategorieJournal = "Sepp.Migrations";

    /// <summary>Mode demandé par la configuration ; « migrer puis quitter » l'emporte.</summary>
    public static ModeMigration Mode(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.GetValue(CleMigrerPuisQuitter, false))
        {
            return ModeMigration.MigrerPuisQuitter;
        }

        return configuration.GetValue(CleMigrerAuDemarrage, false) ? ModeMigration.MigrerPuisServir : ModeMigration.Servir;
    }

    /// <summary>
    /// Applique le mode de migration demandé.
    /// </summary>
    /// <param name="host">Hôte construit du service.</param>
    /// <param name="initialiser">Données de départ idempotentes, exécutées après les migrations (facultatif).</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>
    /// <c>null</c> si le service doit servir les requêtes ; sinon le code de sortie du processus (mode « migrer puis
    /// quitter »), <see cref="CodeSucces"/> ou <see cref="CodeEchec"/>.
    /// </returns>
    public static async Task<int?> MigrerSchemaAsync<TContext>(
        this IHost host,
        Func<IServiceProvider, CancellationToken, Task>? initialiser = null,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(host);
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        switch (Mode(configuration))
        {
            case ModeMigration.MigrerPuisServir:
                // Local et tests : une erreur interrompt le démarrage, comme auparavant.
                await MigrerAsync<TContext>(host.Services, initialiser, roleExecution: null, Journal(host), cancellationToken);
                return null;

            case ModeMigration.MigrerPuisQuitter:
                return await MigrerPuisQuitterAsync<TContext>(host.Services, configuration, initialiser, Journal(host), cancellationToken);

            default:
                return null;
        }
    }

    /// <summary>
    /// Instruction SQL accordant au rôle d'exécution les seuls droits DML (lecture, ajout, modification, suppression ;
    /// séquences) sur les schémas du service : <c>public</c> et ceux que possède l'identité de migration. Les droits par
    /// défaut couvrent les objets créés par les migrations suivantes. Aucun droit DDL, ni <c>TRUNCATE</c>, ni propriété.
    /// </summary>
    /// <exception cref="ArgumentException">Nom de rôle invalide (seuls lettres, chiffres, « _ », « - » et « . »).</exception>
    public static string InstructionDroitsExecution(string roleExecution)
    {
        if (!EstRoleValide(roleExecution))
        {
            throw new ArgumentException(
                $"{CleRoleExecution} invalide : 1 à 63 caractères, lettres, chiffres, « _ », « - » ou « . » (nom de l'identité managée d'exécution).",
                nameof(roleExecution));
        }

        // Le nom validé ne contient ni guillemet ni apostrophe : il peut figurer dans un littéral, que format(%I) cite.
        return $"""
            DO $droits$
            DECLARE
                schema_service text;
                role_execution constant text := '{roleExecution}';
            BEGIN
                FOR schema_service IN
                    SELECT n.nspname FROM pg_namespace n
                    WHERE n.nspname = 'public'
                       OR (pg_get_userbyid(n.nspowner) = current_user AND n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema')
                LOOP
                    EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', schema_service, role_execution);
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', schema_service, role_execution);
                    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', schema_service, role_execution);
                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', schema_service, role_execution);
                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I', schema_service, role_execution);
                END LOOP;
            END
            $droits$;
            """;
    }

    /// <summary>Nom de rôle PostgreSQL acceptable pour l'identité d'exécution (nom d'identité managée).</summary>
    public static bool EstRoleValide(string? role) => role is not null && RoleValide().IsMatch(role);

    private static async Task<int> MigrerPuisQuitterAsync<TContext>(
        IServiceProvider services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task>? initialiser,
        ILogger journal,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        try
        {
            var roleExecution = configuration[CleRoleExecution];
            if (string.IsNullOrWhiteSpace(roleExecution))
            {
                // Local ou base dont l'identité de migration sert aussi l'exécution : aucun droit à accorder.
                roleExecution = null;
                JournalMigration.RoleExecutionAbsent(journal, CleRoleExecution);
            }
            else if (!EstRoleValide(roleExecution))
            {
                JournalMigration.RoleExecutionInvalide(journal, CleRoleExecution);
                return CodeEchec;
            }

            await MigrerAsync<TContext>(services, initialiser, roleExecution, journal, cancellationToken);
            JournalMigration.Terminee(journal, typeof(TContext).Name);
            return CodeSucces;
        }
        catch (Exception erreur) when (erreur is not OutOfMemoryException)
        {
            // Type et message seulement : la connexion se fait par jeton Entra (aucun mot de passe dans la chaîne), et les
            // messages Npgsql et EF Core ne contiennent ni jeton ni secret applicatif.
            JournalMigration.Echec(journal, typeof(TContext).Name, erreur.GetType().FullName ?? erreur.GetType().Name, MessageSansDonnees(erreur));
            return CodeEchec;
        }
    }

    private static async Task MigrerAsync<TContext>(
        IServiceProvider services,
        Func<IServiceProvider, CancellationToken, Task>? initialiser,
        string? roleExecution,
        ILogger journal,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var contexte = scope.ServiceProvider.GetRequiredService<TContext>();
        var enAttente = (await contexte.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        JournalMigration.Debut(journal, typeof(TContext).Name, enAttente.Count, enAttente);

        await contexte.Database.MigrateAsync(cancellationToken);

        if (initialiser is not null)
        {
            await initialiser(scope.ServiceProvider, cancellationToken);
        }

        if (roleExecution is not null)
        {
            await contexte.Database.ExecuteSqlRawAsync(InstructionDroitsExecution(roleExecution), cancellationToken);
            JournalMigration.DroitsAccordes(journal, roleExecution);
        }
    }

    /// <summary>Message de l'exception la plus interne, borné (pas de pile, aucune valeur de paramètre SQL).</summary>
    private static string MessageSansDonnees(Exception erreur)
    {
        var racine = erreur;
        while (racine.InnerException is not null)
        {
            racine = racine.InnerException;
        }

        var message = racine == erreur ? erreur.Message : $"{erreur.Message} -> {racine.GetType().Name}: {racine.Message}";
        return message.Length <= 2000 ? message : message[..2000];
    }

    private static ILogger Journal(IHost host) =>
        host.Services.GetService<ILoggerFactory>()?.CreateLogger(CategorieJournal) ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleValide();
}

/// <summary>Journal des migrations (aucune donnée personnelle ni secret, ARC-06).</summary>
internal static partial class JournalMigration
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Migration du schéma {Contexte} : {Nombre} migration(s) en attente ({Migrations}).")]
    public static partial void Debut(ILogger journal, string contexte, int nombre, IReadOnlyList<string> migrations);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Droits DML accordés au rôle d'exécution {Role}.")]
    public static partial void DroitsAccordes(ILogger journal, string role);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "Migration du schéma {Contexte} terminée.")]
    public static partial void Terminee(ILogger journal, string contexte);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Error, Message = "Échec de la migration du schéma {Contexte} : {TypeErreur} : {Message}")]
    public static partial void Echec(ILogger journal, string contexte, string typeErreur, string message);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning, Message = "{Cle} absent : aucun droit DML accordé à une identité d'exécution distincte.")]
    public static partial void RoleExecutionAbsent(ILogger journal, string cle);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Error, Message = "{Cle} invalide : nom de rôle PostgreSQL refusé, migration non exécutée.")]
    public static partial void RoleExecutionInvalide(ILogger journal, string cle);
}
