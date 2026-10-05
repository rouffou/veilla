using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Adapters.Planification;

/// <summary>Traitements planifiés des flux (configuration <c>Integrations:Planification</c>).</summary>
public sealed class OptionsPlanification
{
    /// <summary>Désactivé en développement et en test : les flux se lancent alors à la demande (API d'administration).</summary>
    public bool Actif { get; set; }

    public TimeSpan Intervalle { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Attente avant la première exécution (laisse le service et ses dépendances démarrer).</summary>
    public TimeSpan DelaiInitial { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Flux exécutés à chaque passage, dans cet ordre (BCE d'abord : les correspondances d'employeurs servent à DIMONA).</summary>
    public TypeFlux[] Flux { get; set; } = [TypeFlux.Bce, TypeFlux.Dimona, TypeFlux.RegistreNational];
}

/// <summary>
/// Exécution périodique des flux entrants puis purge des charges utiles échues (minimisation RGPD). Une instance
/// unique du service est prévue pour les traitements planifiés ; une exécution concurrente reste sûre (clé
/// d'idempotence unique du journal) mais échoue sur conflit et se rattrape au passage suivant.
/// </summary>
public sealed partial class PlanificateurFlux(IServiceScopeFactory scopes, IOptions<OptionsPlanification> options, ILogger<PlanificateurFlux> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.Actif)
        {
            JournalDesactive(logger);
            return;
        }

        await Task.Delay(configuration.DelaiInitial, stoppingToken);
        using var minuterie = new PeriodicTimer(configuration.Intervalle);
        do
        {
            await ExecuterUnPassageAsync(configuration.Flux, stoppingToken);
        }
        while (await minuterie.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Un passage complet : chaque flux, puis la purge ; un échec n'empêche pas les flux suivants.</summary>
    public async Task ExecuterUnPassageAsync(IEnumerable<TypeFlux> flux, CancellationToken cancellationToken)
    {
        foreach (var f in flux)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var rapport = await scope.ServiceProvider.GetRequiredService<ExecutionFlux>().ExecuterAsync(f, cancellationToken);
                JournalExecution(logger, f, rapport.Recus, rapport.DejaRecus, rapport.Traites, rapport.Rejetes, rapport.EnErreur);
                if (rapport.ErreurRecuperation is { } erreur)
                {
                    JournalRecuperationImpossible(logger, f, erreur);
                }
            }
#pragma warning disable CA1031 // Un flux en échec ne doit pas arrêter le planificateur ni les autres flux.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                // Seul le type est journalisé : un message d'exception pourrait reprendre des données reçues (DAT-06).
                JournalEchec(logger, f, ex.GetType().Name);
            }
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var purges = await scope.ServiceProvider.GetRequiredService<PurgeChargesUtiles>().ExecuterAsync(cancellationToken);
            JournalPurge(logger, purges);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            JournalEchecPurge(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Traitements planifiés des flux désactivés (Integrations:Planification:Actif).")]
    private static partial void JournalDesactive(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flux {Flux} : {Recus} reçus, {DejaRecus} déjà reçus, {Traites} traités, {Rejetes} rejetés, {EnErreur} en erreur.")]
    private static partial void JournalExecution(ILogger logger, TypeFlux flux, int recus, int dejaRecus, int traites, int rejetes, int enErreur);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flux {Flux} : récupération impossible — {Erreur}")]
    private static partial void JournalRecuperationImpossible(ILogger logger, TypeFlux flux, string erreur);

    [LoggerMessage(Level = LogLevel.Error, Message = "Flux {Flux} : exécution en échec ({TypeErreur}).")]
    private static partial void JournalEchec(ILogger logger, TypeFlux flux, string typeErreur);

    [LoggerMessage(Level = LogLevel.Information, Message = "Purge des charges utiles : {Purges} échange(s).")]
    private static partial void JournalPurge(ILogger logger, int purges);

    [LoggerMessage(Level = LogLevel.Error, Message = "Purge des charges utiles en échec ({TypeErreur}).")]
    private static partial void JournalEchecPurge(ILogger logger, string typeErreur);
}
