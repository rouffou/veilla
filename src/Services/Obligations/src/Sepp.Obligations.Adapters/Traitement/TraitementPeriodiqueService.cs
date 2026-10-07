using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Obligations.Application.Gestion;

namespace Sepp.Obligations.Adapters.Traitement;

/// <summary>Traitement périodique des échéances : section <c>Obligations:Traitement</c>.</summary>
public sealed class OptionsTraitementPeriodique
{
    public bool Actif { get; set; } = true;

    /// <summary>Intervalle entre deux traitements ; une fois par jour par défaut.</summary>
    public TimeSpan Intervalle { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>
/// Recalcule les travailleurs qui ont des obligations ouvertes puis publie ObligationEchue pour chaque date limite
/// dépassée (une seule fois par dépassement). Plusieurs instances peuvent tourner : le verrou optimiste et l'unicité
/// des obligations protègent contre les écritures concurrentes (le traitement suivant reprend).
/// </summary>
public sealed partial class TraitementPeriodiqueService(
    IServiceScopeFactory scopeFactory,
    IOptions<OptionsTraitementPeriodique> options,
    TimeProvider timeProvider,
    ILogger<TraitementPeriodiqueService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Actif)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Intervalle, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var resultat = await scope.ServiceProvider.GetRequiredService<TraitementEcheances>().ExecuterAsync(stoppingToken);
                LogTraitement(logger, resultat.PersonnesRecalculees, resultat.ObligationsEchues);
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

    [LoggerMessage(LogLevel.Information, "Traitement des échéances : {Personnes} travailleur(s) recalculé(s), {Echues} obligation(s) échue(s) signalée(s).")]
    private static partial void LogTraitement(ILogger logger, int personnes, int echues);

    [LoggerMessage(LogLevel.Error, "Échec du traitement périodique des échéances.")]
    private static partial void LogEchec(ILogger logger, Exception exception);
}
