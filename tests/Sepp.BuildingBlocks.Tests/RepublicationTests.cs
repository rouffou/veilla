using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Republication;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.BuildingBlocks.Tests;

[EventContract("test.dossier-instantane", 1)]
public sealed record DossierInstantane(Guid DossierId, string Intitule, DateTimeOffset EtatDu) : IntegrationEvent;

public sealed class ProducteurDbContext(DbContextOptions<ProducteurDbContext> options, ICurrentUser user, TimeProvider clock)
    : SeppDbContext(options, user, clock)
{
    public DbSet<Dossier> Dossiers => Set<Dossier>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dossier>(b =>
        {
            b.ToTable("dossier");
            b.Property(d => d.Id).ValueGeneratedNever();
        });
        modelBuilder.AjouterRepublications();
    }
}

/// <summary>Source de test : parcours par identifiant, comme les sources des producteurs.</summary>
public sealed class RepublicationDossiers(ProducteurDbContext db) : ISourceRepublication
{
    public string Type => "dossiers";

    public async Task<LotRepublication> LireAsync(string? curseur, int taille, CancellationToken cancellationToken)
    {
        var apres = CurseurRepublication.Lire(curseur);
        var dossiers = db.Dossiers.AsNoTracking();
        if (apres is { } id)
        {
            dossiers = dossiers.Where(d => d.Id.CompareTo(id) > 0);
        }

        var lignes = await dossiers.OrderBy(d => d.Id).Take(taille)
            .Select(d => new { d.Id, d.Intitule, EtatDu = EF.Property<DateTimeOffset>(d, SeppDbContext.UpdatedAt) })
            .ToListAsync(cancellationToken);
        return new LotRepublication(
            [.. lignes.Select(l => new DossierInstantane(l.Id, l.Intitule, l.EtatDu))],
            CurseurRepublication.Suivant(lignes, taille, l => l.Id));
    }
}

/// <summary>Utilisateur de test dont on choisit les permissions.</summary>
public sealed class UtilisateurReglable : ICurrentUser
{
    public HashSet<string> Permissions { get; } = [];

    public bool IsAuthenticated => true;

    public string UserId => "exploitant-test";

    public IReadOnlySet<string> Roles => new HashSet<string>();

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// Socle de republication (ADR 0011) contre un vrai PostgreSQL : demande, lots successifs écrits dans l'outbox avec le curseur
/// dans la même transaction, contre-pression de l'outbox, droits et conflits.
/// </summary>
public sealed class RepublicationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly UtilisateurReglable _utilisateur = new();
    private ServiceProvider _services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = _postgres.GetConnectionString(),
            ["Republication:TailleLot"] = "2",
            ["Republication:ArriereOutboxMax"] = "100",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(_utilisateur);
        services.AddSeppPersistence<ProducteurDbContext>(configuration, "Test");
        services.AddSeppRepublication<ProducteurDbContext>(configuration);
        services.AddRepublicationSource<RepublicationDossiers>();
        _services = services.BuildServiceProvider();
        _utilisateur.Permissions.Add(Permissions.PlateformeRepublier);

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProducteurDbContext>().Database.EnsureCreatedAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Result<RepublicationDto>> Demander(string type) =>
        InScope(sp => sp.GetRequiredService<IRepublications>().DemanderAsync(type, Ct));

    private RepublicationProcessor<ProducteurDbContext> Processeur() =>
        ActivatorUtilities.CreateInstance<RepublicationProcessor<ProducteurDbContext>>(_services);

    private async Task ViderAsync() => await InScope(async sp =>
    {
        var db = sp.GetRequiredService<ProducteurDbContext>();
        await db.OutboxMessages.ExecuteDeleteAsync(Ct);
        await db.Set<DemandeRepublication>().ExecuteDeleteAsync(Ct);
        return await db.Dossiers.ExecuteDeleteAsync(Ct);
    });

    private async Task<List<Guid>> CreerDossiersAsync(int nombre) => await InScope(async sp =>
    {
        var db = sp.GetRequiredService<ProducteurDbContext>();
        var dossiers = Enumerable.Range(1, nombre).Select(i => new Dossier($"Dossier {i}")).ToList();
        db.Dossiers.AddRange(dossiers);
        await db.SaveChangesAsync(Ct);
        return dossiers.Select(d => d.Id).ToList();
    });

    private Task<List<OutboxMessage>> InstantanesAsync() =>
        InScope(sp => sp.GetRequiredService<ProducteurDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.EventType == "test.dossier-instantane.v1").ToListAsync(Ct));

    [Fact]
    public async Task Une_republication_ecrit_l_etat_courant_par_lots_jusqu_a_epuisement_de_la_source()
    {
        await ViderAsync();
        var dossiers = await CreerDossiersAsync(5);

        var demande = (await Demander("dossiers")).Value;
        demande.Statut.ShouldBe("EnCours");

        var lots = 0;
        while (await Processeur().TraiterLotAsync(Ct))
        {
            lots++;
            lots.ShouldBeLessThan(10);
        }

        lots.ShouldBe(3, "lots de 2, 2 et 1 avec une taille de lot de 2.");
        var suivi = (await InScope(sp => sp.GetRequiredService<IRepublications>().ConsulterAsync(demande.Id, Ct))).Value;
        suivi.Statut.ShouldBe("Terminee");
        suivi.Publies.ShouldBe(5);
        suivi.Lots.ShouldBe(3);
        suivi.TermineeLe.ShouldNotBeNull();
        suivi.DemandeePar.ShouldBe("exploitant-test");

        var instantanes = await InstantanesAsync();
        instantanes.Count.ShouldBe(5);
        instantanes.ShouldAllBe(m => m.CorrelationId == demande.Id.ToString() && m.Topic == "test");
        dossiers.ShouldAllBe(id => instantanes.Count(m => m.Payload.Contains(id.ToString())) == 1);
        instantanes.ShouldAllBe(m => m.Payload.Contains("\"etatDu\""));
    }

    [Fact]
    public async Task Aucun_lot_n_est_ecrit_tant_que_l_outbox_est_engorgee()
    {
        await ViderAsync();
        await CreerDossiersAsync(1);
        await InScope(async sp =>
        {
            var db = sp.GetRequiredService<ProducteurDbContext>();
            for (var i = 0; i < 100; i++)
            {
                ((IIntegrationEventOutbox)db).Add(new DossierCree(Guid.CreateVersion7()));
            }

            return await db.SaveChangesAsync(Ct);
        });
        await Demander("dossiers");

        (await Processeur().TraiterLotAsync(Ct)).ShouldBeFalse();
        (await InstantanesAsync()).ShouldBeEmpty();

        // L'outbox se vide : la republication reprend.
        await InScope(sp => sp.GetRequiredService<ProducteurDbContext>().OutboxMessages
            .Where(m => m.EventType == "test.dossier-cree.v1")
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.ProcessedAt, DateTimeOffset.UtcNow), Ct));
        (await Processeur().TraiterLotAsync(Ct)).ShouldBeTrue();
        (await InstantanesAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Une_seule_republication_par_type_est_en_cours_et_le_type_doit_etre_connu()
    {
        await ViderAsync();

        (await Demander("dossiers")).IsSuccess.ShouldBeTrue();
        (await Demander("dossiers")).Error!.Kind.ShouldBe(ErrorKind.Conflict);

        var inconnu = await Demander("inconnu");
        inconnu.Error!.Kind.ShouldBe(ErrorKind.Validation);
        inconnu.Error.Message.ShouldContain("dossiers");

        (await InScope(sp => Task.FromResult(sp.GetRequiredService<IRepublications>().Types))).ShouldBe(["dossiers"]);
    }

    [Fact]
    public async Task Sans_le_droit_d_exploitation_la_republication_est_refusee()
    {
        _utilisateur.Permissions.Remove(Permissions.PlateformeRepublier);
        try
        {
            (await Demander("dossiers")).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
            (await InScope(sp => sp.GetRequiredService<IRepublications>().ListerAsync(Ct))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
            (await InScope(sp => sp.GetRequiredService<IRepublications>().ConsulterAsync(Guid.CreateVersion7(), Ct))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        }
        finally
        {
            _utilisateur.Permissions.Add(Permissions.PlateformeRepublier);
        }
    }
}

/// <summary>Règles de la demande de republication (ADR 0011), sans base.</summary>
public sealed class DemandeRepublicationTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Le_curseur_et_les_compteurs_avancent_jusqu_a_la_fin_de_la_source()
    {
        var demande = DemandeRepublication.Demander("occupations", "exploitant", Maintenant);

        demande.Avancer(200, "c1", Maintenant.AddSeconds(1));
        demande.Statut.ShouldBe(StatutRepublication.EnCours);
        demande.Curseur.ShouldBe("c1");

        demande.Avancer(10, null, Maintenant.AddSeconds(2));
        demande.Statut.ShouldBe(StatutRepublication.Terminee);
        demande.Publies.ShouldBe(210);
        demande.Lots.ShouldBe(2);
        demande.Curseur.ShouldBe("c1");
        demande.TermineeLe.ShouldBe(Maintenant.AddSeconds(2));
        Should.Throw<InvalidOperationException>(() => demande.Avancer(1, null, Maintenant));
    }

    [Fact]
    public void Un_echec_est_borne_et_termine_la_demande()
    {
        var demande = DemandeRepublication.Demander("affilies", "exploitant", Maintenant);

        demande.Echouer(new string('x', 2000), Maintenant);

        demande.Statut.ShouldBe(StatutRepublication.Echouee);
        demande.Erreur!.Length.ShouldBe(DemandeRepublication.LongueurErreurMax);
        demande.Dto().Statut.ShouldBe("Echouee");
    }

    [Fact]
    public void Le_curseur_suit_le_dernier_identifiant_d_un_lot_plein()
    {
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };

        CurseurRepublication.Suivant(ids, 2, id => id).ShouldBe(ids[1].ToString());
        CurseurRepublication.Suivant(ids, 3, id => id).ShouldBeNull();
        CurseurRepublication.Lire(ids[1].ToString()).ShouldBe(ids[1]);
        CurseurRepublication.Lire(null).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => CurseurRepublication.Lire("pas-un-guid"));
    }
}

/// <summary>Droit d'exploitation (ADR 0011) : distinct de tout droit métier.</summary>
public sealed class ExploitationPermissionsTests
{
    private static readonly string[] RolesMetier = typeof(Roles).GetFields()
        .Select(f => (string)f.GetRawConstantValue()!)
        .Where(r => r != Roles.Exploitation)
        .ToArray();

    [Fact]
    public void Seul_le_role_d_exploitation_peut_republier() =>
        RolesMetier.Where(r => RolePermissions.For([r]).Contains(Permissions.PlateformeRepublier)).ShouldBeEmpty();

    [Fact]
    public void Le_role_d_exploitation_n_a_aucun_droit_metier() =>
        RolePermissions.For([Roles.Exploitation]).ShouldBe([Permissions.PlateformeRepublier]);
}
