using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.Sagas.Tests.Plateforme;

/// <summary>Un hôte de service démarré dans le processus du test, avec sa base, son publieur de test et son répartiteur.</summary>
public sealed class HoteService : IAsyncDisposable
{
    private readonly IAsyncDisposable _fabrique;

    private HoteService(string nom, IAsyncDisposable fabrique, IServiceProvider services, Func<HttpClient> nouveauClient,
        Func<CancellationToken, Task<int>> traiterOutbox, Func<Guid, string, string, CancellationToken, Task<int>> distribuer,
        Func<CancellationToken, Task<bool>> traiterRepublication)
    {
        Nom = nom;
        _fabrique = fabrique;
        Services = services;
        NouveauClient = nouveauClient;
        TraiterOutbox = traiterOutbox;
        Distribuer = distribuer;
        TraiterRepublication = traiterRepublication;
    }

    /// <summary>Nom du service dans le catalogue Terraform (<c>infra/variables.tf</c>).</summary>
    public string Nom { get; }

    public IServiceProvider Services { get; }

    public Func<HttpClient> NouveauClient { get; }

    /// <summary>Client HTTP authentifié (en-têtes de test) de l'hôte.</summary>
    public HttpClient Client(string role, string utilisateur = "test-user", Guid? affilie = null, Guid? personne = null) =>
        AuthentificationTest.Configurer(NouveauClient(), role, utilisateur, affilie, personne);

    /// <summary>Un lot de l'outbox (<c>OutboxProcessor.ProcessBatchAsync</c>) : publie vers le bus de test.</summary>
    public Func<CancellationToken, Task<int>> TraiterOutbox { get; }

    /// <summary>Remet un message au répartiteur idempotent du service (identifiant, contrat versionné, charge).</summary>
    public Func<Guid, string, string, CancellationToken, Task<int>> Distribuer { get; }

    /// <summary>
    /// Un lot de republication (<c>RepublicationProcessor.TraiterLotAsync</c>, ADR 0011) pour un producteur ; toujours
    /// <c>false</c> pour un service qui ne republie pas.
    /// </summary>
    public Func<CancellationToken, Task<bool>> TraiterRepublication { get; }

    /// <summary>
    /// Démarre l'hôte <typeparamref name="TProgram"/> (migrations comprises). Les seuls ajustements par rapport à la
    /// production : horloge commune, publieur de test à la place de Service Bus, authentification de test, et retrait des
    /// services d'arrière-plan de l'outbox et de la republication (la pompe du test appelle <c>ProcessBatchAsync</c> et
    /// <c>TraiterLotAsync</c> elle-même). Un service sans consommateur (Personnes) reçoit un répartiteur vide.
    /// </summary>
    public static HoteService Demarrer<TProgram, TContext>(
        string nom, string nomChaineConnexion, string chaineConnexion, IReadOnlyDictionary<string, string> reglages, TimeProvider horloge, BusDeTest bus)
        where TProgram : class
        where TContext : SeppDbContext
    {
        var republie = false;
        var fabrique = new WebApplicationFactory<TProgram>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting($"ConnectionStrings:{nomChaineConnexion}", chaineConnexion);
            b.UseSetting("Database:MigrateOnStartup", "true");
            foreach (var (cle, valeur) in reglages)
            {
                b.UseSetting(cle, valeur);
            }

            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton(horloge);
                s.RemoveAll<IMessagePublisher>();
                s.AddSingleton<IMessagePublisher>(new PublieurDeTest(nom, bus));
                foreach (var arrierePlan in s.Where(d => d.ServiceType == typeof(IHostedService)
                             && (d.ImplementationType == typeof(OutboxProcessor<TContext>) || d.ImplementationType == typeof(RepublicationProcessor<TContext>))).ToList())
                {
                    republie |= arrierePlan.ImplementationType == typeof(RepublicationProcessor<TContext>);
                    s.Remove(arrierePlan);
                }

                AuthentificationTest.Ajouter(s);
            });
        });
        _ = fabrique.Server;

        var outboxProcessor = ActivatorUtilities.CreateInstance<OutboxProcessor<TContext>>(fabrique.Services);
        var repartiteur = fabrique.Services.GetService<IntegrationEventDispatcher<TContext>>();
        Func<CancellationToken, Task<bool>> traiterRepublication = republie
            ? ActivatorUtilities.CreateInstance<RepublicationProcessor<TContext>>(fabrique.Services).TraiterLotAsync
            : _ => Task.FromResult(false);
        return new HoteService(
            nom,
            fabrique,
            fabrique.Services,
            fabrique.CreateClient,
            outboxProcessor.ProcessBatchAsync,
            (id, contrat, charge, ct) => repartiteur is null ? Task.FromResult(0) : repartiteur.DispatchAsync(id, contrat, charge, ct),
            traiterRepublication);
    }

    public async ValueTask DisposeAsync() => await _fabrique.DisposeAsync();
}
