using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.Configuration;

using Npgsql;

namespace Sepp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Connexion PostgreSQL par identité managée (CTR-16, ARC-02) : en Azure, le serveur n'accepte aucun mot de passe
/// (<c>password_auth_enabled = false</c>) ; le mot de passe est un jeton Microsoft Entra obtenu par
/// <see cref="DefaultAzureCredential"/> et renouvelé périodiquement. Terraform injecte <c>POSTGRES__HOST</c>,
/// <c>POSTGRES__DATABASE</c> et <c>POSTGRES__USER</c> (nom de l'identité managée du service, rôle PostgreSQL du même nom).
/// </summary>
public static class PostgresIdentiteManagee
{
    /// <summary>Portée du jeton d'accès à Azure Database for PostgreSQL.</summary>
    public const string Portee = "https://ossrdbms-aad.database.windows.net/.default";

    /// <summary>Un jeton Entra vit au moins une heure : renouvelé bien avant son expiration.</summary>
    public static readonly TimeSpan IntervalleRenouvellement = TimeSpan.FromMinutes(25);

    /// <summary>Nouvelle tentative rapide après un échec d'obtention du jeton.</summary>
    public static readonly TimeSpan IntervalleReessai = TimeSpan.FromSeconds(10);

    /// <summary>Indique si la configuration décrit une connexion par identité managée (<c>Postgres:Host</c> renseigné).</summary>
    public static bool EstConfiguree(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Postgres:Host"]);

    /// <summary>Paramètres de connexion sans mot de passe, TLS obligatoire (<c>SslMode=Require</c>), à partir de <c>Postgres:*</c>.</summary>
    public static NpgsqlConnectionStringBuilder ConstruireConnexion(IConfiguration configuration) =>
        new()
        {
            Host = Exiger(configuration, "Host"),
            Port = int.TryParse(configuration["Postgres:Port"], out var port) ? port : 5432,
            Database = Exiger(configuration, "Database"),
            Username = Exiger(configuration, "User"),
            SslMode = SslMode.Require,
        };

    /// <summary>Source de données dont le mot de passe est un jeton Entra renouvelé avant son expiration.</summary>
    public static NpgsqlDataSource ConstruireSourceDeDonnees(IConfiguration configuration, TokenCredential? identifiants = null)
    {
        var connexion = ConstruireConnexion(configuration);
        var credential = identifiants ?? new DefaultAzureCredential();
        var constructeur = new NpgsqlDataSourceBuilder(connexion.ConnectionString);
        constructeur.UsePeriodicPasswordProvider(
            async (_, annulation) => (await credential.GetTokenAsync(new TokenRequestContext([Portee]), annulation)).Token,
            IntervalleRenouvellement,
            IntervalleReessai);
        return constructeur.Build();
    }

    private static string Exiger(IConfiguration configuration, string cle) =>
        !string.IsNullOrWhiteSpace(configuration[$"Postgres:{cle}"])
            ? configuration[$"Postgres:{cle}"]!
            : throw new InvalidOperationException(
                $"Connexion PostgreSQL par identité managée incomplète : Postgres:{cle} (variable POSTGRES__{cle.ToUpperInvariant()}) est absent.");
}
