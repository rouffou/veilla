using System.Globalization;

using Npgsql;

using NpgsqlTypes;

using Sepp.BuildingBlocks.Infrastructure.Security;

namespace Sepp.Pseudonymisation;

/// <summary>Résultat de la pseudonymisation d'un service.</summary>
public sealed record RapportService(
    string Service,
    IReadOnlyDictionary<string, int> LignesParTable,
    IReadOnlyList<string> TablesVidees,
    int ValeursUtilisateurRemplacees,
    IReadOnlyList<string> ColonnesNonCouvertes,
    bool ABlanc);

/// <summary>
/// Applique les règles d'un service à une copie restaurée de sa base, dans une seule transaction (annulée en mode à
/// blanc). Refuse de rejouer sur une base déjà pseudonymisée (table <c>pseudonymisation_execution</c>).
/// </summary>
public static class Moteur
{
    public const string TableMarqueur = "pseudonymisation_execution";
    private const int TailleLot = 500;

    private static readonly string[] ColonnesAudit = ["created_by", "updated_by"];

    private static readonly string[] TablesTechniques = ["outbox_message", "inbox_message", "republication", "__EFMigrationsHistory", TableMarqueur];

    public static async Task<RapportService> ExecuterAsync(
        ReglesService regles,
        string connexion,
        Pseudonymiseur pseudonymiseur,
        FieldEncryptor? source,
        FieldEncryptor? cible,
        BlindIndex? indexCible,
        string environnement,
        bool aBlanc,
        CancellationToken ct = default)
    {
        if (regles.Chiffrement && (source is null || cible is null))
        {
            throw new InvalidOperationException($"Le service « {regles.Nom} » a des champs chiffrés : clés source et cible requises.");
        }

        if (regles.IndexAveugle && indexCible is null)
        {
            throw new InvalidOperationException($"Le service « {regles.Nom} » exige la clé de l'index aveugle cible.");
        }

        await using var connection = new NpgsqlConnection(connexion);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        if (await TableExisteAsync(connection, TableMarqueur, ct))
        {
            throw new InvalidOperationException($"La base du service « {regles.Nom} » a déjà été pseudonymisée ({TableMarqueur}) : restaurer une copie neuve.");
        }

        var colonnes = await ColonnesParTableAsync(connection, ct);
        VerifierRegles(regles, colonnes);

        var lignes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var table in regles.Tables)
        {
            lignes[table.Table] = await TraiterTableAsync(connection, table, colonnes[table.Table], pseudonymiseur, source, cible, indexCible, ct);
        }

        var vidées = new List<string>();
        foreach (var table in regles.TablesAVider.Where(colonnes.ContainsKey))
        {
            await ExecuterAsync(connection, $"DELETE FROM \"{table}\"", ct);
            vidées.Add(table);
        }

        var utilisateurs = await PseudonymiserAuditAsync(connection, colonnes, pseudonymiseur, ct);

        await ExecuterAsync(connection, $"CREATE TABLE \"{TableMarqueur}\" (id serial PRIMARY KEY, execute_le timestamptz NOT NULL DEFAULT now(), environnement text NOT NULL)", ct);
        await using (var marqueur = new NpgsqlCommand($"INSERT INTO \"{TableMarqueur}\" (environnement) VALUES (@e)", connection, transaction))
        {
            marqueur.Parameters.AddWithValue("e", environnement);
            await marqueur.ExecuteNonQueryAsync(ct);
        }

        var couvertes = regles.Tables.SelectMany(t => t.Colonnes.Where(c => c.Technique != Technique.Conserver).Select(c => (t.Table, c.Colonne))).ToHashSet();
        var nonCouvertes = colonnes
            .Where(t => !TablesTechniques.Contains(t.Key, StringComparer.Ordinal))
            .SelectMany(t => t.Value.Where(c => c.Value is "character varying" or "text" or "jsonb" or "json").Select(c => (Table: t.Key, Colonne: c.Key)))
            .Where(c => !couvertes.Contains(c) && c.Colonne is not ("created_by" or "updated_by"))
            .Select(c => $"{c.Table}.{c.Colonne}")
            .Order(StringComparer.Ordinal)
            .ToList();

        if (aBlanc)
        {
            await transaction.RollbackAsync(ct);
        }
        else
        {
            await transaction.CommitAsync(ct);
        }

        return new RapportService(regles.Nom, lignes, vidées, utilisateurs, nonCouvertes, aBlanc);
    }

    private static async Task ExecuterAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var commande = new NpgsqlCommand(sql, connection);
        await commande.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> TableExisteAsync(NpgsqlConnection connection, string table, CancellationToken ct)
    {
        await using var commande = new NpgsqlCommand("SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @t", connection);
        commande.Parameters.AddWithValue("t", table);
        return await commande.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task<Dictionary<string, Dictionary<string, string>>> ColonnesParTableAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var resultat = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        await using var commande = new NpgsqlCommand(
            "SELECT c.table_name, c.column_name, c.data_type FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE' " +
            "WHERE c.table_schema = 'public'", connection);
        await using var lecteur = await commande.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct))
        {
            var table = lecteur.GetString(0);
            if (!resultat.TryGetValue(table, out var cols))
            {
                cols = new Dictionary<string, string>(StringComparer.Ordinal);
                resultat[table] = cols;
            }

            cols[lecteur.GetString(1)] = lecteur.GetString(2);
        }

        return resultat;
    }

    private static void VerifierRegles(ReglesService regles, Dictionary<string, Dictionary<string, string>> colonnes)
    {
        foreach (var table in regles.Tables)
        {
            if (!colonnes.TryGetValue(table.Table, out var cols))
            {
                throw new InvalidOperationException($"Service « {regles.Nom} » : la table « {table.Table} » n'existe pas (schéma inattendu : mauvaise base ou version de règles).");
            }

            foreach (var nom in table.Colonnes.Select(c => c.Colonne).Append(table.ColonneCle).Where(n => !cols.ContainsKey(n)))
            {
                throw new InvalidOperationException($"Service « {regles.Nom} » : la colonne « {table.Table}.{nom} » n'existe pas.");
            }
        }
    }

    private static async Task<int> TraiterTableAsync(
        NpgsqlConnection connection,
        RegleTable table,
        Dictionary<string, string> types,
        Pseudonymiseur pseudonymiseur,
        FieldEncryptor? source,
        FieldEncryptor? cible,
        BlindIndex? indexCible,
        CancellationToken ct)
    {
        var noms = table.Colonnes.Select(c => c.Colonne).Distinct(StringComparer.Ordinal).ToList();
        var selection = string.Join(", ", noms.Select(n => $"\"{n}\""));
        var total = 0;
        object derniere = Guid.Empty;

        while (true)
        {
            var lot = new List<(object Cle, Dictionary<string, object?> Valeurs)>();
            await using (var lecture = new NpgsqlCommand(
                             $"SELECT \"{table.ColonneCle}\", {selection} FROM \"{table.Table}\" WHERE \"{table.ColonneCle}\" > @derniere ORDER BY \"{table.ColonneCle}\" LIMIT {TailleLot}", connection))
            {
                lecture.Parameters.AddWithValue("derniere", derniere);
                await using var lecteur = await lecture.ExecuteReaderAsync(ct);
                while (await lecteur.ReadAsync(ct))
                {
                    var valeurs = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var i = 0; i < noms.Count; i++)
                    {
                        valeurs[noms[i]] = lecteur.IsDBNull(i + 1) ? null : lecteur.GetValue(i + 1);
                    }

                    lot.Add((lecteur.GetValue(0), valeurs));
                }
            }

            if (lot.Count == 0)
            {
                return total;
            }

            foreach (var (cle, valeurs) in lot)
            {
                var contexte = new ContexteLigne(table.Table, cle, valeurs, pseudonymiseur, source, cible, indexCible);
                var modifications = new List<(string Colonne, object? Valeur, Technique Technique)>();
                foreach (var regle in table.Colonnes)
                {
                    var valeur = Techniques.Appliquer(regle, contexte);
                    if (!ReferenceEquals(valeur, Techniques.Ignorer))
                    {
                        modifications.Add((regle.Colonne, valeur, regle.Technique));
                    }
                }

                if (modifications.Count > 0)
                {
                    await MettreAJourAsync(connection, table, cle, modifications, ct);
                }
            }

            total += lot.Count;
            derniere = lot[^1].Cle;
        }
    }

    private static async Task MettreAJourAsync(NpgsqlConnection connection, RegleTable table, object cle, List<(string Colonne, object? Valeur, Technique Technique)> modifications, CancellationToken ct)
    {
        var affectations = string.Join(", ", modifications.Select((m, i) => $"\"{m.Colonne}\" = @p{i.ToString(CultureInfo.InvariantCulture)}"));
        await using var commande = new NpgsqlCommand($"UPDATE \"{table.Table}\" SET {affectations} WHERE \"{table.ColonneCle}\" = @cle", connection);
        for (var i = 0; i < modifications.Count; i++)
        {
            var (_, valeur, technique) = modifications[i];
            var parametre = new NpgsqlParameter("p" + i.ToString(CultureInfo.InvariantCulture), valeur ?? DBNull.Value);
            if (technique == Technique.JsonVide)
            {
                parametre.NpgsqlDbType = NpgsqlDbType.Jsonb;
            }

            commande.Parameters.Add(parametre);
        }

        commande.Parameters.AddWithValue("cle", cle);
        await commande.ExecuteNonQueryAsync(ct);
    }

    /// <summary>created_by et updated_by de toutes les tables : identifiants d'utilisateurs remplacés par des jetons stables.</summary>
    private static async Task<int> PseudonymiserAuditAsync(NpgsqlConnection connection, Dictionary<string, Dictionary<string, string>> colonnes, Pseudonymiseur pseudonymiseur, CancellationToken ct)
    {
        var remplaces = 0;
        foreach (var (table, cols) in colonnes.Where(t => !TablesTechniques.Contains(t.Key, StringComparer.Ordinal)))
        {
            foreach (var colonne in ColonnesAudit.Where(cols.ContainsKey))
            {
                var distincts = new List<string>();
                await using (var lecture = new NpgsqlCommand($"SELECT DISTINCT \"{colonne}\" FROM \"{table}\" WHERE \"{colonne}\" IS NOT NULL", connection))
                await using (var lecteur = await lecture.ExecuteReaderAsync(ct))
                {
                    while (await lecteur.ReadAsync(ct))
                    {
                        distincts.Add(lecteur.GetString(0));
                    }
                }

                foreach (var ancien in distincts)
                {
                    var nouveau = pseudonymiseur.Utilisateur(ancien);
                    if (nouveau == ancien)
                    {
                        continue;
                    }

                    await using var maj = new NpgsqlCommand($"UPDATE \"{table}\" SET \"{colonne}\" = @n WHERE \"{colonne}\" = @o", connection);
                    maj.Parameters.AddWithValue("n", nouveau);
                    maj.Parameters.AddWithValue("o", ancien);
                    remplaces += await maj.ExecuteNonQueryAsync(ct);
                }
            }
        }

        return remplaces;
    }
}
