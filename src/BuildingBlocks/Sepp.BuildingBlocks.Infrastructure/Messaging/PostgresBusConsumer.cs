using System.Reflection;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Consommateur du bus de développement (ADR 0004, amendement), même sémantique que <see cref="ServiceBusConsumer{TContext}"/> :
/// un abonnement <c>ServiceBus:SubscriptionName</c> par rubrique souscrite par le code, filtré par
/// <c>ServiceBus:SubjectFilters</c> (copie des <c>subject_filters</c> de Terraform), livraison au moins une fois et remise au
/// répartiteur idempotent (inbox, ARC-31). Chaque abonnement avance un curseur par rubrique (<c>bus_curseur</c>), validé
/// après traitement : un redémarrage reprend au premier message non acquitté. Un message en échec est retenté à chaque
/// passe, puis mis en lettre morte (<c>bus_lettre_morte</c>) après <see cref="BusDeveloppementOptions.MaxAttempts"/>
/// tentatives : il ne bloque pas l'abonnement au-delà. Un nouvel abonnement lit la rubrique depuis le début, comme un
/// abonnement Service Bus créé par Terraform avant le démarrage des services.
/// </summary>
public sealed partial class PostgresBusConsumer<TContext>(
    BusDeveloppement bus,
    IntegrationEventSubscriptions subscriptions,
    IntegrationEventDispatcher<TContext> dispatcher,
    IOptions<ServiceBusConsumerOptions> consumerOptions,
    IOptions<BusDeveloppementOptions> options,
    TimeProvider timeProvider,
    ILogger<PostgresBusConsumer<TContext>> logger) : BackgroundService
    where TContext : SeppDbContext
{
    private const int LongueurErreurMax = 2000;

    private string Abonnement => consumerOptions.Value.SubscriptionName;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Abonnement))
        {
            throw new InvalidOperationException("ServiceBus:SubscriptionName doit être configuré pour consommer des événements.");
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, timeProvider);
        do
        {
            try
            {
                // Lot complet : il reste probablement des messages, on enchaîne sans attendre.
                while (await TraiterAsync(stoppingToken) >= options.Value.BatchSize && !stoppingToken.IsCancellationRequested)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPasseEchouee(logger, Abonnement, ex);
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    /// <summary>
    /// Une passe sur chaque rubrique souscrite. Renvoie le nombre de messages acquittés (traités ou mis en lettre morte) ;
    /// un message en attente de nouvelle tentative n'est pas compté.
    /// </summary>
    public async Task<int> TraiterAsync(CancellationToken cancellationToken)
    {
        var acquittes = 0;
        foreach (var rubrique in subscriptions.Topics)
        {
            acquittes += await TraiterRubriqueAsync(rubrique, cancellationToken);
        }

        return acquittes;
    }

    /// <summary>Sujets acceptés sur la rubrique, ou <c>null</c> si l'abonnement reçoit tous ses messages.</summary>
    public string[]? Filtre(string rubrique) =>
        consumerOptions.Value.SubjectFilters.TryGetValue(rubrique, out var sujets)
        && sujets.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() is { Length: > 0 } retenus
            ? retenus
            : null;

    private async Task<int> TraiterRubriqueAsync(string rubrique, CancellationToken cancellationToken)
    {
        await using var connexion = await bus.OuvrirAsync(cancellationToken);
        await using var transaction = await connexion.BeginTransactionAsync(cancellationToken);

        await ExecuterAsync(connexion, transaction, """
            INSERT INTO bus_curseur (subscription, topic, seq) VALUES (@subscription, @topic, 0)
            ON CONFLICT (subscription, topic) DO NOTHING
            """, rubrique, cancellationToken);

        // Une seule instance du service consomme un abonnement à la fois ; les autres passent leur tour.
        long curseur;
        await using (var lecture = Commande(connexion, transaction, "SELECT seq FROM bus_curseur WHERE subscription = @subscription AND topic = @topic FOR UPDATE SKIP LOCKED", rubrique))
        {
            if (await lecture.ExecuteScalarAsync(cancellationToken) is not long position)
            {
                return 0;
            }

            curseur = position;
        }

        var messages = await LireMessagesAsync(connexion, transaction, rubrique, curseur, cancellationToken);
        var acquittes = 0;
        foreach (var message in messages)
        {
            if (!await TraiterMessageAsync(connexion, transaction, rubrique, message, cancellationToken))
            {
                // Ordre conservé : le message sera retenté à la prochaine passe.
                break;
            }

            curseur = message.Seq;
            acquittes++;
        }

        if (acquittes > 0)
        {
            await using var avance = Commande(connexion, transaction, "UPDATE bus_curseur SET seq = @seq, maj_le = now() WHERE subscription = @subscription AND topic = @topic", rubrique);
            avance.Parameters.AddWithValue("seq", curseur);
            await avance.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return acquittes;
    }

    /// <returns><c>true</c> si le message est acquitté (traité ou mis en lettre morte).</returns>
    private async Task<bool> TraiterMessageAsync(NpgsqlConnection connexion, NpgsqlTransaction transaction, string rubrique, MessageBus message, CancellationToken cancellationToken)
    {
        try
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = message.CorrelationId ?? string.Empty }))
            {
                await dispatcher.DispatchAsync(message.Id, message.Sujet, message.Charge, cancellationToken);
            }

            await using var oubli = Commande(connexion, transaction, "DELETE FROM bus_tentative WHERE subscription = @subscription AND topic = @topic AND seq = @seq", rubrique);
            oubli.Parameters.AddWithValue("seq", message.Seq);
            await oubli.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un gestionnaire qui lève avant sa première attente est appelé par réflexion : on journalise la vraie cause.
            var cause = ex is TargetInvocationException { InnerException: { } interne } ? interne : ex;
            var erreur = $"{cause.GetType().Name} : {cause.Message}";
            erreur = erreur.Length > LongueurErreurMax ? erreur[..LongueurErreurMax] : erreur;

            int tentatives;
            await using (var compte = Commande(connexion, transaction, """
                INSERT INTO bus_tentative (subscription, topic, seq, tentatives, derniere_erreur) VALUES (@subscription, @topic, @seq, 1, @erreur)
                ON CONFLICT (subscription, topic, seq) DO UPDATE
                SET tentatives = bus_tentative.tentatives + 1, derniere_erreur = excluded.derniere_erreur, maj_le = now()
                RETURNING tentatives
                """, rubrique))
            {
                compte.Parameters.AddWithValue("seq", message.Seq);
                compte.Parameters.AddWithValue("erreur", erreur);
                tentatives = (int)(await compte.ExecuteScalarAsync(cancellationToken))!;
            }

            if (tentatives < options.Value.MaxAttempts)
            {
                LogTentativeEchouee(logger, Abonnement, rubrique, message.Id, message.Sujet, tentatives, options.Value.MaxAttempts, cause);
                return false;
            }

            await using (var lettreMorte = Commande(connexion, transaction, """
                INSERT INTO bus_lettre_morte (subscription, topic, seq, message_id, subject, tentatives, erreur)
                VALUES (@subscription, @topic, @seq, @message_id, @subject, @tentatives, @erreur)
                ON CONFLICT (subscription, topic, seq) DO NOTHING;
                DELETE FROM bus_tentative WHERE subscription = @subscription AND topic = @topic AND seq = @seq;
                """, rubrique))
            {
                lettreMorte.Parameters.AddWithValue("seq", message.Seq);
                lettreMorte.Parameters.AddWithValue("message_id", message.Id);
                lettreMorte.Parameters.AddWithValue("subject", message.Sujet);
                lettreMorte.Parameters.AddWithValue("tentatives", tentatives);
                lettreMorte.Parameters.AddWithValue("erreur", erreur);
                await lettreMorte.ExecuteNonQueryAsync(cancellationToken);
            }

            LogLettreMorte(logger, Abonnement, rubrique, message.Id, message.Sujet, tentatives, cause);
            return true;
        }
    }

    private async Task<List<MessageBus>> LireMessagesAsync(NpgsqlConnection connexion, NpgsqlTransaction transaction, string rubrique, long curseur, CancellationToken cancellationToken)
    {
        var filtre = Filtre(rubrique);
        await using var commande = Commande(connexion, transaction, filtre is null
            ? "SELECT seq, id, subject, payload, correlation_id FROM bus_message WHERE topic = @topic AND seq > @seq ORDER BY seq LIMIT @limite"
            : "SELECT seq, id, subject, payload, correlation_id FROM bus_message WHERE topic = @topic AND seq > @seq AND subject = ANY(@sujets) ORDER BY seq LIMIT @limite", rubrique);
        commande.Parameters.AddWithValue("seq", curseur);
        commande.Parameters.AddWithValue("limite", options.Value.BatchSize);
        if (filtre is not null)
        {
            commande.Parameters.AddWithValue("sujets", filtre);
        }

        var messages = new List<MessageBus>();
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken);
        while (await lecteur.ReadAsync(cancellationToken))
        {
            messages.Add(new MessageBus(
                lecteur.GetInt64(0),
                lecteur.GetGuid(1),
                lecteur.GetString(2),
                lecteur.GetString(3),
                await lecteur.IsDBNullAsync(4, cancellationToken) ? null : lecteur.GetString(4)));
        }

        return messages;
    }

    private NpgsqlCommand Commande(NpgsqlConnection connexion, NpgsqlTransaction transaction, string sql, string rubrique)
    {
        var commande = new NpgsqlCommand(sql, connexion, transaction);
        commande.Parameters.AddWithValue("subscription", Abonnement);
        commande.Parameters.AddWithValue("topic", rubrique);
        return commande;
    }

    private async Task ExecuterAsync(NpgsqlConnection connexion, NpgsqlTransaction transaction, string sql, string rubrique, CancellationToken cancellationToken)
    {
        await using var commande = Commande(connexion, transaction, sql, rubrique);
        await commande.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed record MessageBus(long Seq, Guid Id, string Sujet, string Charge, string? CorrelationId);

    [LoggerMessage(LogLevel.Error, "Échec d'une passe du bus de développement (abonnement {Abonnement}).")]
    private static partial void LogPasseEchouee(ILogger logger, string abonnement, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Échec du message {MessageId} ({Sujet}) sur {Rubrique}/{Abonnement} : tentative {Tentative} sur {Maximum}.")]
    private static partial void LogTentativeEchouee(ILogger logger, string abonnement, string rubrique, Guid messageId, string sujet, int tentative, int maximum, Exception exception);

    [LoggerMessage(LogLevel.Error, "Message {MessageId} ({Sujet}) mis en lettre morte sur {Rubrique}/{Abonnement} après {Tentatives} tentatives (table bus_lettre_morte).")]
    private static partial void LogLettreMorte(ILogger logger, string abonnement, string rubrique, Guid messageId, string sujet, int tentatives, Exception exception);
}
