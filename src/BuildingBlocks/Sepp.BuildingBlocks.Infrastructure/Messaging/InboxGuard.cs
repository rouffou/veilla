using Microsoft.EntityFrameworkCore;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Exécute l'effet d'un message au plus une fois par consommateur : l'identifiant du message est
/// enregistré dans l'inbox dans la même transaction que l'effet (ARC-31).
/// </summary>
public sealed class InboxGuard<TContext>(TContext db, TimeProvider timeProvider)
    where TContext : SeppDbContext
{
    /// <returns><c>true</c> si le message a été traité, <c>false</c> s'il l'avait déjà été.</returns>
    public async Task<bool> ExecuteOnceAsync(Guid messageId, string consumer, Func<CancellationToken, Task> effect, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await db.InboxMessages.AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, ct))
            {
                return false;
            }

            await effect(ct);
            db.InboxMessages.Add(new InboxMessage { MessageId = messageId, Consumer = consumer, ProcessedAt = timeProvider.GetUtcNow() });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                if (await db.InboxMessages.AsNoTracking().AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, ct))
                {
                    // Livraison concurrente du même message : une autre instance l'a traité.
                    return false;
                }

                throw;
            }

            await transaction.CommitAsync(ct);
            return true;
        }, cancellationToken);
    }
}
