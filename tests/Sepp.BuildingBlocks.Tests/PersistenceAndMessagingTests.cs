using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.BuildingBlocks.Tests;

[EventContract("test.dossier-cree", 1)]
public sealed record DossierCree(Guid DossierId) : IntegrationEvent;

public sealed class Dossier : AggregateRoot
{
    private Dossier()
    {
    }

    public Dossier(string intitule) : base(NewId()) => Intitule = intitule;

    public string Intitule { get; private set; } = string.Empty;

    public void Renommer(string intitule) => Intitule = intitule;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser user, TimeProvider clock)
    : SeppDbContext(options, user, clock)
{
    public DbSet<Dossier> Dossiers => Set<Dossier>();

    public List<Guid> Traites { get; } = [];

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Dossier>(b =>
        {
            b.ToTable("dossier");
            b.Property(d => d.Id).ValueGeneratedNever();
        });
}

public sealed class TestUser : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => "utilisateur-test";

    public IReadOnlySet<string> Roles => new HashSet<string>();

    public bool HasPermission(string permission) => true;
}

/// <summary>Gestionnaire de test : crée un dossier pour chaque événement reçu.</summary>
public sealed class CreerCopieHandler(TestDbContext db) : IIntegrationEventHandler<DossierCree>
{
    private static int _appels;

    public static int Appels => Volatile.Read(ref _appels);

    public Task HandleAsync(DossierCree integrationEvent, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _appels);
        db.Dossiers.Add(new Dossier($"copie de {integrationEvent.DossierId}"));
        return Task.CompletedTask;
    }
}

public sealed class PersistenceAndMessagingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ServiceProvider _services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = _postgres.GetConnectionString(),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser, TestUser>();
        services.AddSeppPersistence<TestDbContext>(configuration, "Test");
        services.AddSeppMessaging(configuration);
        services.AddIntegrationEventHandler<DossierCree, CreerCopieHandler>();
        services.AddSeppConsumer<TestDbContext>(configuration);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<T> InScope<T>(Func<TestDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    [Fact]
    public async Task Les_colonnes_d_audit_et_la_version_sont_renseignees()
    {
        var dossier = new Dossier("A");
        await InScope(async db => { db.Dossiers.Add(dossier); return await db.SaveChangesAsync(Ct); });
        await InScope(async db =>
        {
            (await db.Dossiers.SingleAsync(d => d.Id == dossier.Id, Ct)).Renommer("B");
            return await db.SaveChangesAsync(Ct);
        });

        var (version, createdBy) = await InScope(async db =>
        {
            var entry = db.Entry(await db.Dossiers.SingleAsync(d => d.Id == dossier.Id, Ct));
            return ((int)entry.Property(SeppDbContext.Version).CurrentValue!, (string)entry.Property(SeppDbContext.CreatedBy).CurrentValue!);
        });
        version.ShouldBe(2);
        createdBy.ShouldBe("utilisateur-test");
    }

    [Fact]
    public async Task Une_modification_concurrente_est_rejetee()
    {
        var dossier = new Dossier("A");
        await InScope(async db => { db.Dossiers.Add(dossier); return await db.SaveChangesAsync(Ct); });

        await using var scope1 = _services.CreateAsyncScope();
        await using var scope2 = _services.CreateAsyncScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<TestDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<TestDbContext>();
        (await db1.Dossiers.SingleAsync(d => d.Id == dossier.Id, Ct)).Renommer("B");
        (await db2.Dossiers.SingleAsync(d => d.Id == dossier.Id, Ct)).Renommer("C");
        await db1.SaveChangesAsync(Ct);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task La_suppression_est_logique()
    {
        var dossier = new Dossier("A");
        await InScope(async db => { db.Dossiers.Add(dossier); return await db.SaveChangesAsync(Ct); });
        await InScope(async db => { db.Dossiers.Remove(await db.Dossiers.SingleAsync(d => d.Id == dossier.Id, Ct)); return await db.SaveChangesAsync(Ct); });

        (await InScope(db => db.Dossiers.AnyAsync(d => d.Id == dossier.Id, Ct))).ShouldBeFalse();
        (await InScope(db => db.Dossiers.IgnoreQueryFilters().AnyAsync(d => d.Id == dossier.Id, Ct))).ShouldBeTrue();
    }

    [Fact]
    public async Task L_evenement_est_ecrit_dans_la_transaction_puis_publie_une_fois()
    {
        var dossier = new Dossier("A");
        await InScope(async db =>
        {
            db.Dossiers.Add(dossier);
            ((IIntegrationEventOutbox)db).Add(new DossierCree(dossier.Id));
            return await db.SaveChangesAsync(Ct);
        });

        var processor = _services.GetServices<IHostedService>().OfType<OutboxProcessor<TestDbContext>>().Single();
        (await processor.ProcessBatchAsync(Ct)).ShouldBe(1);
        (await processor.ProcessBatchAsync(Ct)).ShouldBe(0);

        var publie = ((InMemoryMessagePublisher)_services.GetRequiredService<IMessagePublisher>()).Published.ShouldHaveSingleItem();
        publie.EventType.ShouldBe("test.dossier-cree.v1");
        publie.Topic.ShouldBe("test");
        publie.Payload.ShouldContain(dossier.Id.ToString());
    }

    [Fact]
    public async Task Un_message_recu_deux_fois_n_est_traite_qu_une_fois()
    {
        var dispatcher = _services.GetRequiredService<IntegrationEventDispatcher<TestDbContext>>();
        var evenement = new DossierCree(Guid.CreateVersion7());
        var payload = System.Text.Json.JsonSerializer.Serialize(evenement, EventSerialization.Options);
        var avant = CreerCopieHandler.Appels;

        (await dispatcher.DispatchAsync(evenement.EventId, "test.dossier-cree.v1", payload, Ct)).ShouldBe(1);
        (await dispatcher.DispatchAsync(evenement.EventId, "test.dossier-cree.v1", payload, Ct)).ShouldBe(0);
        (await dispatcher.DispatchAsync(Guid.CreateVersion7(), "autre.evenement.v1", "{}", Ct)).ShouldBe(0);

        (CreerCopieHandler.Appels - avant).ShouldBe(1);
        (await InScope(db => db.Dossiers.CountAsync(d => d.Intitule == $"copie de {evenement.DossierId}", Ct))).ShouldBe(1);
        (await InScope(db => db.InboxMessages.CountAsync(m => m.MessageId == evenement.EventId, Ct))).ShouldBe(1);
    }

    [Fact]
    public void Sans_bus_configure_aucun_consommateur_service_bus_n_est_demarre() =>
        _services.GetServices<IHostedService>().Any(s => s.GetType().Name.StartsWith("ServiceBusConsumer", StringComparison.Ordinal)).ShouldBeFalse();

    [Fact]
    public void Les_options_d_outbox_ont_des_valeurs_par_defaut() =>
        _services.GetRequiredService<IOptions<OutboxOptions>>().Value.BatchSize.ShouldBe(50);
}
