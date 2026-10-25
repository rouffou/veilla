using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Republication;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Demande et suivi des republications (ADR 0011). La demande ne fait qu'enregistrer une ligne <c>EnCours</c> : les lots sont
/// écrits par <see cref="RepublicationProcessor{TContext}"/>. Permission <c>plateforme:republier</c> vérifiée ici (ARC-41).
/// </summary>
public sealed partial class Republications<TContext>(
    TContext db,
    IEnumerable<ISourceRepublication> sources,
    ICurrentUser user,
    TimeProvider timeProvider,
    ILogger<Republications<TContext>> logger) : IRepublications
    where TContext : SeppDbContext
{
    private const int ListeMax = 50;

    public IReadOnlyList<string> Types { get; } = [.. sources.Select(s => s.Type).Order(StringComparer.Ordinal)];

    public async Task<Result<RepublicationDto>> DemanderAsync(string type, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlateformeRepublier))
        {
            return Interdit();
        }

        if (string.IsNullOrWhiteSpace(type) || !Types.Contains(type.Trim(), StringComparer.Ordinal))
        {
            return Error.Validation("republication.type-inconnu", $"Type d'état inconnu. Types republiables : {string.Join(", ", Types)}.");
        }

        type = type.Trim();
        if (await db.Set<DemandeRepublication>().AnyAsync(r => r.Type == type && r.Statut == StatutRepublication.EnCours, cancellationToken))
        {
            return EnCours(type);
        }

        var demande = DemandeRepublication.Demander(type, user.UserId, timeProvider.GetUtcNow());
        db.Set<DemandeRepublication>().Add(demande);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Index unique partiel : une demande concurrente du même type vient d'être enregistrée.
            return EnCours(type);
        }

        LogDemandee(logger, demande.Id, type);
        return demande.Dto();
    }

    public async Task<Result<RepublicationDto>> ConsulterAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlateformeRepublier))
        {
            return Interdit();
        }

        var demande = await db.Set<DemandeRepublication>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return demande is null ? Error.NotFound("republication.inconnue", "Republication inconnue.") : demande.Dto();
    }

    public async Task<Result<IReadOnlyList<RepublicationDto>>> ListerAsync(CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlateformeRepublier))
        {
            return Interdit();
        }

        var demandes = await db.Set<DemandeRepublication>().AsNoTracking()
            .OrderByDescending(r => r.DemandeeLe)
            .Take(ListeMax)
            .ToListAsync(cancellationToken);
        return demandes.Select(d => d.Dto()).ToList();
    }

    private static Error Interdit() => Error.Forbidden("republication.interdite", "La republication est réservée à l'exploitation.");

    private static Error EnCours(string type) => Error.Conflict("republication.en-cours", $"Une republication « {type} » est déjà en cours.");

    [LoggerMessage(LogLevel.Information, "Republication {RepublicationId} demandée ({Type}).")]
    private static partial void LogDemandee(ILogger logger, Guid republicationId, string type);
}

/// <summary>
/// Écrit les instantanés des republications en cours dans l'outbox, un lot par intervalle (débit borné, ADR 0011) et seulement
/// si l'outbox n'est pas engorgée. La ligne de republication est verrouillée (<c>FOR UPDATE SKIP LOCKED</c>) : plusieurs
/// répliques ne traitent jamais le même lot. Lecture, écriture dans l'outbox et avancée du curseur forment une transaction.
/// </summary>
public sealed partial class RepublicationProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<RepublicationOptions> options,
    IOptions<OutboxOptions> outboxOptions,
    TimeProvider timeProvider,
    ILogger<RepublicationProcessor<TContext>> logger) : BackgroundService
    where TContext : SeppDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Intervalle, timeProvider);
        do
        {
            try
            {
                await TraiterLotAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLotEchoue(logger, ex);
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    /// <summary>Écrit au plus un lot ; renvoie <c>true</c> si une republication a avancé.</summary>
    public async Task<bool> TraiterLotAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var sources = scope.ServiceProvider.GetServices<ISourceRepublication>().ToList();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(ct => TraiterLotVerrouilleAsync(db, sources, ct), cancellationToken);
    }

    private async Task<bool> TraiterLotVerrouilleAsync(TContext db, IReadOnlyList<ISourceRepublication> sources, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var demande = await db.Set<DemandeRepublication>()
            .FromSql($"""
                SELECT * FROM republication
                WHERE statut = 'EnCours'
                ORDER BY demandee_le
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .FirstOrDefaultAsync(cancellationToken);
        if (demande is null)
        {
            return false;
        }

        // Messages encore à publier (ceux qui ont épuisé leurs tentatives ne seront plus repris : ils ne freinent pas).
        var maxTentatives = outboxOptions.Value.MaxAttempts;
        if (await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.Attempts < maxTentatives, cancellationToken) >= options.Value.ArriereOutboxMax)
        {
            return false;
        }

        var source = sources.FirstOrDefault(s => s.Type == demande.Type);
        if (source is null)
        {
            demande.Echouer("Type d'état sans source dans cette version du service.", timeProvider.GetUtcNow());
        }
        else
        {
            var lot = await source.LireAsync(demande.Curseur, options.Value.TailleLot, cancellationToken);
            var correlation = demande.Id.ToString();
            foreach (var instantane in lot.Instantanes)
            {
                ((IIntegrationEventOutbox)db).Add(instantane with { CorrelationId = correlation });
            }

            demande.Avancer(lot.Instantanes.Count, lot.CurseurSuivant, timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (demande.Statut != StatutRepublication.EnCours)
        {
            LogTerminee(logger, demande.Id, demande.Type, demande.Statut.ToString(), demande.Publies);
        }

        return true;
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

    [LoggerMessage(LogLevel.Error, "Échec d'un lot de republication.")]
    private static partial void LogLotEchoue(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "Republication {RepublicationId} ({Type}) : {Statut}, {Publies} instantanés.")]
    private static partial void LogTerminee(ILogger logger, Guid republicationId, string type, string statut, int publies);
}
