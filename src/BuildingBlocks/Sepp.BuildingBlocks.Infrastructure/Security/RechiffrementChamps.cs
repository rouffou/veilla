using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>Colonne chiffrée par <see cref="FieldEncryptor"/> et clé primaire (une seule colonne) de sa table.</summary>
public sealed record ColonneChiffree(string? Schema, string Table, string Colonne, string ColonneCle)
{
    /// <summary>Nom qualifié, pour le rapport et le journal (aucune donnée).</summary>
    public string Nom => Schema is null ? $"{Table}.{Colonne}" : $"{Schema}.{Table}.{Colonne}";
}

/// <summary>
/// Colonnes chiffrées d'un modèle EF Core : celles dont le convertisseur porte le marqueur
/// <see cref="IConvertisseurChiffrement"/> (ARC-45, #482). Le marqueur et le suffixe <c>_chiffre</c>/<c>_chiffree</c> vont
/// ensemble, dans les deux sens : une colonne marquée sans suffixe, une colonne suffixée sans marqueur, ou un convertisseur
/// non marqué qui utilise <see cref="FieldEncryptor"/> font échouer le traitement, au lieu de laisser une colonne chiffrée de
/// côté (elle deviendrait illisible au retrait de l'ancienne clé). Une table à clé composite est aussi refusée.
/// </summary>
public static class ColonnesChiffrees
{
    private static readonly string[] Suffixes = ["_chiffre", "_chiffree"];

    public static IReadOnlyList<ColonneChiffree> Depuis(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        VerifierConvertisseurs(model);
        var colonnes = new List<ColonneChiffree>();
        foreach (var table in model.GetRelationalModel().Tables)
        {
            var chiffrees = new List<IColumn>();
            foreach (var colonne in table.Columns)
            {
                var marquees = colonne.PropertyMappings.Count(m => Convertisseur(m.Property, m.TypeMapping) is IConvertisseurChiffrement);
                var suffixee = Suffixes.Any(s => colonne.Name.EndsWith(s, StringComparison.Ordinal));
                if (marquees == 0 && !suffixee)
                {
                    continue;
                }

                if (!suffixee)
                {
                    throw new InvalidOperationException(
                        $"Colonne « {table.Name}.{colonne.Name} » : convertisseur de chiffrement sur une colonne sans suffixe _chiffre ou _chiffree.");
                }

                if (marquees != colonne.PropertyMappings.Count)
                {
                    throw new InvalidOperationException(
                        $"Colonne « {table.Name}.{colonne.Name} » : nom de colonne chiffrée sans convertisseur de chiffrement ({nameof(IConvertisseurChiffrement)}).");
                }

                chiffrees.Add(colonne);
            }

            if (chiffrees.Count == 0)
            {
                continue;
            }

            if (table.PrimaryKey is not { Columns: [var cle] })
            {
                throw new InvalidOperationException($"Table « {table.Name} » : le rechiffrement exige une clé primaire à une seule colonne.");
            }

            colonnes.AddRange(chiffrees.Select(c => new ColonneChiffree(table.Schema, table.Name, c.Name, cle.Name)));
        }

        return [.. colonnes.OrderBy(c => c.Nom, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Toute propriété dont le convertisseur utilise <see cref="FieldEncryptor"/> doit porter le marqueur, et toute propriété
    /// marquée doit être stockée dans une table : sinon ses valeurs échapperaient au rechiffrement.
    /// </summary>
    private static void VerifierConvertisseurs(IModel model)
    {
        foreach (var type in model.GetEntityTypes())
        {
            foreach (var propriete in type.GetFlattenedProperties())
            {
                var convertisseur = Convertisseur(propriete, null);
                if (convertisseur is null)
                {
                    continue;
                }

                var nom = $"{type.DisplayName()}.{propriete.Name}";
                if (convertisseur is IConvertisseurChiffrement)
                {
                    if (!propriete.GetTableColumnMappings().Any())
                    {
                        throw new InvalidOperationException($"Propriété « {nom} » : convertisseur de chiffrement hors d'une table, non rechiffrable.");
                    }
                }
                else if (UtiliseLeChiffreur(convertisseur))
                {
                    throw new InvalidOperationException(
                        $"Propriété « {nom} » : convertisseur qui utilise {nameof(FieldEncryptor)} sans le marqueur {nameof(IConvertisseurChiffrement)} " +
                        $"(utiliser {nameof(EncryptedStringConverter)} ou ConvertisseurChiffre<T>).");
                }
            }
        }
    }

    private static ValueConverter? Convertisseur(IReadOnlyProperty propriete, CoreTypeMapping? correspondance) =>
        propriete.GetValueConverter() ?? correspondance?.Converter ?? propriete.FindTypeMapping()?.Converter;

    private static bool UtiliseLeChiffreur(ValueConverter convertisseur)
    {
        var recherche = new RechercheChiffreur();
        recherche.Visit(convertisseur.ConvertToProviderExpression);
        recherche.Visit(convertisseur.ConvertFromProviderExpression);
        return recherche.Trouve;
    }

    /// <summary>Repère, dans l'expression d'un convertisseur, une référence au chiffreur (variable capturée ou appel).</summary>
    private sealed class RechercheChiffreur : ExpressionVisitor
    {
        public bool Trouve { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node is not null && (node.Type == typeof(FieldEncryptor)
                                     || (node is MethodCallExpression appel && appel.Method.DeclaringType == typeof(FieldEncryptor))))
            {
                Trouve = true;
                return node;
            }

            return Trouve ? node : base.Visit(node);
        }
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

    /// <summary>
    /// Attente maximale d'un verrou (<c>lock_timeout</c>, par transaction) : une ligne verrouillée par une transaction du
    /// service est laissée en erreur et reprise à la prochaine exécution, au lieu de bloquer le traitement.
    /// </summary>
    public TimeSpan DelaiVerrou { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Valeur non rechiffrée : identifiant technique de la ligne et type d'erreur, jamais la valeur ni la clé.</summary>
public sealed record ErreurRechiffrement(string Colonne, string Cle, string TypeErreur, string Message);

/// <summary>
/// Résultat pour une colonne. Les compteurs « courantes », « restantes » et « v1 » viennent de la vérification finale
/// (<see cref="VerificationChiffrement"/>), relevée dans un même instantané pour toute la base.
/// </summary>
public sealed record RapportColonneRechiffrement(
    string Colonne,
    long Examinees,
    long Rechiffrees,
    long Concurrentes,
    long Erreurs,
    long Courantes,
    long Restantes,
    long RestantesV1);

/// <summary>Rapport d'une exécution : compteurs par colonne, erreurs (sans donnée ni clé) et vérification finale.</summary>
public sealed record RapportRechiffrement(
    string PrefixeCourant,
    IReadOnlyList<RapportColonneRechiffrement> Colonnes,
    IReadOnlyList<ErreurRechiffrement> ErreursDetaillees,
    RapportVerificationChiffrement Verification)
{
    public long Rechiffrees => Colonnes.Sum(c => c.Rechiffrees);

    public long Erreurs => Colonnes.Sum(c => c.Erreurs);

    /// <summary>Valeurs qui n'utilisent pas la DEK courante, dans toute la base (vérification finale).</summary>
    public long Restantes => Verification.HorsDekCourante;

    public long RestantesV1 => Verification.V1;

    /// <summary>
    /// Aucune erreur, et la vérification finale ne trouve plus aucune valeur chiffrée avec une autre clé que la DEK
    /// courante, ni aucune valeur chiffrée hors des colonnes du modèle : les anciennes clés peuvent être retirées.
    /// </summary>
    public bool Complet => Erreurs == 0 && Verification.Complet;
}

/// <summary>
/// Rechiffrement des champs vers la DEK courante (ARC-44, ADR 0012, #396, #482) : valeurs <c>v1</c> (anciennes clés AES)
/// et <c>v2</c> d'une ancienne DEK. Par colonne, curseur sur la clé primaire et lots dans des transactions courtes ; seules
/// les valeurs qui ne commencent pas par le préfixe de la DEK courante sont lues (rejouable, idempotent, reprise après
/// interruption en relançant simplement le traitement). La mise à jour est conditionnée à la valeur lue : une valeur
/// modifiée entre-temps par le service (donc déjà chiffrée avec la DEK courante) n'est pas écrasée.
/// </summary>
/// <remarks>
/// <para>
/// Écriture directe en SQL, hors du suivi EF Core : le rechiffrement n'est pas une modification métier (ni colonnes
/// d'audit, ni événement). Aucune valeur, en clair comme chiffrée, ni aucune clé n'est journalisée.
/// </para>
/// <para>
/// Une erreur de base de données (troncature, contrainte, verrou non obtenu dans <see cref="OptionsRechiffrement.DelaiVerrou"/>)
/// annule le lot, qui est alors repris ligne par ligne, chacune dans sa transaction : seule la ligne en cause reste en
/// erreur. Une erreur sur une colonne entière est comptée, et le traitement passe à la colonne suivante.
/// </para>
/// <para>
/// Les lots passent par <see cref="MoteurLots"/>, partagé avec le recalcul de l'index aveugle (<see cref="RecalculIndexAveugle"/>, #473).
/// </para>
/// </remarks>
public static partial class RechiffrementChamps
{
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
        ArgumentNullException.ThrowIfNull(journal);
        MoteurLots.Valider(options);
        horloge ??= TimeProvider.System;

        var prefixe = chiffreur.PrefixeCourant;
        LogDebut(journal, colonnes.Count, prefixe);
        var passes = new List<(ColonneChiffree Colonne, long Examinees, long Rechiffrees, long Concurrentes, long Erreurs)>();
        var erreurs = new List<ErreurRechiffrement>();
        foreach (var colonne in colonnes)
        {
            passes.Add(await TraiterColonneAsync(connexion, chiffreur, colonne, prefixe, options, journal, erreurs, horloge, cancellationToken));
        }

        // Recomptage global, dans un même instantané : le code de sortie ne repose pas sur des comptes relevés colonne par
        // colonne à des instants différents, et couvre aussi les colonnes texte hors du modèle (#482).
        var verification = await VerificationChiffrement.VerifierAsync(connexion, prefixe, colonnes, options.DelaiVerrou, journal, cancellationToken);
        var rapports = new List<RapportColonneRechiffrement>();
        foreach (var (colonne, examinees, rechiffrees, concurrentes, enErreur) in passes)
        {
            var compte = verification.Colonnes.FirstOrDefault(c => c.DuModele && c.Colonne == colonne.Nom);
            var rapport = new RapportColonneRechiffrement(colonne.Nom, examinees, rechiffrees, concurrentes, enErreur,
                compte?.Courantes ?? 0, compte?.HorsDekCourante ?? 0, compte?.V1 ?? 0);
            rapports.Add(rapport);
            LogColonne(journal, colonne.Nom, rapport.Examinees, rapport.Rechiffrees, rapport.Concurrentes, rapport.Erreurs, rapport.Courantes, rapport.Restantes, rapport.RestantesV1);
        }

        var resultat = new RapportRechiffrement(prefixe, rapports, erreurs, verification);
        LogFin(journal, resultat.Rechiffrees, resultat.Erreurs, resultat.Restantes, resultat.RestantesV1, resultat.Complet);
        return resultat;
    }

    private static async Task<(ColonneChiffree, long, long, long, long)> TraiterColonneAsync(
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
        // Seules les valeurs qui ne commencent pas par le préfixe de la DEK courante sont lues ; la colonne est à la fois
        // la source et la destination.
        var cible = new CibleLots(colonne.Nom, colonne.Schema, colonne.Table, colonne.ColonneCle, colonne.Colonne, colonne.Colonne, prefixe);
        var bilan = await MoteurLots.ExecuterAsync(
            connexion, cible, chiffreur.Rechiffrer, options, detail => Signaler(erreurs, options, journal, detail), horloge, cancellationToken);
        return (colonne, bilan.Examinees, bilan.Ecrites, bilan.Concurrentes, bilan.Erreurs);
    }

    private static void Signaler(List<ErreurRechiffrement> erreurs, OptionsRechiffrement options, ILogger journal, ErreurRechiffrement detail)
    {
        if (erreurs.Count < options.ErreursDetailleesMax)
        {
            erreurs.Add(detail);
        }

        LogErreurValeur(journal, detail.Colonne, detail.Cle, detail.TypeErreur, detail.Message);
    }

    /// <summary>Code SQLSTATE seulement : le message et le détail d'une erreur PostgreSQL peuvent citer une valeur.</summary>
    internal static string MessageBase(DbException erreur) =>
        erreur.SqlState is { Length: > 0 } etat ? $"Erreur de base de données (SQLSTATE {etat})." : "Erreur de base de données.";

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

/// <summary>Construction des commandes SQL du rechiffrement et de sa vérification (identifiants cités, valeurs en paramètres).</summary>
internal static class Sql
{
    public static DbCommand Commande(DbConnection connexion, DbTransaction? transaction, string sql)
    {
        var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
#pragma warning disable CA2100 // Identifiants issus du modèle EF Core ou du catalogue, cités ; toutes les valeurs passent en paramètres.
        commande.CommandText = sql;
#pragma warning restore CA2100
        return commande;
    }

    public static void Parametre(DbCommand commande, string nom, object valeur)
    {
        var parametre = commande.CreateParameter();
        parametre.ParameterName = nom;
        parametre.Value = valeur;
        commande.Parameters.Add(parametre);
    }

    /// <summary><c>SET LOCAL lock_timeout</c> : borne l'attente d'un verrou pour la seule transaction en cours.</summary>
    public static async Task DelaiVerrouAsync(DbConnection connexion, DbTransaction transaction, TimeSpan delai, CancellationToken cancellationToken)
    {
        var millisecondes = Math.Max(1L, (long)Math.Ceiling(delai.TotalMilliseconds));
        await using var commande = Commande(connexion, transaction, $"SET LOCAL lock_timeout = {millisecondes.ToString(CultureInfo.InvariantCulture)}");
        await commande.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string Table(string? schema, string table) =>
        schema is null ? Identifiant(table) : $"{Identifiant(schema)}.{Identifiant(table)}";

    public static string Identifiant(string nom) => $"\"{nom.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
