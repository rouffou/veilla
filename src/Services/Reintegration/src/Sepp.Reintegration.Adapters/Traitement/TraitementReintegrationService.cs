using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Reintegration.Adapters.Persistence;
using Sepp.Reintegration.Application.Echanges;
using Sepp.Reintegration.Application.Minuteries;

namespace Sepp.Reintegration.Adapters.Traitement;

/// <summary>Traitements d'arrière-plan : section <c>Reintegration:Traitement</c>.</summary>
public sealed class OptionsTraitement
{
    public bool Actif { get; set; } = true;

    /// <summary>Intervalle entre deux passages ; une minute par défaut (les minuteries sont des dates, pas des secondes).</summary>
    public TimeSpan Intervalle { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed record ResultatTraitement(int DossiersTraites, int MinuteriesDeclenchees, int EchangesEnvoyes, int EchangesEnErreur, int EchangesRecus);

/// <summary>
/// ARC-33, ADR 0008 : minuteries des dossiers et des trajets stockées en base (<c>prochaine_echeance</c>), envoi des échanges eHealth
/// en attente et réception des réponses (SAN-64). Chaque lot est traité dans une transaction qui garde les verrous
/// <c>FOR UPDATE SKIP LOCKED</c> jusqu'à l'enregistrement : plusieurs instances ne traitent jamais le même processus ni le même envoi.
/// </summary>
public sealed partial class TraitementReintegrationService(
    IServiceScopeFactory scopeFactory,
    IOptions<OptionsTraitement> options,
    TimeProvider timeProvider,
    ILogger<TraitementReintegrationService> logger) : BackgroundService
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
                var r = await ExecuterUneFoisAsync(stoppingToken);
                if (r.DossiersTraites + r.EchangesEnvoyes + r.EchangesEnErreur + r.EchangesRecus > 0)
                {
                    LogTraitement(logger, r.DossiersTraites, r.MinuteriesDeclenchees, r.EchangesEnvoyes, r.EchangesEnErreur, r.EchangesRecus);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Seul le type est journalisé : aucun contenu médical ne doit atteindre les journaux (ARC-06).
                LogEchec(logger, ex.GetType().Name);
            }
        }
        while (await AttendreAsync(timer, stoppingToken));
    }

    /// <summary>Un passage complet (public pour les tests et le scénario de bout en bout).</summary>
    public async Task<ResultatTraitement> ExecuterUneFoisAsync(CancellationToken cancellationToken)
    {
        var (traites, declenchees, envoyes, erreurs) = (0, 0, 0, 0);
        while (true)
        {
            var lot = await EnTransactionAsync(sp => sp.GetRequiredService<TraiterMinuteries>().ExecuterLotAsync(cancellationToken), cancellationToken);
            traites += lot.DossiersTraites;
            declenchees += lot.MinuteriesDeclenchees;
            if (lot.DossiersTraites < TraiterMinuteries.TailleLot)
            {
                break;
            }
        }

        while (true)
        {
            var lot = await EnTransactionAsync(sp => sp.GetRequiredService<EnvoiEchangesEHealth>().ExecuterLotAsync(cancellationToken), cancellationToken);
            envoyes += lot.Envoyes;
            erreurs += lot.EnErreur;
            if (lot.Envoyes + lot.EnErreur < EnvoiEchangesEHealth.TailleLot)
            {
                break;
            }
        }

        var recus = await EnTransactionAsync(sp => sp.GetRequiredService<ReceptionEchangesEHealth>().ExecuterAsync(cancellationToken), cancellationToken);
        return new ResultatTraitement(traites, declenchees, envoyes, erreurs, recus);
    }

    private async Task<T> EnTransactionAsync<T>(Func<IServiceProvider, Task<T>> action, CancellationToken cancellationToken)
    {
        // La stratégie d'exécution (nouvelles tentatives) rejoue tout le lot dans un périmètre neuf : pas d'état résiduel.
        await using var strategieScope = scopeFactory.CreateAsyncScope();
        var strategie = strategieScope.ServiceProvider.GetRequiredService<ReintegrationDbContext>().Database.CreateExecutionStrategy();
        return await strategie.ExecuteAsync(async ct =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReintegrationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var resultat = await action(scope.ServiceProvider);
            await transaction.CommitAsync(ct);
            return resultat;
        }, cancellationToken);
    }

    private static async Task<bool> AttendreAsync(PeriodicTimer timer, CancellationToken stoppingToken)
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

    [LoggerMessage(LogLevel.Information, "Réintégration : {Dossiers} dossier(s) traité(s), {Minuteries} minuterie(s), {Envoyes} envoi(s) eHealth, {Erreurs} en erreur, {Recus} reçu(s).")]
    private static partial void LogTraitement(ILogger logger, int dossiers, int minuteries, int envoyes, int erreurs, int recus);

    [LoggerMessage(LogLevel.Error, "Échec du traitement d'arrière-plan de la réintégration ({TypeErreur}).")]
    private static partial void LogEchec(ILogger logger, string typeErreur);
}
