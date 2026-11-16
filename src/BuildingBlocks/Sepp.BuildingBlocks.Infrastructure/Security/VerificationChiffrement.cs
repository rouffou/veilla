using System.Data;
using System.Data.Common;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Comptes d'une colonne texte ou JSON. Pour une colonne du modèle : valeurs non nulles, valeurs chiffrées avec la DEK
/// courante, autres valeurs (ancienne DEK, v1, valeur illisible) et valeurs v1. Pour une autre colonne : valeurs qui ont la
/// forme d'une valeur chiffrée (<c>v1:&lt;clé&gt;:</c> ou <c>v2:&lt;clé&gt;:</c>) en tête d'une chaîne JSON, et, pour une
/// colonne texte, en tête du texte ou d'une chaîne JSON stockée en texte (motif <c>(^|")v[12]:</c>, #502).
/// </summary>
/// <remarks>
/// Types non couverts : seules les colonnes <c>text</c>, <c>character varying</c>, <c>character</c>, <c>json</c> et
/// <c>jsonb</c> sont lues. Un tableau (<c>text[]</c>), une colonne <c>citext</c> ou un type de domaine échappent à la
/// vérification (voir <c>infra/README.md</c>).
/// </remarks>
public sealed record ColonneVerifiee(string Colonne, bool DuModele, long Chiffrees, long Courantes, long HorsDekCourante, long V1);

/// <summary>Résultat de la vérification : colonnes du modèle et colonnes hors modèle qui contiennent des valeurs chiffrées.</summary>
/// <remarks>
/// <see cref="ColonnesAbsentes"/> : colonnes chiffrées du modèle introuvables dans la base, ou invisibles pour l'identité
/// (droits) : leurs valeurs n'ont pas pu être comptées.
/// </remarks>
public sealed record RapportVerificationChiffrement(string PrefixeCourant, IReadOnlyList<ColonneVerifiee> Colonnes, IReadOnlyList<string> ColonnesAbsentes)
{
    /// <summary>Valeurs qui n'utilisent pas la DEK courante, dans toute la base.</summary>
    public long HorsDekCourante => Colonnes.Sum(c => c.HorsDekCourante);

    public long V1 => Colonnes.Sum(c => c.V1);

    /// <summary>Colonnes qui contiennent des valeurs chiffrées sans être déclarées chiffrées dans le modèle : jamais rechiffrées.</summary>
    public IReadOnlyList<ColonneVerifiee> ColonnesHorsModele => [.. Colonnes.Where(c => !c.DuModele)];

    /// <summary>
    /// Plus aucune valeur n'utilise une autre clé que la DEK courante, aucune valeur chiffrée n'est hors du modèle, et
    /// toutes les colonnes chiffrées du modèle ont été comptées.
    /// </summary>
    public bool Complet => HorsDekCourante == 0 && ColonnesHorsModele.Count == 0 && ColonnesAbsentes.Count == 0;
}

/// <summary>
/// Vérification fondée sur les données (#482) : avant de retirer une clé, toutes les colonnes texte et JSON de la base,
/// quel que soit leur nom, sont lues dans un seul instantané (transaction <c>REPEATABLE READ</c> en lecture seule). Une
/// valeur chiffrée dans une colonne que le modèle ne déclare pas chiffrée est signalée, comme toute valeur qui n'utilise
/// pas la DEK courante. Aucune écriture, aucune valeur ni clé journalisée.
/// </summary>
/// <remarks>
/// Le catalogue PostgreSQL ne montre que les colonnes sur lesquelles l'identité a un droit : la vérification s'exécute avec
/// l'identité d'exécution du service, propriétaire des données. Chaque table est lue une fois (un balayage séquentiel).
/// </remarks>
public static partial class VerificationChiffrement
{
    private const string PrefixeV1 = "v1:";

    public static async Task<RapportVerificationChiffrement> VerifierAsync(
        DbConnection connexion,
        string prefixeCourant,
        IReadOnlyList<ColonneChiffree> colonnesDuModele,
        TimeSpan delaiVerrou,
        ILogger journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connexion);
        ArgumentException.ThrowIfNullOrEmpty(prefixeCourant);
        ArgumentNullException.ThrowIfNull(colonnesDuModele);
        ArgumentNullException.ThrowIfNull(journal);
        var dek = IdentifiantDek(prefixeCourant);

        await using var transaction = await connexion.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var lectureSeule = Sql.Commande(connexion, transaction, "SET TRANSACTION READ ONLY"))
        {
            await lectureSeule.ExecuteNonQueryAsync(cancellationToken);
        }

        await Sql.DelaiVerrouAsync(connexion, transaction, delaiVerrou, cancellationToken);
        var (schemaCourant, catalogue) = await CatalogueAsync(connexion, transaction, cancellationToken);

        var modele = colonnesDuModele.ToDictionary(c => (c.Schema ?? schemaCourant, c.Table, c.Colonne), c => c);
        var absentes = modele.Keys.Where(k => !catalogue.Any(c => (c.Schema, c.Table, c.Colonne) == k))
            .Select(k => modele[k].Nom).Order(StringComparer.Ordinal).ToList();

        var resultats = new List<ColonneVerifiee>();
        foreach (var table in catalogue.GroupBy(c => (c.Schema, c.Table)))
        {
            var colonnes = table.ToList();
            var comptes = colonnes.Select(c =>
            {
                var col = Sql.Identifiant(c.Colonne);
                if (modele.ContainsKey((c.Schema, c.Table, c.Colonne)))
                {
                    return $"count({col}), count(*) FILTER (WHERE starts_with({col}, @prefixe)), " +
                           $"count(*) FILTER (WHERE NOT starts_with({col}, @prefixe)), count(*) FILTER (WHERE starts_with({col}, @v1))";
                }

                var forme = c.Json ? "j" : "t";
                return $"count(*) FILTER (WHERE {col}::text ~ @chiffre_{forme}), count(*) FILTER (WHERE {col}::text ~ @courant_{forme}), " +
                       $"count(*) FILTER (WHERE {col}::text ~ @hors_{forme}), count(*) FILTER (WHERE {col}::text ~ @v1_{forme})";
            });
            var sql = $"SELECT {string.Join(", ", comptes)} FROM {Sql.Table(table.Key.Schema, table.Key.Table)}";
            await using var commande = Sql.Commande(connexion, transaction, sql);
            Sql.Parametre(commande, "prefixe", prefixeCourant);
            Sql.Parametre(commande, "v1", PrefixeV1);
            // Texte : en tête du texte ou d'une chaîne JSON stockée en texte (#502) ; JSON : en tête d'une chaîne JSON.
            foreach (var (forme, debut) in new[] { ("t", "(^|\")"), ("j", "\"") })
            {
                Sql.Parametre(commande, $"chiffre_{forme}", $"{debut}v[12]:[^:\"]+:");
                Sql.Parametre(commande, $"courant_{forme}", $"{debut}v2:{dek}:");
                Sql.Parametre(commande, $"hors_{forme}", $"{debut}(v1:[^:\"]+:|v2:(?!{dek}:)[^:\"]+:)");
                Sql.Parametre(commande, $"v1_{forme}", $"{debut}v1:[^:\"]+:");
            }

            await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken);
            await lecteur.ReadAsync(cancellationToken);
            for (var i = 0; i < colonnes.Count; i++)
            {
                var c = colonnes[i];
                var duModele = modele.TryGetValue((c.Schema, c.Table, c.Colonne), out var declaree);
                var compte = new ColonneVerifiee(
                    declaree?.Nom ?? (c.Schema == schemaCourant ? $"{c.Table}.{c.Colonne}" : $"{c.Schema}.{c.Table}.{c.Colonne}"),
                    duModele, lecteur.GetInt64(4 * i), lecteur.GetInt64((4 * i) + 1), lecteur.GetInt64((4 * i) + 2), lecteur.GetInt64((4 * i) + 3));
                if (duModele || compte.Chiffrees > 0)
                {
                    resultats.Add(compte);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);

        var rapport = new RapportVerificationChiffrement(prefixeCourant, [.. resultats.OrderBy(c => c.Colonne, StringComparer.Ordinal)], absentes);
        foreach (var colonne in absentes)
        {
            LogAbsente(journal, colonne);
        }

        foreach (var colonne in rapport.Colonnes.Where(c => !c.DuModele))
        {
            LogHorsModele(journal, colonne.Colonne, colonne.Chiffrees, colonne.HorsDekCourante);
        }

        foreach (var colonne in rapport.Colonnes.Where(c => c.DuModele && c.HorsDekCourante > 0))
        {
            LogRestantes(journal, colonne.Colonne, colonne.HorsDekCourante, colonne.V1);
        }

        LogVerification(journal, prefixeCourant, catalogue.Count, rapport.HorsDekCourante, rapport.V1, rapport.ColonnesHorsModele.Count, rapport.Complet);
        return rapport;
    }

    /// <summary>Identifiant de la DEK courante, échappé pour une expression rationnelle PostgreSQL.</summary>
    private static string IdentifiantDek(string prefixeCourant)
    {
        if (!prefixeCourant.StartsWith("v2:", StringComparison.Ordinal) || !prefixeCourant.EndsWith(':') || prefixeCourant.Length < 5)
        {
            throw new ArgumentException("Préfixe de DEK courante attendu au format v2:<id>:.", nameof(prefixeCourant));
        }

        var echappe = new StringBuilder();
        foreach (var caractere in prefixeCourant[3..^1])
        {
            echappe.Append(char.IsAsciiLetterOrDigit(caractere) ? string.Empty : "\\").Append(caractere);
        }

        return echappe.ToString();
    }

    private static async Task<(string SchemaCourant, List<(string Schema, string Table, string Colonne, bool Json)> Colonnes)> CatalogueAsync(
        DbConnection connexion, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var commande = Sql.Commande(connexion, transaction,
            "SELECT current_schema(), c.table_schema, c.table_name, c.column_name, c.data_type IN ('json', 'jsonb') " +
            "FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE' " +
            "WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema') AND c.table_schema NOT LIKE 'pg\\_%' " +
            "AND c.data_type IN ('text', 'character varying', 'character', 'json', 'jsonb') " +
            "ORDER BY c.table_schema, c.table_name, c.ordinal_position");
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken);
        string? schemaCourant = null;
        var colonnes = new List<(string, string, string, bool)>();
        while (await lecteur.ReadAsync(cancellationToken))
        {
            schemaCourant ??= lecteur.IsDBNull(0) ? "public" : lecteur.GetString(0);
            colonnes.Add((lecteur.GetString(1), lecteur.GetString(2), lecteur.GetString(3), lecteur.GetBoolean(4)));
        }

        return (schemaCourant ?? "public", colonnes);
    }

    [LoggerMessage(EventId = 5106, Level = LogLevel.Information,
        Message = "Vérification du chiffrement ({PrefixeCourant}) : {Colonnes} colonne(s) texte ou JSON lue(s), {HorsDekCourante} valeur(s) hors DEK courante dont {V1} au format v1, {HorsModele} colonne(s) chiffrée(s) hors du modèle ; complet : {Complet}.")]
    private static partial void LogVerification(ILogger journal, string prefixeCourant, int colonnes, long horsDekCourante, long v1, int horsModele, bool complet);

    [LoggerMessage(EventId = 5107, Level = LogLevel.Error,
        Message = "Colonne {Colonne} : {Chiffrees} valeur(s) chiffrée(s) dont {HorsDekCourante} hors DEK courante, alors que le modèle ne la déclare pas chiffrée : jamais rechiffrée. Ne retirer aucune clé.")]
    private static partial void LogHorsModele(ILogger journal, string colonne, long chiffrees, long horsDekCourante);

    [LoggerMessage(EventId = 5109, Level = LogLevel.Error, Message = "Colonne chiffrée {Colonne} du modèle absente de la base ou inaccessible : ses valeurs n'ont pas été comptées.")]
    private static partial void LogAbsente(ILogger journal, string colonne);

    [LoggerMessage(EventId = 5108, Level = LogLevel.Warning, Message = "Colonne {Colonne} : {HorsDekCourante} valeur(s) hors DEK courante, dont {V1} au format v1.")]
    private static partial void LogRestantes(ILogger journal, string colonne, long horsDekCourante, long v1);
}
