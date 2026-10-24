using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Sepp.BuildingBlocks.Infrastructure.Persistence;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Contexte minimal doté d'une migration, pour éprouver le mode « migrer puis quitter » (ADR 0010).</summary>
public sealed class MigrationTestDbContext(DbContextOptions<MigrationTestDbContext> options) : DbContext(options)
{
    public DbSet<ElementMigre> Elements => Set<ElementMigre>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ElementMigre>(b =>
        {
            b.ToTable("element_migre");
            b.HasKey(e => e.Id).HasName("pk_element_migre");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Libelle).HasColumnName("libelle");
        });
}

public sealed class ElementMigre
{
    public int Id { get; set; }

    public string Libelle { get; set; } = string.Empty;
}

[DbContext(typeof(MigrationTestDbContext))]
[Migration("20261024000000_Initiale")]
public sealed class MigrationInitialeTest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "element_migre",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                libelle = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_element_migre", x => x.id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "element_migre");
}

/// <summary>Journal capturé en mémoire, pour vérifier qu'aucun secret n'y figure.</summary>
public sealed class JournalCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Capture(this);

    public void Dispose()
    {
    }

    private sealed class Capture(JournalCapture parent) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            parent.Messages.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}

/// <summary>Hôte de test : la même méthode que les <c>Program.cs</c> des services, sur un contexte de test.</summary>
internal static class HoteMigration
{
    public static IHost Construire(string? connexion, IDictionary<string, string?> configuration, JournalCapture journal)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(journal);
        if (connexion is not null)
        {
            // Pas d'instantané de modèle dans ce contexte de test : l'avertissement de modèle modifié est ignoré.
            builder.Services.AddDbContext<MigrationTestDbContext>(o => o
                .UseNpgsql(connexion)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        }

        return builder.Build();
    }
}

/// <summary>Mode « migrer puis quitter » commun aux services (ARC-50, CTR-16, ADR 0010) : choix du mode et garde-fous.</summary>
public sealed class MigrationSchemaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IConfiguration Configuration(params (string Cle, string Valeur)[] valeurs) =>
        new ConfigurationBuilder().AddInMemoryCollection(valeurs.ToDictionary(v => v.Cle, v => (string?)v.Valeur)).Build();

    [Fact]
    public void Sans_configuration_le_service_sert_sans_migrer() =>
        MigrationSchema.Mode(Configuration()).ShouldBe(ModeMigration.Servir);

    [Fact]
    public void MigrateOnStartup_migre_puis_sert() =>
        MigrationSchema.Mode(Configuration(("Database:MigrateOnStartup", "true"))).ShouldBe(ModeMigration.MigrerPuisServir);

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void MigrateAndExit_l_emporte_sur_MigrateOnStartup(string migrerAuDemarrage) =>
        MigrationSchema.Mode(Configuration(("Database:MigrateAndExit", "true"), ("Database:MigrateOnStartup", migrerAuDemarrage)))
            .ShouldBe(ModeMigration.MigrerPuisQuitter);

    [Fact]
    public void La_variable_d_environnement_du_job_active_le_mode_migrer_puis_quitter()
    {
        const string variable = "SEPP_TEST_MIGRATION_Database__MigrateAndExit";
        Environment.SetEnvironmentVariable(variable, "true");
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables("SEPP_TEST_MIGRATION_").Build();
            MigrationSchema.Mode(configuration).ShouldBe(ModeMigration.MigrerPuisQuitter);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("id-sepp-dev-affilies")]
    [InlineData("id-sepp-preprod-surveillance-medicale")]
    [InlineData("execution_test")]
    public void Un_nom_d_identite_managee_est_un_role_d_execution_valide(string role) =>
        MigrationSchema.EstRoleValide(role).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("x'; DROP ROLE postgres; --")]
    [InlineData("a\"b")]
    [InlineData("role avec espace")]
    [InlineData("-commence-par-un-tiret")]
    public void Un_nom_de_role_hors_alphabet_est_refuse(string role)
    {
        MigrationSchema.EstRoleValide(role).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => MigrationSchema.InstructionDroitsExecution(role));
    }

    [Fact]
    public void Un_nom_de_role_de_plus_de_63_caracteres_est_refuse() =>
        MigrationSchema.EstRoleValide(new string('a', 64)).ShouldBeFalse();

    [Fact]
    public void L_instruction_n_accorde_que_des_droits_DML_au_role_d_execution()
    {
        var sql = MigrationSchema.InstructionDroitsExecution("id-sepp-dev-affilies");

        sql.ShouldContain("'id-sepp-dev-affilies'");
        sql.ShouldContain("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA");
        sql.ShouldContain("GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA");
        sql.ShouldContain("ALTER DEFAULT PRIVILEGES IN SCHEMA");
        foreach (var interdit in new[] { "TRUNCATE", "ALL PRIVILEGES", "OWNER TO", "CREATE ON", "REFERENCES", "TRIGGER ON" })
        {
            sql.ShouldNotContain(interdit, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Sans_mode_de_migration_le_service_sert_sans_ouvrir_la_base()
    {
        // Aucun contexte enregistré : toute tentative de migration lèverait une exception.
        using var hote = HoteMigration.Construire(connexion: null, new Dictionary<string, string?>(), new JournalCapture());

        (await hote.MigrerSchemaAsync<MigrationTestDbContext>(cancellationToken: Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Un_echec_de_connexion_retourne_un_code_non_nul_sans_journaliser_le_mot_de_passe()
    {
        const string motDePasse = "mot-de-passe-de-test-a-ne-pas-journaliser";
        var journal = new JournalCapture();
        var connexion = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "absente",
            Username = "migration",
            Password = motDePasse,
            Timeout = 3,
        }.ConnectionString;
        using var hote = HoteMigration.Construire(connexion, new Dictionary<string, string?> { ["Database:MigrateAndExit"] = "true" }, journal);

        var code = await hote.MigrerSchemaAsync<MigrationTestDbContext>(cancellationToken: Ct);

        code.ShouldBe(MigrationSchema.CodeEchec);
        journal.Messages.ShouldContain(m => m.Contains("Échec de la migration du schéma MigrationTestDbContext", StringComparison.Ordinal));
        journal.Messages.ShouldAllBe(m => !m.Contains(motDePasse, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_role_d_execution_invalide_fait_echouer_le_job_avant_toute_migration()
    {
        var journal = new JournalCapture();
        var initialisations = 0;
        using var hote = HoteMigration.Construire(
            "Host=127.0.0.1;Port=1;Database=absente;Username=migration;Timeout=3",
            new Dictionary<string, string?> { ["Database:MigrateAndExit"] = "true", ["Database:RuntimeRole"] = "x'; DROP ROLE postgres; --" },
            journal);

        var code = await hote.MigrerSchemaAsync<MigrationTestDbContext>((_, _) => { initialisations++; return Task.CompletedTask; }, Ct);

        code.ShouldBe(MigrationSchema.CodeEchec);
        initialisations.ShouldBe(0);
        journal.Messages.ShouldContain(m => m.Contains("Database:RuntimeRole invalide", StringComparison.Ordinal));
        journal.Messages.ShouldAllBe(m => !m.Contains("DROP ROLE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task En_local_un_echec_de_migration_interrompt_le_demarrage()
    {
        using var hote = HoteMigration.Construire(
            "Host=127.0.0.1;Port=1;Database=absente;Username=migration;Timeout=3",
            new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "true" },
            new JournalCapture());

        await Should.ThrowAsync<Exception>(() => hote.MigrerSchemaAsync<MigrationTestDbContext>(cancellationToken: Ct));
    }
}

/// <summary>Serveur PostgreSQL partagé par les tests de migration ; chaque test crée sa propre base.</summary>
public sealed class ServeurPostgresMigration : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnexionAdministrateur(string? baseDeDonnees = null) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = baseDeDonnees ?? "postgres" }.ConnectionString;

    public string Connexion(string baseDeDonnees, string role, string motDePasse) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = baseDeDonnees, Username = role, Password = motDePasse }.ConnectionString;

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}

/// <summary>
/// Mode « migrer puis quitter » sur un vrai PostgreSQL, avec l'amorçage des rôles documenté dans <c>infra/README.md</c> :
/// l'identité de migration porte le DDL, l'identité d'exécution ne reçoit que le DML (ADR 0010).
/// </summary>
public sealed class MigrationSchemaPostgresTests(ServeurPostgresMigration serveur) : IClassFixture<ServeurPostgresMigration>
{
    // Valeurs de test d'un conteneur jetable, sans rapport avec un environnement réel.
    private const string MotDePasseMigration = "mdp-migration-test";
    private const string MotDePasseExecution = "mdp-execution-test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrer_puis_quitter_applique_les_migrations_et_l_initialisation_puis_retourne_0()
    {
        var baseDeDonnees = await CreerBaseAsync();
        var initialisations = 0;
        using var hote = HoteMigration.Construire(
            serveur.ConnexionAdministrateur(baseDeDonnees),
            new Dictionary<string, string?> { ["Database:MigrateAndExit"] = "true" },
            new JournalCapture());

        var code = await hote.MigrerSchemaAsync<MigrationTestDbContext>(
            async (services, annulation) =>
            {
                initialisations++;
                var db = services.GetRequiredService<MigrationTestDbContext>();
                db.Elements.Add(new ElementMigre { Libelle = "donnée de départ" });
                await db.SaveChangesAsync(annulation);
            },
            Ct);

        code.ShouldBe(MigrationSchema.CodeSucces);
        initialisations.ShouldBe(1);
        (await ScalaireAsync(serveur.ConnexionAdministrateur(baseDeDonnees), "SELECT count(*) FROM \"__EFMigrationsHistory\"")).ShouldBe(1L);
        (await ScalaireAsync(serveur.ConnexionAdministrateur(baseDeDonnees), "SELECT count(*) FROM element_migre")).ShouldBe(1L);

        // Une seconde exécution du job ne rejoue rien.
        (await hote.MigrerSchemaAsync<MigrationTestDbContext>(cancellationToken: Ct)).ShouldBe(MigrationSchema.CodeSucces);
        (await ScalaireAsync(serveur.ConnexionAdministrateur(baseDeDonnees), "SELECT count(*) FROM \"__EFMigrationsHistory\"")).ShouldBe(1L);
    }

    [Fact]
    public async Task L_identite_d_execution_recoit_le_DML_mais_aucun_droit_DDL()
    {
        var baseDeDonnees = await CreerBaseAsync();
        var suffixe = baseDeDonnees["service_".Length..];
        var migration = $"migration_{suffixe}";
        var execution = $"execution_{suffixe}";
        await AmorcerRolesAsync(baseDeDonnees, migration, execution);

        using var hote = HoteMigration.Construire(
            serveur.Connexion(baseDeDonnees, migration, MotDePasseMigration),
            new Dictionary<string, string?> { ["Database:MigrateAndExit"] = "true", ["Database:RuntimeRole"] = execution },
            new JournalCapture());

        (await hote.MigrerSchemaAsync<MigrationTestDbContext>(cancellationToken: Ct)).ShouldBe(MigrationSchema.CodeSucces);

        // DML : lecture, ajout (colonne d'identité), modification, suppression.
        var connexionExecution = serveur.Connexion(baseDeDonnees, execution, MotDePasseExecution);
        await ExecuterAsync(connexionExecution, "INSERT INTO element_migre (libelle) VALUES ('a')");
        await ExecuterAsync(connexionExecution, "UPDATE element_migre SET libelle = 'b'");
        (await ScalaireAsync(connexionExecution, "SELECT count(*) FROM element_migre")).ShouldBe(1L);
        await ExecuterAsync(connexionExecution, "DELETE FROM element_migre");

        // Aucun DDL ni TRUNCATE.
        foreach (var interdit in new[]
                 {
                     "CREATE TABLE intrus (id int)", "DROP TABLE element_migre", "ALTER TABLE element_migre ADD COLUMN intrus int",
                     "TRUNCATE element_migre", "CREATE SCHEMA intrus",
                 })
        {
            var erreur = await Should.ThrowAsync<PostgresException>(() => ExecuterAsync(connexionExecution, interdit));
            erreur.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Droits par défaut : une table créée par une migration ultérieure est aussitôt accessible en DML.
        await ExecuterAsync(serveur.Connexion(baseDeDonnees, migration, MotDePasseMigration), "CREATE TABLE ulterieure (id int)");
        await ExecuterAsync(connexionExecution, "INSERT INTO ulterieure VALUES (1)");
        (await ScalaireAsync(connexionExecution, "SELECT count(*) FROM ulterieure")).ShouldBe(1L);
    }

    [Fact]
    public async Task L_identite_d_execution_ne_peut_pas_migrer_elle_meme()
    {
        var baseDeDonnees = await CreerBaseAsync();
        var suffixe = baseDeDonnees["service_".Length..];
        var execution = $"execution_{suffixe}";
        await AmorcerRolesAsync(baseDeDonnees, $"migration_{suffixe}", execution);

        using var hote = HoteMigration.Construire(
            serveur.Connexion(baseDeDonnees, execution, MotDePasseExecution),
            new Dictionary<string, string?> { ["Database:MigrateAndExit"] = "true" },
            new JournalCapture());

        (await hote.MigrerSchemaAsync<MigrationTestDbContext>(cancellationToken: Ct)).ShouldBe(MigrationSchema.CodeEchec);
    }

    private async Task<string> CreerBaseAsync()
    {
        var nom = $"service_{Guid.NewGuid():N}"[..20];
        await ExecuterAsync(serveur.ConnexionAdministrateur(), $"CREATE DATABASE {nom}");
        return nom;
    }

    /// <summary>Amorçage décrit dans infra/README.md (« Rôles PostgreSQL »), aux noms des rôles près.</summary>
    private async Task AmorcerRolesAsync(string baseDeDonnees, string migration, string execution)
    {
        await ExecuterAsync(serveur.ConnexionAdministrateur(), $"""
            CREATE ROLE {migration} LOGIN PASSWORD '{MotDePasseMigration}';
            CREATE ROLE {execution} LOGIN PASSWORD '{MotDePasseExecution}';
            REVOKE CONNECT ON DATABASE {baseDeDonnees} FROM PUBLIC;
            GRANT CONNECT, TEMPORARY, CREATE ON DATABASE {baseDeDonnees} TO {migration};
            GRANT CONNECT ON DATABASE {baseDeDonnees} TO {execution};
            """);
        await ExecuterAsync(serveur.ConnexionAdministrateur(baseDeDonnees), $"""
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE, CREATE ON SCHEMA public TO {migration};
            GRANT USAGE ON SCHEMA public TO {execution};
            """);
    }

    private static async Task ExecuterAsync(string connexion, string sql)
    {
        await using var source = NpgsqlDataSource.Create(connexion);
        await using var commande = source.CreateCommand(sql);
        await commande.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalaireAsync(string connexion, string sql)
    {
        await using var source = NpgsqlDataSource.Create(connexion);
        await using var commande = source.CreateCommand(sql);
        return await commande.ExecuteScalarAsync(Ct);
    }
}
