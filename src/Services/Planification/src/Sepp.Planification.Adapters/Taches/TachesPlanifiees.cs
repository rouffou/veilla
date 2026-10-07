using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.Synchronisation;
using Sepp.Planification.Domain;

namespace Sepp.Planification.Adapters.Taches;

/// <summary>Traitements périodiques (configuration <c>Planification:Taches</c>).</summary>
public sealed class OptionsTaches
{
    /// <summary>Désactivé en développement et en test : rappels et synchronisation se lancent alors à la demande (API).</summary>
    public bool Actif { get; set; }

    public TimeSpan Intervalle { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DelaiInitial { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>PLA-09 : synchronise aussi les agendas externes à chaque passage (jours à venir couverts).</summary>
    public bool SynchroniserAgendas { get; set; }

    public int HorizonSynchronisationJours { get; set; } = 30;
}

/// <summary>
/// SAN-13 : émission des rappels J-7 / J-1 et, si activée, synchronisation des agendas externes (PLA-09). Les rappels sont
/// idempotents (un rappel émis est noté sur le rendez-vous) : un passage manqué est rattrapé au passage suivant, et deux
/// instances ne produisent pas de doublon durable (le second enregistrement échoue sur le verrou optimiste).
/// </summary>
public sealed partial class TachesPlanifiees(IServiceScopeFactory scopes, IOptions<OptionsTaches> options, TimeProvider horloge, ILogger<TachesPlanifiees> logger)
    : BackgroundService
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
            await ExecuterUnPassageAsync(configuration, stoppingToken);
        }
        while (await minuterie.WaitForNextTickAsync(stoppingToken));
    }

    public async Task ExecuterUnPassageAsync(OptionsTaches configuration, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rappels = await scope.ServiceProvider.GetRequiredService<EmissionRappels>().ExecuterAsync(null, cancellationToken);
            JournalRappels(logger, rappels.PremiersRappels, rappels.SecondsRappels);
        }
#pragma warning disable CA1031 // Un passage en échec ne doit pas arrêter le planificateur ni la synchronisation.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Seul le type est journalisé : un message d'exception pourrait reprendre des données du dossier.
            JournalEchecRappels(logger, ex.GetType().Name);
        }

        if (!configuration.SynchroniserAgendas)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var aujourdhui = HeureBelge.Jour(horloge.GetUtcNow());
            var rapport = await scope.ServiceProvider.GetRequiredService<SynchronisationAgendas>()
                .ExecuterAsync(aujourdhui, aujourdhui.AddDays(configuration.HorizonSynchronisationJours), cancellationToken);
            JournalSynchronisation(logger, rapport.Ressources, rapport.EvenementsEcrits, rapport.RessourcesEnErreur);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            JournalEchecSynchronisation(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Traitements planifiés de la planification désactivés (Planification:Taches:Actif).")]
    private static partial void JournalDesactive(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rappels de rendez-vous : {Premiers} premier(s), {Seconds} second(s).")]
    private static partial void JournalRappels(ILogger logger, int premiers, int seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Émission des rappels en échec ({TypeErreur}).")]
    private static partial void JournalEchecRappels(ILogger logger, string typeErreur);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synchronisation des agendas : {Ressources} ressource(s), {Ecrits} événement(s) écrit(s), {EnErreur} en erreur.")]
    private static partial void JournalSynchronisation(ILogger logger, int ressources, int ecrits, int enErreur);

    [LoggerMessage(Level = LogLevel.Error, Message = "Synchronisation des agendas en échec ({TypeErreur}).")]
    private static partial void JournalEchecSynchronisation(ILogger logger, string typeErreur);
}
