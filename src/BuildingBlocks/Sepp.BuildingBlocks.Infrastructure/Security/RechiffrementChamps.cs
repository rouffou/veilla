using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>Colonne chiffrée par <see cref="FieldEncryptor"/> et clé primaire (une seule colonne) de sa table.</summary>
public sealed record ColonneChiffree(string? Schema, string Table, string Colonne, string ColonneCle)
{
    /// <summary>Nom qualifié, pour le rapport et le journal (aucune donnée).</summary>
    public string Nom => Schema is null ? $"{Table}.{Colonne}" : $"{Schema}.{Table}.{Colonne}";
}

/// <summary>
/// Colonnes chiffrées d'un modèle EF Core : par convention du dépôt, toute colonne chiffrée se termine par
/// <c>_chiffre</c> ou <c>_chiffree</c> et porte un convertisseur (ARC-45). Un nom chiffré sans convertisseur, ou une table
/// à clé composite, est refusé : le rechiffrement ne s'appliquerait pas correctement.
/// </summary>
public static class ColonnesChiffrees
{
    private static readonly string[] Suffixes = ["_chiffre", "_chiffree"];

    public static IReadOnlyList<ColonneChiffree> Depuis(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var colonnes = new List<ColonneChiffree>();
        foreach (var table in model.GetRelationalModel().Tables)
        {
            var chiffrees = table.Columns.Where(c => Suffixes.Any(s => c.Name.EndsWith(s, StringComparison.Ordinal))).ToList();
            if (chiffrees.Count == 0)
            {
                continue;
            }

            if (table.PrimaryKey is not { Columns: [var cle] })
            {
                throw new InvalidOperationException($"Table « {table.Name} » : le rechiffrement exige une clé primaire à une seule colonne.");
            }

            foreach (var colonne in chiffrees)
            {
                if (colonne.PropertyMappings.Any(m => m.TypeMapping.Converter is null))
                {
                    throw new InvalidOperationException($"Colonne « {table.Name}.{colonne.Name} » : nom de colonne chiffrée sans convertisseur de chiffrement.");
                }

                colonnes.Add(new ColonneChiffree(table.Schema, table.Name, colonne.Name, cle.Name));
            }
        }

        return [.. colonnes.OrderBy(c => c.Nom, StringComparer.Ordinal)];
    }
}

/// <summary>Paramètres du rechiffrement (section <c>Rechiffrement</c>).</summary>
public sealed record OptionsRechiffrement
{
    /// <summary>Valeurs lues et rechiffrées par transaction : des transactions courtes, qui ne bloquent pas le service.</summary>
    public int TailleLot { get; init; } = 200;

    /// <summary>Pause entre deux lots, pour laisser la base au service.</summary>
    public TimeSpan PauseEntreLots { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Nombre maximal d'erreurs détaillées dans le rapport (les compteurs restent exacts).</summary>
    public int ErreursDetailleesMax { get; init; } = 100;
}

/// <summary>Valeur non rechiffrée : identifiant technique de la ligne et type d'erreur, jamais la valeur ni la clé.</summary>
public sealed record ErreurRechiffrement(string Colonne, string Cle, string TypeErreur, string Message);

/// <summary>Résultat pour une colonne. Les compteurs « courantes », « restantes » et « v1 » sont relevés en fin de passe.</summary>
public sealed record RapportColonneRechiffrement(
    string Colonne,
    long Examinees,
    long Rechiffrees,
    long Concurrentes,
    long Erreurs,
    long Courantes,
    long Restantes,
    long RestantesV1);

/// <summary>Rapport d'une exécution : compteurs par colonne et erreurs (sans donnée ni clé).</summary>
public sealed record RapportRechiffrement(
    string PrefixeCourant,
    IReadOnlyList<RapportColonneRechiffrement> Colonnes,
    IReadOnlyList<ErreurRechiffrement> ErreursDetaillees)
{
    public long Rechiffrees => Colonnes.Sum(c => c.Rechiffrees);

    public long Erreurs => Colonnes.Sum(c => c.Erreurs);

    public long Restantes => Colonnes.Sum(c => c.Restantes);

    public long RestantesV1 => Colonnes.Sum(c => c.RestantesV1);

    /// <summary>Toutes les valeurs sont chiffrées avec la DEK courante : les anciennes clés peuvent être retirées.</summary>
    public bool Complet => Erreurs == 0 && Restantes == 0;
}

/// <summary>
/// Rechiffrement des champs vers la DEK courante (ARC-44, ADR 0012, #396) : valeurs <c>v1</c> (anciennes clés AES) et
/// <c>v2</c> d'une ancienne DEK. Par colonne, curseur sur la clé primaire et lots dans des transactions courtes ; seules
/// les valeurs qui ne commencent pas par le préfixe de la DEK courante sont lues (rejouable, idempotent, reprise après
/// interruption en relançant simplement le traitement). La mise à jour est conditionnée à la valeur lue : une valeur
/// modifiée entre-temps par le service (donc déjà chiffrée avec la DEK courante) n'est pas écrasée.
/// </summary>
/// <remarks>
/// Écriture directe en SQL, hors du suivi EF Core : le rechiffrement n'est pas une modification métier (ni colonnes
/// d'audit, ni événement). Aucune valeur, en clair comme chiffrée, ni aucune clé n'est journalisée.
/// </remarks>
public static partial class RechiffrementChamps
{
    private const string PrefixeV1 = "v1:";

    public static async Task<RapportRechiffrement> ExecuterAsync(
        DbConnection connexion,
        FieldEncryptor chiffreur,
        IReadOnlyList<ColonneChiffree> colonnes,
        OptionsRechiffrement options,
        ILogger journal,
        TimeProvider? horloge = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connexion);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(colonnes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TailleLot, 1, nameof(options.TailleLot));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TailleLot, 10_000, nameof(options.TailleLot));
        horloge ??= TimeProvider.System;

        var prefixe = chiffreur.PrefixeCourant;
        LogDebut(journal, colonnes.Count, prefixe);
        var rapports = new List<RapportColonneRechiffrement>();
        var erreurs = new List<ErreurRechiffrement>();
        foreach (var colonne in colonnes)
        {
            var rapport = await TraiterColonneAsync(connexion, chiffreur, colonne, prefixe, options, journal, erreurs, horloge, cancellationToken);
            rapports.Add(rapport);
            LogColonne(journal, colonne.Nom, rapport.Examinees, rapport.Rechiffrees, rapport.Concurrentes, rapport.Erreurs, rapport.Courantes, rapport.Restantes, rapport.RestantesV1);
        }

        var resultat = new RapportRechiffrement(prefixe, rapports, erreurs);
        LogFin(journal, resultat.Rechiffrees, resultat.Erreurs, resultat.Restantes, resultat.RestantesV1, resultat.Complet);
        return resultat;
    }

    private static async Task<RapportColonneRechiffrement> TraiterColonneAsync(
        DbConnection connexion,
        FieldEncryptor chiffreur,
        ColonneChiffree colonne,
        string prefixe,
        OptionsRechiffrement options,
        ILogger journal,
        List<ErreurRechiffrement> erreurs,
        TimeProvider horloge,
        CancellationToken cancellationToken)
    {
        var table = Table(colonne);
        var col = Identifiant(colonne.Colonne);
        var cle = Identifiant(colonne.ColonneCle);
        long examinees = 0, rechiffrees = 0, concurrentes = 0, enErreur = 0;
        object? curseur = null;

        while (true)
        {
            await using var transaction = await connexion.BeginTransactionAsync(cancellationToken);
            var lot = new List<(object Cle, string Valeur)>();
            await using (var lecture = Commande(connexion, transaction,
                             $"SELECT {cle}, {col} FROM {table} WHERE {col} IS NOT NULL AND NOT starts_with({col}, @prefixe)" +
                             (curseur is null ? string.Empty : $" AND {cle} > @curseur") +
                             $" ORDER BY {cle} LIMIT {options.TailleLot.ToString(CultureInfo.InvariantCulture)}"))
            {
                Parametre(lecture, "prefixe", prefixe);
                if (curseur is not null)
                {
                    Parametre(lecture, "curseur", curseur);
                }

                await using var lecteur = await lecture.ExecuteReaderAsync(cancellationToken);
                while (await lecteur.ReadAsync(cancellationToken))
                {
                    lot.Add((lecteur.GetValue(0), lecteur.GetString(1)));
                }
            }

            foreach (var (identifiant, ancienne) in lot)
            {
                string nouvelle;
                try
                {
                    nouvelle = chiffreur.Rechiffrer(ancienne);
                }
                catch (Exception erreur) when (erreur is CryptographicException or FormatException)
                {
                    // Clé non configurée, valeur altérée ou illisible : la valeur reste en place, le traitement continue.
                    enErreur++;
                    var detail = new ErreurRechiffrement(colonne.Nom, Convert.ToString(identifiant, CultureInfo.InvariantCulture) ?? string.Empty,
                        erreur.GetType().Name, erreur is CryptographicException ? erreur.Message : "Valeur chiffrée illisible (base64).");
                    if (erreurs.Count < options.ErreursDetailleesMax)
                    {
                        erreurs.Add(detail);
                    }

                    LogErreurValeur(journal, detail.Colonne, detail.Cle, detail.TypeErreur, detail.Message);
                    continue;
                }

                await using var miseAJour = Commande(connexion, transaction, $"UPDATE {table} SET {col} = @nouvelle WHERE {cle} = @cle AND {col} = @ancienne");
                Parametre(miseAJour, "nouvelle", nouvelle);
                Parametre(miseAJour, "cle", identifiant);
                Parametre(miseAJour, "ancienne", ancienne);
                if (await miseAJour.ExecuteNonQueryAsync(cancellationToken) == 1)
                {
                    rechiffrees++;
                }
                else
                {
                    // Modifiée ou supprimée par le service depuis la lecture : sa nouvelle valeur est déjà à jour.
                    concurrentes++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            examinees += lot.Count;
            if (lot.Count < options.TailleLot)
            {
                break;
            }

            curseur = lot[^1].Cle;
            if (options.PauseEntreLots > TimeSpan.Zero)
            {
                await Task.Delay(options.PauseEntreLots, horloge, cancellationToken);
            }
        }

        var (courantes, restantes, restantesV1) = await CompterAsync(connexion, table, col, prefixe, cancellationToken);
        return new RapportColonneRechiffrement(colonne.Nom, examinees, rechiffrees, concurrentes, enErreur, courantes, restantes, restantesV1);
    }

    /// <summary>Contrôle final : valeurs à jour, valeurs restantes (toute autre clé) et valeurs encore au format v1.</summary>
    private static async Task<(long Courantes, long Restantes, long V1)> CompterAsync(DbConnection connexion, string table, string col, string prefixe, CancellationToken cancellationToken)
    {
        await using var commande = Commande(connexion, null,
            $"SELECT count(*) FILTER (WHERE starts_with({col}, @prefixe)), count(*) FILTER (WHERE NOT starts_with({col}, @prefixe)), " +
            $"count(*) FILTER (WHERE starts_with({col}, @v1)) FROM {table} WHERE {col} IS NOT NULL");
        Parametre(commande, "prefixe", prefixe);
        Parametre(commande, "v1", PrefixeV1);
        await using var lecteur = await commande.ExecuteReaderAsync(cancellationToken);
        await lecteur.ReadAsync(cancellationToken);
        return (lecteur.GetInt64(0), lecteur.GetInt64(1), lecteur.GetInt64(2));
    }

    private static DbCommand Commande(DbConnection connexion, DbTransaction? transaction, string sql)
    {
        var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
#pragma warning disable CA2100 // Identifiants issus du modèle EF Core, cités ; toutes les valeurs passent en paramètres.
        commande.CommandText = sql;
#pragma warning restore CA2100
        return commande;
    }

    private static void Parametre(DbCommand commande, string nom, object valeur)
    {
        var parametre = commande.CreateParameter();
        parametre.ParameterName = nom;
        parametre.Value = valeur;
        commande.Parameters.Add(parametre);
    }

    private static string Table(ColonneChiffree colonne) =>
        colonne.Schema is null ? Identifiant(colonne.Table) : $"{Identifiant(colonne.Schema)}.{Identifiant(colonne.Table)}";

    private static string Identifiant(string nom) => $"\"{nom.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information, Message = "Rechiffrement des champs : {Nombre} colonne(s) chiffrée(s), clé de données courante {PrefixeCourant}.")]
    private static partial void LogDebut(ILogger journal, int nombre, string prefixeCourant);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information,
        Message = "Rechiffrement de {Colonne} : {Examinees} examinée(s), {Rechiffrees} rechiffrée(s), {Concurrentes} modifiée(s) entre-temps, {Erreurs} erreur(s) ; {Courantes} à jour, {Restantes} restante(s) dont {RestantesV1} au format v1.")]
    private static partial void LogColonne(ILogger journal, string colonne, long examinees, long rechiffrees, long concurrentes, long erreurs, long courantes, long restantes, long restantesV1);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning, Message = "Valeur non rechiffrée : {Colonne}, ligne {Cle} : {TypeErreur} : {Message}")]
    private static partial void LogErreurValeur(ILogger journal, string colonne, string cle, string typeErreur, string message);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Information,
        Message = "Rechiffrement terminé : {Rechiffrees} valeur(s) rechiffrée(s), {Erreurs} erreur(s), {Restantes} valeur(s) restante(s) dont {RestantesV1} au format v1 ; complet : {Complet}.")]
    private static partial void LogFin(ILogger journal, long rechiffrees, long erreurs, long restantes, long restantesV1, bool complet);
}
