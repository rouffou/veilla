using Azure.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Câblage Azure (CTR-16) : connexion PostgreSQL par identité managée et garde-fou des adaptateurs réels.</summary>
public sealed class ConfigurationAzureTests
{
    private static IConfiguration Configuration(params (string Cle, string Valeur)[] valeurs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valeurs.ToDictionary(v => v.Cle, v => (string?)v.Valeur))
            .Build();

    [Fact]
    public void Connexion_identite_geree_est_construite_depuis_POSTGRES_sans_mot_de_passe_et_avec_TLS_obligatoire()
    {
        var configuration = Configuration(
            ("Postgres:Host", "psql-sepp-dev-std-abc123.postgres.database.azure.com"),
            ("Postgres:Database", "affilies"),
            ("Postgres:User", "id-sepp-dev-affilies"));

        var connexion = PostgresIdentiteManagee.ConstruireConnexion(configuration);

        connexion.Host.ShouldBe("psql-sepp-dev-std-abc123.postgres.database.azure.com");
        connexion.Database.ShouldBe("affilies");
        connexion.Username.ShouldBe("id-sepp-dev-affilies");
        connexion.SslMode.ShouldBe(SslMode.Require);
        connexion.Password.ShouldBeNull();
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Database")]
    [InlineData("User")]
    public void Connexion_identite_geree_incomplete_echoue_avec_un_message_clair(string manquante)
    {
        var valeurs = new Dictionary<string, string> { ["Host"] = "serveur", ["Database"] = "base", ["User"] = "id-service" };
        valeurs.Remove(manquante);
        var configuration = Configuration([.. valeurs.Select(v => ($"Postgres:{v.Key}", v.Value))]);

        var erreur = Should.Throw<InvalidOperationException>(() => PostgresIdentiteManagee.ConstruireConnexion(configuration));

        erreur.Message.ShouldContain($"POSTGRES__{manquante.ToUpperInvariant()}");
    }

    [Fact]
    public void La_source_de_donnees_est_creee_sans_obtenir_de_jeton_au_demarrage()
    {
        var configuration = Configuration(("Postgres:Host", "serveur"), ("Postgres:Database", "base"), ("Postgres:User", "id-service"));

        using var source = PostgresIdentiteManagee.ConstruireSourceDeDonnees(configuration, new IdentifiantsInterdits());

        source.ConnectionString.ShouldContain("SSL Mode=Require");
        source.ConnectionString.ShouldNotContain("Password");
    }

    [Fact]
    public void ConnectionStrings_reste_prioritaire_pour_le_developpement_local()
    {
        var configuration = Configuration(
            ("ConnectionStrings:Test", "Host=localhost;Database=test;Username=u;Password=p"),
            ("Postgres:Host", "serveur"));
        var services = new ServiceCollection();

        services.AddSeppPersistence<ContexteDeTest>(configuration, "Test");

        services.ShouldNotContain(d => d.ServiceType == typeof(NpgsqlDataSource));
    }

    [Fact]
    public void Sans_aucune_connexion_le_demarrage_echoue_en_citant_les_deux_modes()
    {
        var erreur = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddSeppPersistence<ContexteDeTest>(Configuration(), "Test"));

        erreur.Message.ShouldContain("ConnectionStrings:Test");
        erreur.Message.ShouldContain("POSTGRES__HOST");
    }

    [Fact]
    public void Un_adaptateur_reel_non_implemente_est_refuse_hors_production()
    {
        var configuration = Configuration(("Sepp:Environnement", "recette"));

        var erreur = Should.Throw<InvalidOperationException>(
            () => GardeAdaptateurs.RefuserReelNonImplementeHorsProduction(configuration, "Integrations:Adaptateurs:Bce", "Reel", reelImplemente: false));

        erreur.Message.ShouldContain("Integrations:Adaptateurs:Bce");
        erreur.Message.ShouldContain("Simulateur");
    }

    [Fact]
    public void Un_adaptateur_reel_non_implemente_est_refuse_sans_environnement_defini()
    {
        Should.Throw<InvalidOperationException>(
            () => GardeAdaptateurs.RefuserReelNonImplementeHorsProduction(Configuration(), "Documents:Adaptateurs:Signature", "Reel", reelImplemente: false));
    }

    [Theory]
    [InlineData("Simulateur", false, "recette")]
    [InlineData("Reel", true, "recette")]
    [InlineData("Reel", false, "prod")]
    [InlineData("Reel", false, "PROD")]
    public void Le_garde_fou_laisse_passer_les_simulateurs_les_adaptateurs_reels_implementes_et_la_production(string choix, bool implemente, string environnement)
    {
        var configuration = Configuration(("Sepp:Environnement", environnement));

        Should.NotThrow(() => GardeAdaptateurs.RefuserReelNonImplementeHorsProduction(configuration, "X:Adaptateurs:Y", choix, implemente));
    }

    private sealed class ContexteDeTest(Microsoft.EntityFrameworkCore.DbContextOptions<ContexteDeTest> options)
        : SeppDbContext(options, null!, TimeProvider.System)
    {
        protected override void ConfigureModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
        }
    }

    private sealed class IdentifiantsInterdits : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Aucun jeton ne doit être demandé à la construction.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Aucun jeton ne doit être demandé à la construction.");
    }
}
