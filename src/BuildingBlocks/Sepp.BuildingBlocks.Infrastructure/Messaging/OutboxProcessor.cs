using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 10;
}

/// <summary>
/// Publie les messages de l'outbox. Plusieurs instances peuvent tourner en parallèle :
/// les lignes sont verrouillées avec <c>FOR UPDATE SKIP LOCKED</c>. À l'arrêt, le lot en cours se termine (CTR-12).
/// </summary>
public sealed partial class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : SeppDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, timeProvider);
        do
        {
            try
            {
                while (await ProcessBatchAsync(CancellationToken.None) == options.Value.BatchSize && !stoppingToken.IsCancellationRequested)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex);
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    /// <summary>Publie un lot ; renvoie le nombre de messages traités.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(ct => PublishLockedBatchAsync(db, ct), cancellationToken);
    }

    private async Task<int> PublishLockedBatchAsync(TContext db, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_message
                WHERE processed_at IS NULL AND attempts < {options.Value.MaxAttempts}
                ORDER BY occurred_at
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                message.ProcessedAt = timeProvider.GetUtcNow();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                LogPublishFailed(logger, message.Id, message.EventType, ex);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
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

    [LoggerMessage(LogLevel.Error, "Échec du traitement d'un lot de l'outbox.")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Échec de publication du message {MessageId} ({EventType}).")]
    private static partial void LogPublishFailed(ILogger logger, Guid messageId, string eventType, Exception exception);
}
