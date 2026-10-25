using System.Text.RegularExpressions;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Réglages du bus de développement (section <c>BusDeveloppement</c>, développement et tests uniquement).</summary>
public sealed class BusDeveloppementOptions
{
    /// <summary>Intervalle d'interrogation des rubriques par le consommateur.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Nombre maximal de messages lus par rubrique et par passe.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Nombre de tentatives d'un message avant sa mise en lettre morte (<c>bus_lettre_morte</c>), comme le
    /// <c>max_delivery_count</c> d'un abonnement Service Bus. Une tentative a lieu à chaque passe.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>
/// Bus d'événements de développement en PostgreSQL (ADR 0004, amendement) : remplace Azure Service Bus sur le poste de
/// développement et dans les tests, activé par <c>ConnectionStrings:BusDeveloppement</c>. Une table <c>bus_message</c>
/// tient les messages de toutes les rubriques ; chaque abonnement lit sa rubrique à partir de son curseur
/// (<c>bus_curseur</c>). <b>Refusé hors Development, Test et Testing</b> : il n'offre ni chiffrement, ni contrôle d'accès
/// par rubrique, ni rétention.
/// </summary>
public sealed partial class BusDeveloppement : IAsyncDisposable
{
    public const string ConnectionStringName = "BusDeveloppement";

    /// <summary>Environnements où le bus de développement est admis.</summary>
    public static readonly IReadOnlyList<string> EnvironnementsAutorises = ["Development", "Test", "Testing"];

    private const string Schema = """
        SELECT pg_advisory_xact_lock(4203910, 0);
        CREATE TABLE IF NOT EXISTS bus_message (
            seq bigserial PRIMARY KEY,
            id uuid NOT NULL,
            topic text NOT NULL,
            subject text NOT NULL,
            payload text NOT NULL,
            correlation_id text NULL,
            occurred_at timestamptz NOT NULL,
            cree_le timestamptz NOT NULL DEFAULT now());
        CREATE INDEX IF NOT EXISTS ix_bus_message_topic_seq ON bus_message (topic, seq);
        CREATE TABLE IF NOT EXISTS bus_curseur (
            subscription text NOT NULL,
            topic text NOT NULL,
            seq bigint NOT NULL DEFAULT 0,
            maj_le timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (subscription, topic));
        CREATE TABLE IF NOT EXISTS bus_tentative (
            subscription text NOT NULL,
            topic text NOT NULL,
            seq bigint NOT NULL,
            tentatives int NOT NULL,
            derniere_erreur text NULL,
            maj_le timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (subscription, topic, seq));
        CREATE TABLE IF NOT EXISTS bus_lettre_morte (
            subscription text NOT NULL,
            topic text NOT NULL,
            seq bigint NOT NULL,
            message_id uuid NOT NULL,
            subject text NOT NULL,
            tentatives int NOT NULL,
            erreur text NULL,
            mise_le timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (subscription, topic, seq));
        """;

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _source;
    private readonly ILogger<BusDeveloppement> _logger;
    private readonly SemaphoreSlim _initialisation = new(1, 1);
    private volatile bool _pret;

    public BusDeveloppement(string connectionString, IHostEnvironment environment, ILogger<BusDeveloppement> logger)
    {
        VerifierEnvironnement(environment.EnvironmentName);
        _connectionString = connectionString;
        _source = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
    }

    /// <summary>Refuse le bus de développement hors Development, Test et Testing (démarrage du service en échec).</summary>
    public static void VerifierEnvironnement(string? environnement)
    {
        if (environnement is null || !EnvironnementsAutorises.Contains(environnement, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Le bus de développement (ConnectionStrings:{ConnectionStringName}) est refusé dans l'environnement « {environnement} » : " +
                $"il est réservé à {string.Join(", ", EnvironnementsAutorises)} (ADR 0004). Configurer Azure Service Bus.");
        }
    }

    /// <summary>Ouvre une connexion au bus, après création de la base et du schéma au premier appel.</summary>
    internal async Task<NpgsqlConnection> OuvrirAsync(CancellationToken cancellationToken)
    {
        await InitialiserAsync(cancellationToken);
        return await _source.OpenConnectionAsync(cancellationToken);
    }

    /// <summary>
    /// Crée la base (si le volume local est antérieur à <c>init-databases.sql</c>) et les tables du bus. Idempotent et sûr
    /// entre services démarrés en même temps (verrou consultatif).
    /// </summary>
    public async Task InitialiserAsync(CancellationToken cancellationToken)
    {
        if (_pret)
        {
            return;
        }

        await _initialisation.WaitAsync(cancellationToken);
        try
        {
            if (_pret)
            {
                return;
            }

            await CreerBaseSiAbsenteAsync(cancellationToken);
            await using var connexion = await _source.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connexion.BeginTransactionAsync(cancellationToken);
            await using (var commande = new NpgsqlCommand(Schema, connexion, transaction))
            {
                await commande.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _pret = true;
        }
        finally
        {
            _initialisation.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _source.DisposeAsync();
        _initialisation.Dispose();
    }

    private async Task CreerBaseSiAbsenteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var essai = await _source.OpenConnectionAsync(cancellationToken);
            return;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            // Base absente : volume créé avant l'ajout de la base « bus » à init-databases.sql.
        }

        var cible = new NpgsqlConnectionStringBuilder(_connectionString);
        var nom = cible.Database ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} ne désigne aucune base.");
        if (!NomDeBase().IsMatch(nom))
        {
            throw new InvalidOperationException($"Nom de base du bus de développement invalide : « {nom} » (lettres minuscules, chiffres, « _ »).");
        }

        var administration = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres", Pooling = false };
        await using var connexion = new NpgsqlConnection(administration.ConnectionString);
        await connexion.OpenAsync(cancellationToken);
        try
        {
#pragma warning disable CA2100 // Nom de base validé par expression régulière ; CREATE DATABASE n'accepte pas de paramètre.
            await using var commande = new NpgsqlCommand($"CREATE DATABASE \"{nom}\"", connexion);
#pragma warning restore CA2100
            await commande.ExecuteNonQueryAsync(cancellationToken);
            LogBaseCreee(_logger, nom);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DuplicateDatabase or PostgresErrorCodes.UniqueViolation)
        {
            // Créée en même temps par un autre service.
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex NomDeBase();

    [LoggerMessage(LogLevel.Information, "Base {Base} du bus de développement créée.")]
    private static partial void LogBaseCreee(ILogger logger, string @base);
}

/// <summary>
/// Garde de démarrage du bus de développement : sa construction refuse un environnement non admis (le service ne démarre
/// pas) ; au démarrage, il signale le mode et prépare le schéma.
/// </summary>
public sealed partial class GardeBusDeveloppement(BusDeveloppement bus, IHostEnvironment environment, ILogger<GardeBusDeveloppement> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogActif(logger, environment.EnvironmentName);
        try
        {
            await bus.InitialiserAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            // Base momentanément indisponible : publication et consommation réessaient à chaque passe.
            LogInitialisationReportee(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Warning, "Bus d'événements de développement PostgreSQL actif (environnement {Environnement}) : réservé au poste de développement et aux tests (ADR 0004).")]
    private static partial void LogActif(ILogger logger, string environnement);

    [LoggerMessage(LogLevel.Warning, "Initialisation du bus de développement reportée.")]
    private static partial void LogInitialisationReportee(ILogger logger, Exception exception);
}

/// <summary>
/// Publication sur le bus de développement : une ligne de <c>bus_message</c> par message de l'outbox, sujet = nom versionné
/// du contrat comme sur Service Bus. Les publications d'une même rubrique sont sérialisées (verrou consultatif de
/// transaction) : une ligne n'est jamais visible avant une ligne de numéro inférieur, le curseur ne saute donc aucun message.
/// </summary>
public sealed class PostgresMessagePublisher(BusDeveloppement bus) : IMessagePublisher
{
    private const string Insertion = """
        SELECT pg_advisory_xact_lock(4203911, hashtext(@topic));
        INSERT INTO bus_message (id, topic, subject, payload, correlation_id, occurred_at)
        VALUES (@id, @topic, @subject, @payload, @correlation_id, @occurred_at);
        """;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var connexion = await bus.OuvrirAsync(cancellationToken);
        await using var transaction = await connexion.BeginTransactionAsync(cancellationToken);
        await using (var commande = new NpgsqlCommand(Insertion, connexion, transaction))
        {
            commande.Parameters.AddWithValue("id", message.Id);
            commande.Parameters.AddWithValue("topic", message.Topic);
            commande.Parameters.AddWithValue("subject", message.EventType);
            commande.Parameters.AddWithValue("payload", message.Payload);
            commande.Parameters.AddWithValue("correlation_id", (object?)message.CorrelationId ?? DBNull.Value);
            commande.Parameters.AddWithValue("occurred_at", message.OccurredAt.ToUniversalTime());
            await commande.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
