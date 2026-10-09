using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Obligations.Adapters.Persistence;
using Sepp.Obligations.Application.Reprises;

namespace Sepp.Obligations.Adapters.Traitement;

/// <summary>Minuteries du processus de reprise : section <c>Obligations:Reprise</c>.</summary>
public sealed class OptionsMinuteriesReprise
{
    public bool Actif { get; set; } = true;

    /// <summary>Intervalle entre deux passages ; une minute par défaut (les minuteries sont des dates, pas des secondes).</summary>
    public TimeSpan IntervalleMinuteries { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// ARC-33, ADR 0008 : traite les minuteries du processus de reprise stockées en base (<c>prochaine_echeance</c>), à la place de
/// messages planifiés. Chaque lot est traité dans une transaction qui garde les verrous <c>FOR UPDATE SKIP LOCKED</c> jusqu'à
/// l'enregistrement : plusieurs instances (ou plusieurs passages simultanés) ne traitent jamais le même processus.
/// </summary>
public sealed partial class MinuteriesRepriseService(
    IServiceScopeFactory scopeFactory,
    IOptions<OptionsMinuteriesReprise> options,
    TimeProvider timeProvider,
    ILogger<MinuteriesRepriseService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Actif)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.IntervalleMinuteries, timeProvider);
        do
        {
            try
            {
                var resultat = await ExecuterUneFoisAsync(stoppingToken);
                if (resultat.ProcessusTraites > 0)
                {
                    LogTraitement(logger, resultat.ProcessusTraites, resultat.MinuteriesDeclenchees);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogEchec(logger, ex);
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    /// <summary>Traite tous les processus échus, lot par lot. Public pour les tests (deux passages simultanés).</summary>
    public async Task<ResultatMinuteries> ExecuterUneFoisAsync(CancellationToken cancellationToken)
    {
        var traites = 0;
        var declenchees = 0;
        while (true)
        {
            var lot = await ExecuterLotAsync(cancellationToken);
            traites += lot.ProcessusTraites;
            declenchees += lot.MinuteriesDeclenchees;
            if (lot.ProcessusTraites < TraiterMinuteriesReprise.TailleLot)
            {
                return new ResultatMinuteries(traites, declenchees);
            }
        }
    }

    private async Task<ResultatMinuteries> ExecuterLotAsync(CancellationToken cancellationToken)
    {
        // La stratégie d'exécution (nouvelles tentatives) rejoue tout le lot dans un périmètre neuf : pas d'état résiduel.
        await using var strategieScope = scopeFactory.CreateAsyncScope();
        var strategie = strategieScope.ServiceProvider.GetRequiredService<ObligationsDbContext>().Database.CreateExecutionStrategy();
        return await strategie.ExecuteAsync(async ct =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ObligationsDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var resultat = await scope.ServiceProvider.GetRequiredService<TraiterMinuteriesReprise>().ExecuterLotAsync(ct);
            await transaction.CommitAsync(ct);
            return resultat;
        }, cancellationToken);
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

    [LoggerMessage(LogLevel.Information, "Minuteries de reprise : {Processus} processus traité(s), {Minuteries} minuterie(s) déclenchée(s).")]
    private static partial void LogTraitement(ILogger logger, int processus, int minuteries);

    [LoggerMessage(LogLevel.Error, "Échec du traitement des minuteries de reprise.")]
    private static partial void LogEchec(ILogger logger, Exception exception);
}
