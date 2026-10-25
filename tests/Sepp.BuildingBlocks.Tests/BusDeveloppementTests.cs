using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.BuildingBlocks.Tests;

[EventContract("colis.colis-envoye", 1)]
public sealed record ColisEnvoye(Guid ColisId) : IntegrationEvent;

[EventContract("colis.colis-recu", 1)]
public sealed record ColisRecu(Guid ColisId) : IntegrationEvent;

[EventContract("colis.colis-perdu", 1)]
public sealed record ColisPerdu(Guid ColisId) : IntegrationEvent;

/// <summary>Effets observés par un hôte de test : contrat et identifiant de chaque événement traité.</summary>
public sealed class JournalColis
{
    public ConcurrentQueue<(string Contrat, Guid ColisId)> Traites { get; } = new();
}

public sealed class ColisEnvoyeHandler(JournalColis journal) : IIntegrationEventHandler<ColisEnvoye>
{
    public Task HandleAsync(ColisEnvoye integrationEvent, CancellationToken cancellationToken)
    {
        journal.Traites.Enqueue(("colis.colis-envoye.v1", integrationEvent.ColisId));
        return Task.CompletedTask;
    }
}

public sealed class ColisRecuHandler(JournalColis journal) : IIntegrationEventHandler<ColisRecu>
{
    public Task HandleAsync(ColisRecu integrationEvent, CancellationToken cancellationToken)
    {
        journal.Traites.Enqueue(("colis.colis-recu.v1", integrationEvent.ColisId));
        return Task.CompletedTask;
    }
}

/// <summary>Gestionnaire toujours en échec : alimente la lettre morte.</summary>
public sealed class ColisPerduHandler : IIntegrationEventHandler<ColisPerdu>
{
    public Task HandleAsync(ColisPerdu integrationEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("échec simulé");
}

internal sealed class EnvironnementDeTest(string nom) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = nom;

    public string ApplicationName { get; set; } = "Sepp.BuildingBlocks.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

public sealed class PostgresBusFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    public string Connexion(string baseDeDonnees) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = baseDeDonnees }.ConnectionString;

    public async Task<string> CreerBaseAsync(string prefixe)
    {
        var nom = $"{prefixe}_{Guid.NewGuid():N}";
        await using var connexion = new NpgsqlConnection(_postgres.GetConnectionString());
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand($"CREATE DATABASE \"{nom}\"", connexion);
        await commande.ExecuteNonQueryAsync();
        return nom;
    }
}

/// <summary>
/// Bus de développement PostgreSQL (ADR 0004, amendement, ARC-31) : publication par l'outbox, consommation par curseur et
/// par abonnement avec filtres de sujets comme Service Bus, reprise après redémarrage, rejeu idempotent, lettre morte et
/// refus hors Development, Test et Testing.
/// </summary>
public sealed class BusDeveloppementTests(PostgresBusFixture postgres) : IClassFixture<PostgresBusFixture>, IAsyncLifetime
{
    private readonly List<ServiceProvider> _hotes = [];
    private string _bus = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        // Base du bus non créée d'avance : le bus la crée (volume local antérieur à init-databases.sql).
        _bus = $"bus_{Guid.NewGuid():N}";
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hote in _hotes)
        {
            await hote.DisposeAsync();
        }
    }

    private async Task<ServiceProvider> HoteAsync(string abonnement, string? baseService = null, Dictionary<string, string?>? reglages = null, string environnement = "Development")
    {
        baseService ??= await postgres.CreerBaseAsync("service");
        var valeurs = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = postgres.Connexion(baseService),
            ["ConnectionStrings:BusDeveloppement"] = postgres.Connexion(_bus),
            ["ServiceBus:SubscriptionName"] = abonnement,
            ["BusDeveloppement:MaxAttempts"] = "3",
        };
        foreach (var (cle, valeur) in reglages ?? [])
        {
            valeurs[cle] = valeur;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new EnvironnementDeTest(environnement));
        services.AddSingleton<ICurrentUser, TestUser>();
        services.AddSingleton<JournalColis>();
        services.AddSeppPersistence<TestDbContext>(configuration, "Test");
        services.AddSeppMessaging(configuration);
        services.AddIntegrationEventHandler<ColisEnvoye, ColisEnvoyeHandler>();
        services.AddIntegrationEventHandler<ColisRecu, ColisRecuHandler>();
        services.AddIntegrationEventHandler<ColisPerdu, ColisPerduHandler>();
        services.AddSeppConsumer<TestDbContext>(configuration);
        var hote = services.BuildServiceProvider();
        _hotes.Add(hote);

        await using var scope = hote.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync(Ct);
        return hote;
    }

    private static PostgresBusConsumer<TestDbContext> Consommateur(ServiceProvider hote) =>
        hote.GetServices<IHostedService>().OfType<PostgresBusConsumer<TestDbContext>>().Single();

    private static Task PublierAsync(ServiceProvider hote, IntegrationEvent evenement) =>
        hote.GetRequiredService<IMessagePublisher>().PublishAsync(OutboxMessage.From(evenement), Ct);

    private static List<(string Contrat, Guid ColisId)> Traites(ServiceProvider hote) => [.. hote.GetRequiredService<JournalColis>().Traites];

    private async Task<T?> ScalaireAsync<T>(string sql)
    {
        await using var connexion = new NpgsqlConnection(postgres.Connexion(_bus));
        await connexion.OpenAsync(Ct);
        await using var commande = new NpgsqlCommand(sql, connexion);
        return (T?)await commande.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public async Task Un_evenement_ecrit_dans_l_outbox_est_publie_sur_le_bus_puis_consomme()
    {
        var producteur = await HoteAsync("producteur");
        var consommateur = await HoteAsync("consommateur");
        var colis = Guid.CreateVersion7();

        await using (var scope = producteur.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            ((IIntegrationEventOutbox)db).Add(new ColisEnvoye(colis) { CorrelationId = "corr-1" });
            await db.SaveChangesAsync(Ct);
        }

        producteur.GetRequiredService<IMessagePublisher>().ShouldBeOfType<PostgresMessagePublisher>();
        var outbox = producteur.GetServices<IHostedService>().OfType<OutboxProcessor<TestDbContext>>().Single();
        (await outbox.ProcessBatchAsync(Ct)).ShouldBe(1);

        (await ScalaireAsync<string>("SELECT subject || '|' || topic || '|' || correlation_id FROM bus_message"))
            .ShouldBe("colis.colis-envoye.v1|colis|corr-1");

        (await Consommateur(consommateur).TraiterAsync(Ct)).ShouldBe(1);
        Traites(consommateur).ShouldBe([("colis.colis-envoye.v1", colis)]);
        (await Consommateur(consommateur).TraiterAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Plusieurs_abonnements_a_une_meme_rubrique_recoivent_chacun_les_messages_selon_leur_filtre()
    {
        var tous = await HoteAsync("tous");
        var filtre = await HoteAsync("filtre", reglages: new() { ["ServiceBus:SubjectFilters:colis:0"] = "colis.colis-recu.v1", ["ServiceBus:SubjectFilters:colis:1"] = "" });
        var envoye = Guid.CreateVersion7();
        var recu = Guid.CreateVersion7();

        await PublierAsync(tous, new ColisEnvoye(envoye));
        await PublierAsync(tous, new ColisRecu(recu));

        Consommateur(filtre).Filtre("colis").ShouldBe(["colis.colis-recu.v1"]);
        Consommateur(tous).Filtre("colis").ShouldBeNull();
        (await Consommateur(tous).TraiterAsync(Ct)).ShouldBe(2);
        (await Consommateur(filtre).TraiterAsync(Ct)).ShouldBe(1);

        Traites(tous).ShouldBe([("colis.colis-envoye.v1", envoye), ("colis.colis-recu.v1", recu)]);
        Traites(filtre).ShouldBe([("colis.colis-recu.v1", recu)]);
        (await ScalaireAsync<long>("SELECT count(*) FROM bus_curseur WHERE topic = 'colis'")).ShouldBe(2);
    }

    [Fact]
    public async Task Apres_redemarrage_l_abonnement_reprend_a_son_curseur()
    {
        var baseService = await postgres.CreerBaseAsync("service");
        var premier = await HoteAsync("reprise", baseService);
        await PublierAsync(premier, new ColisEnvoye(Guid.CreateVersion7()));
        await PublierAsync(premier, new ColisEnvoye(Guid.CreateVersion7()));
        (await Consommateur(premier).TraiterAsync(Ct)).ShouldBe(2);
        await premier.DisposeAsync();
        _hotes.Remove(premier);

        var nouveau = Guid.CreateVersion7();
        var redemarre = await HoteAsync("reprise", baseService);
        await PublierAsync(redemarre, new ColisEnvoye(nouveau));

        // Seul le message publié après le dernier acquittement est relu.
        (await Consommateur(redemarre).TraiterAsync(Ct)).ShouldBe(1);
        Traites(redemarre).ShouldBe([("colis.colis-envoye.v1", nouveau)]);
    }

    [Fact]
    public async Task Un_rejeu_ou_une_double_publication_n_execute_l_effet_qu_une_fois()
    {
        var hote = await HoteAsync("rejeu");
        var evenement = new ColisEnvoye(Guid.CreateVersion7());
        await PublierAsync(hote, evenement);
        await PublierAsync(hote, evenement); // republication par l'outbox (livraison au moins une fois)

        (await Consommateur(hote).TraiterAsync(Ct)).ShouldBe(2);

        // Rejeu complet : curseur remis à zéro, comme après un arrêt avant l'acquittement.
        (await ScalaireAsync<int>("WITH r AS (UPDATE bus_curseur SET seq = 0 RETURNING 1) SELECT count(*)::int FROM r")).ShouldBe(1);
        (await Consommateur(hote).TraiterAsync(Ct)).ShouldBe(2);

        Traites(hote).Count.ShouldBe(1);
        await using var scope = hote.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TestDbContext>().InboxMessages.CountAsync(m => m.MessageId == evenement.EventId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Un_message_en_echec_repete_passe_en_lettre_morte_sans_bloquer_l_abonnement()
    {
        var hote = await HoteAsync("lettre-morte");
        var perdu = new ColisPerdu(Guid.CreateVersion7());
        var suivant = Guid.CreateVersion7();
        await PublierAsync(hote, perdu);
        await PublierAsync(hote, new ColisEnvoye(suivant));
        var consommateur = Consommateur(hote);

        // Ordre conservé pendant les nouvelles tentatives (MaxAttempts = 3).
        (await consommateur.TraiterAsync(Ct)).ShouldBe(0);
        (await consommateur.TraiterAsync(Ct)).ShouldBe(0);
        Traites(hote).ShouldBeEmpty();
        (await ScalaireAsync<int>("SELECT tentatives FROM bus_tentative")).ShouldBe(2);

        (await consommateur.TraiterAsync(Ct)).ShouldBe(2);
        Traites(hote).ShouldBe([("colis.colis-envoye.v1", suivant)]);
        (await ScalaireAsync<string>("SELECT subscription || '|' || subject || '|' || tentatives || '|' || message_id FROM bus_lettre_morte"))
            .ShouldBe($"lettre-morte|colis.colis-perdu.v1|3|{perdu.EventId}");
        (await ScalaireAsync<string>("SELECT erreur FROM bus_lettre_morte")).ShouldNotBeNull().ShouldContain("échec simulé");
        (await ScalaireAsync<long>("SELECT count(*) FROM bus_tentative")).ShouldBe(0);
        (await consommateur.TraiterAsync(Ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Recette")]
    public async Task Le_bus_de_developpement_est_refuse_hors_Development_Test_et_Testing(string environnement)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BusDeveloppement"] = postgres.Connexion(_bus),
            [HostDefaults.EnvironmentKey] = environnement,
        }).Build();

        // Dès l'enregistrement si la configuration connaît l'environnement.
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSeppMessaging(configuration))
            .Message.ShouldContain("refusé");

        // Sinon à la construction du bus : la garde de démarrage échoue, donc le service ne démarre pas.
        var hote = await HoteAsync("refus", environnement: environnement);
        Should.Throw<InvalidOperationException>(() => hote.GetServices<IHostedService>().ToList()).Message.ShouldContain("refusé");
        Should.Throw<InvalidOperationException>(() => hote.GetRequiredService<IMessagePublisher>()).Message.ShouldContain("refusé");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Testing")]
    public void Le_bus_de_developpement_est_admis_en_Development_Test_et_Testing(string environnement) =>
        Should.NotThrow(() => BusDeveloppement.VerifierEnvironnement(environnement));

    [Fact]
    public void Sans_chaine_BusDeveloppement_la_publication_reste_en_memoire()
    {
        var services = new ServiceCollection();
        services.AddSeppMessaging(new ConfigurationBuilder().Build());
        using var hote = services.BuildServiceProvider();
        hote.GetRequiredService<IMessagePublisher>().ShouldBeOfType<InMemoryMessagePublisher>();
        hote.GetServices<IHostedService>().ShouldBeEmpty();
    }

    [Fact]
    public void Le_bus_de_developpement_et_Service_Bus_ne_peuvent_etre_configures_ensemble()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BusDeveloppement"] = "Host=localhost;Database=bus",
            ["ServiceBus:FullyQualifiedNamespace"] = "sepp.servicebus.windows.net",
        }).Build();

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSeppMessaging(configuration)).Message.ShouldContain("Deux bus");
    }
}
