using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Audit.Application.Journal;
using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;

namespace Sepp.Audit.Adapters.Persistence;

/// <summary>Conservation du journal d'audit (NF-04) : section <c>Audit:Conservation</c>.</summary>
public sealed class ConservationOptions
{
    /// <summary>Durée de conservation en années, au moins <see cref="PolitiqueConservation.MinimumAnnees"/>.</summary>
    public int Annees { get; set; } = PolitiqueConservation.MinimumAnnees;

    public bool PurgeActive { get; set; } = true;

    public TimeSpan IntervallePurge { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>Purge périodique des entrées échues. Plusieurs instances peuvent tourner : la purge verrouille chaque chaîne.</summary>
public sealed partial class PurgeJournalService(
    IServiceScopeFactory scopeFactory,
    IOptions<ConservationOptions> options,
    TimeProvider timeProvider,
    ILogger<PurgeJournalService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PurgeActive)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.IntervallePurge, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PurgerJournal, ResultatPurge>>();
                var resultat = await handler.HandleAsync(new PurgerJournal(options.Value.Annees), stoppingToken);
                if (resultat.IsSuccess && resultat.Value.EntreesPurgeesParZone.Values.Sum() > 0)
                {
                    LogPurge(logger, string.Join(", ", resultat.Value.EntreesPurgeesParZone.Select(kv => $"{kv.Key}={kv.Value}")));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogEchec(logger, ex);
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
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

    [LoggerMessage(LogLevel.Information, "Purge légale du journal d'audit : {Detail}.")]
    private static partial void LogPurge(ILogger logger, string detail);

    [LoggerMessage(LogLevel.Error, "Échec de la purge légale du journal d'audit.")]
    private static partial void LogEchec(ILogger logger, Exception exception);
}
