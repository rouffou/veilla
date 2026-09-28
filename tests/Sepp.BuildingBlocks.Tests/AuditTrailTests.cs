using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts.Audit;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Journal d'audit par l'outbox (NF-04, ARC-32) : brique cliente utilisée par chaque service.</summary>
public sealed class AuditTrailTests : IAsyncLifetime
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
        services.AddSingleton<ICurrentUser, AuditUser>();
        services.AddSeppPersistence<TestDbContext>(configuration, "Test");
        services.AddSeppAuditTrail("surveillance-medicale", ZonesSensibilite.Medicale);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<List<OutboxMessage>> Outbox()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages
            .Where(m => m.EventType == "audit.acces-donnee-sensible.v1")
            .ToListAsync(Ct);
    }

    [Fact]
    public async Task Une_modification_est_tracee_dans_la_meme_transaction()
    {
        var dossier = new Dossier("A");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
            db.Dossiers.Add(dossier);
            audit.Enregistrer(ActionAudit.Creation, "dossier-sante", dossier.Id);

            (await Outbox()).ShouldBeEmpty("La trace n'est écrite qu'avec la modification.");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        var message = (await Outbox()).ShouldHaveSingleItem();
        message.Topic.ShouldBe("audit");
        var trace = JsonSerializer.Deserialize<AccesDonneeSensible>(message.Payload, EventSerialization.Options)!;
        trace.Service.ShouldBe("surveillance-medicale");
        trace.Zone.ShouldBe("medicale");
        trace.UtilisateurId.ShouldBe("cpmt-42");
        trace.Role.ShouldBe("cpmt,cpmt-dirigeant");
        trace.Action.ShouldBe("creation");
        trace.ObjetType.ShouldBe("dossier-sante");
        trace.ObjetId.ShouldBe(dossier.Id);
        trace.BrisDeGlace.ShouldBeFalse();
    }

    [Fact]
    public async Task Une_modification_abandonnee_ne_laisse_aucune_trace()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditTrail>().Enregistrer(ActionAudit.Modification, "dossier-sante", Guid.CreateVersion7());
        }

        (await Outbox()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_lecture_est_tracee_immediatement()
    {
        var objet = Guid.CreateVersion7();
        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditTrail>()
                .EnregistrerLectureAsync("dossier-sante", objet, "  Remplacement du Dr X  ", brisDeGlace: true, Ct);
        }

        var trace = JsonSerializer.Deserialize<AccesDonneeSensible>((await Outbox()).ShouldHaveSingleItem().Payload, EventSerialization.Options)!;
        trace.Action.ShouldBe("lecture");
        trace.ObjetId.ShouldBe(objet);
        trace.Motif.ShouldBe("Remplacement du Dr X");
        trace.BrisDeGlace.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_bris_de_glace_sans_motif_est_refuse()
    {
        await using var scope = _services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

        await Should.ThrowAsync<ArgumentException>(() => audit.EnregistrerLectureAsync("dossier-sante", Guid.CreateVersion7(), " ", brisDeGlace: true, Ct));
        Should.Throw<ArgumentException>(() => audit.Enregistrer(ActionAudit.Lecture, "dossier-sante", Guid.CreateVersion7(), new string('x', MotifAcces.LongueurMaximale + 1)));
        (await Outbox()).ShouldBeEmpty();
    }

    [Fact]
    public void Le_motif_est_verifie_avant_l_acces()
    {
        MotifAcces.Verifier(null, brisDeGlace: false).ShouldBeNull();
        MotifAcces.Verifier("Urgence", brisDeGlace: true).ShouldBeNull();
        MotifAcces.Verifier("", brisDeGlace: true)!.Code.ShouldBe("audit.motif-obligatoire");
        MotifAcces.Verifier(new string('x', 301), brisDeGlace: false)!.Code.ShouldBe("audit.motif-trop-long");
    }

    [Theory]
    [InlineData("Surveillance", ZonesSensibilite.Medicale)]
    [InlineData("psychosocial", "secret")]
    public void Une_identite_de_service_invalide_est_refusee_a_l_enregistrement(string service, string zone) =>
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSeppAuditTrail(service, zone));

    private sealed class AuditUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string UserId => "cpmt-42";

        public IReadOnlySet<string> Roles => new HashSet<string> { Application.Security.Roles.CpmtDirigeant, Application.Security.Roles.Cpmt };

        public bool HasPermission(string permission) => true;
    }
}
