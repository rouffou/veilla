using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.Communications.Application.Expedition;

namespace Sepp.Communications.Adapters.Expedition;

/// <summary>Configuration <c>Communications:Expedition</c> : traitement périodique des messages échus.</summary>
public sealed class OptionsExpedition
{
    public bool Actif { get; set; } = true;

    public TimeSpan Intervalle { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Envoie périodiquement les messages échus (premiers envois et reprises, DOC-05). La réservation des messages évite que
/// plusieurs instances du service envoient le même message.
/// </summary>
public sealed partial class ExpeditionHostedService(IServiceScopeFactory scopes, IOptions<OptionsExpedition> options, ILogger<ExpeditionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.Actif)
        {
            return;
        }

        using var minuteur = new PeriodicTimer(configuration.Intervalle);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var traites = await scope.ServiceProvider.GetRequiredService<ExpediteurMessages>().ExpedierEchusAsync(stoppingToken);
                if (traites > 0)
                {
                    TraitementTermine(logger, traites);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Le traitement reprend au prochain tour : les messages restent échus.
                TraitementEnEchec(logger, ex);
            }
        }
        while (await minuteur.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Traites} message(s) traité(s) par l'expédition.")]
    private static partial void TraitementTermine(ILogger logger, int traites);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du traitement périodique des messages.")]
    private static partial void TraitementEnEchec(ILogger logger, Exception exception);
}
