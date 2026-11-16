using System.Data;
using System.Data.Common;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Index aveugle d'une table : colonne d'empreinte (HMAC, <see cref="BlindIndex"/>) calculée sur le clair d'une colonne
/// chiffrée par <see cref="FieldEncryptor"/>, par exemple <c>personne.niss_hash</c> sur <c>personne.niss_chiffre</c>.
/// </summary>
public sealed record ColonneIndexAveugle(string? Schema, string Table, string ColonneSource, string ColonneEmpreinte, string ColonneCle)
{
    /// <summary>Nom qualifié de la colonne d'empreinte, pour le rapport et le journal (aucune donnée).</summary>
    public string Nom => Schema is null ? $"{Table}.{ColonneEmpreinte}" : $"{Schema}.{Table}.{ColonneEmpreinte}";

    /// <summary>
    /// Colonnes de l'index d'après le modèle EF Core : la source doit être chiffrée (convertisseur marqué
    /// <see cref="IConvertisseurChiffrement"/>), l'empreinte dans la même table, et la clé primaire à une seule colonne.
    /// </summary>
    public static ColonneIndexAveugle Depuis(IModel model, Type entite, string proprieteSource, string proprieteEmpreinte)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(entite);
        var type = model.FindEntityType(entite) ?? throw new InvalidOperationException($"Entité « {entite.Name} » absente du modèle.");
        var source = Colonne(type, proprieteSource);
        var empreinte = Colonne(type, proprieteEmpreinte);
        var convertisseur = type.FindProperty(proprieteSource)!.GetValueConverter() ?? type.FindProperty(proprieteSource)!.FindTypeMapping()?.Converter;
        if (convertisseur is not IConvertisseurChiffrement)
        {
            throw new InvalidOperationException($"Index aveugle : « {entite.Name}.{proprieteSource} » n'est pas une colonne chiffrée.");
        }

        if (source.Table != empreinte.Table || source.Table.PrimaryKey is not { Columns: [var cle] })
        {
            throw new InvalidOperationException($"Index aveugle de « {entite.Name} » : source et empreinte dans une même table à clé primaire d'une seule colonne.");
        }

        return new ColonneIndexAveugle(source.Table.Schema, source.Table.Name, source.Name, empreinte.Name, cle.Name);
    }

    private static IColumn Colonne(IEntityType type, string propriete) =>
        (type.FindProperty(propriete) ?? throw new InvalidOperationException($"Propriété « {type.DisplayName()}.{propriete} » absente du modèle."))
        .GetTableColumnMappings().Select(m => m.Column).SingleOrDefault()
        ?? throw new InvalidOperationException($"Propriété « {type.DisplayName()}.{propriete} » : une seule colonne de table attendue.");
}

/// <summary>Index aveugle à recalculer ou à vérifier, avec sa clé courante et, pendant une rotation, sa clé précédente.</summary>
public sealed record IndexAveugleChiffre(ColonneIndexAveugle Colonne, BlindIndex Index);

/// <summary>
/// Vérification d'un index aveugle, dans un même instantané : lignes dont la source est renseignée, empreintes calculées
/// avec la clé courante, avec la clé précédente, avec une autre clé (ou altérées), et sources illisibles.
/// </summary>
public sealed record RapportVerificationIndexAveugle(string Colonne, long Lignes, long Courantes, long ClePrecedente, long Autres, long Illisibles)
{
    /// <summary>Toutes les empreintes sont calculées avec la clé courante : la clé précédente peut être retirée.</summary>
    public bool Complet => Courantes == Lignes;
}

/// <summary>Résultat d'un recalcul : compteurs, erreurs (sans donnée ni clé) et vérification finale.</summary>
public sealed record RapportRecalculIndexAveugle(
    string Colonne,
    long Examinees,
    long Recalculees,
    long AJour,
    long Concurrentes,
    long Collisions,
    long Erreurs,
    IReadOnlyList<ErreurRechiffrement> ErreursDetaillees,
    RapportVerificationIndexAveugle Verification)
{
    /// <summary>Aucune erreur (ni collision), et la vérification finale ne trouve plus que des empreintes de la clé courante.</summary>
    public bool Complet => Erreurs == 0 && Verification.Complet;
}

/// <summary>
/// Recalcul d'un index aveugle avec la clé courante (#473) : chaque source est déchiffrée, puis son empreinte recalculée ;
/// une empreinte déjà à jour n'est pas réécrite. Les lots passent par <see cref="MoteurLots"/> (curseur sur la clé
/// primaire, transactions courtes, <c>lock_timeout</c>, rejouable, erreurs isolées ligne par ligne), comme le rechiffrement.
/// </summary>
/// <remarks>
/// <para>
/// <b>Collision</b> : si l'empreinte recalculée existe déjà sur une autre ligne (index unique), l'écriture est refusée par
/// la base, comptée en erreur et signalée (identifiant technique de la ligne) ; aucune ligne n'est écrasée ni supprimée.
/// Une collision révèle un doublon : deux lignes pour le même clair, à fusionner par le métier.
/// </para>
/// <para>
/// <b>Contrôle par recalcul</b>, et non par une colonne de version : la vérification déchiffre chaque source et compare
/// l'empreinte stockée à celle de la clé courante. Elle prouve la propriété exigée avant le retrait de l'ancienne clé, sans
/// migration de schéma ni dépendance du domaine à la version de la clé, et détecte aussi une empreinte altérée ou
/// calculée avec une troisième clé. Coût : un déchiffrement AES-GCM en mémoire par ligne (aucun appel au coffre).
/// </para>
/// <para>Aucune valeur, en clair comme chiffrée, aucune empreinte ni aucune clé n'est journalisée.</para>
/// </remarks>
public static partial class RecalculIndexAveugle
{
    public static async Task<RapportRecalculIndexAveugle> ExecuterAsync(
        DbConnection connexion,
        FieldEncryptor chiffreur,
        IndexAveugleChiffre index,
        OptionsRechiffrement options,
        ILogger journal,
        TimeProvider? horloge = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connexion);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(journal);
        MoteurLots.Valider(options);

        var colonne = index.Colonne;
        LogDebut(journal, colonne.Nom, index.Index.AvecClePrecedente);
        var erreurs = new List<ErreurRechiffrement>();
        var cible = new CibleLots(colonne.Nom, colonne.Schema, colonne.Table, colonne.ColonneCle, colonne.ColonneSource, colonne.ColonneEmpreinte);
        var bilan = await MoteurLots.ExecuterAsync(
            connexion, cible, source => index.Index.Compute(chiffreur.Decrypt(source)), options,
            detail =>
            {
                if (erreurs.Count < options.ErreursDetailleesMax)
                {
                    erreurs.Add(detail);
                }

                LogErreur(journal, detail.Colonne, detail.Cle, detail.TypeErreur, detail.Message);
            },
            horloge ?? TimeProvider.System, cancellationToken);

        var verification = await VerifierAsync(connexion, chiffreur, index, options.DelaiVerrou, journal, cancellationToken);
        var rapport = new RapportRecalculIndexAveugle(
            colonne.Nom, bilan.Examinees, bilan.Ecrites, bilan.AJour, bilan.Concurrentes, bilan.Collisions, bilan.Erreurs, erreurs, verification);
        LogFin(journal, colonne.Nom, rapport.Examinees, rapport.Recalculees, rapport.AJour, rapport.Concurrentes, rapport.Collisions, rapport.Erreurs, rapport.Complet);
        return rapport;
    }

    /// <summary>
    /// Vérifie, sans rien écrire et dans un seul instantané (transaction <c>REPEATABLE READ</c> en lecture seule), que
    /// chaque empreinte est celle de la clé courante. Garde-fou du mode « vérifier seulement » avant le retrait de
    /// l'ancienne clé : chaque ligne est lue une fois (balayage séquentiel).
    /// </summary>
    public static async Task<RapportVerificationIndexAveugle> VerifierAsync(
        DbConnection connexion,
        FieldEncryptor chiffreur,
        IndexAveugleChiffre index,
        TimeSpan delaiVerrou,
        ILogger journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connexion);
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(journal);
        var colonne = index.Colonne;

        await using var transaction = await connexion.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var lectureSeule = Sql.Commande(connexion, transaction, "SET TRANSACTION READ ONLY"))
        {
            await lectureSeule.ExecuteNonQueryAsync(cancellationToken);
        }

        await Sql.DelaiVerrouAsync(connexion, transaction, delaiVerrou, cancellationToken);
        long lignes = 0, courantes = 0, precedente = 0, autres = 0, illisibles = 0;
        var source = Sql.Identifiant(colonne.ColonneSource);
        await using (var lecture = Sql.Commande(connexion, transaction,
                         $"SELECT {source}, {Sql.Identifiant(colonne.ColonneEmpreinte)} FROM {Sql.Table(colonne.Schema, colonne.Table)} WHERE {source} IS NOT NULL"))
        await using (var lecteur = await lecture.ExecuteReaderAsync(cancellationToken))
        {
            while (await lecteur.ReadAsync(cancellationToken))
            {
                lignes++;
                var empreinte = lecteur.IsDBNull(1) ? null : lecteur.GetString(1);
                string clair;
                try
                {
                    clair = chiffreur.Decrypt(lecteur.GetString(0));
                }
                catch (Exception erreur) when (erreur is CryptographicException or FormatException)
                {
                    illisibles++;
                    continue;
                }

                if (string.Equals(empreinte, index.Index.Compute(clair), StringComparison.Ordinal))
                {
                    courantes++;
                }
                else if (empreinte is not null && string.Equals(empreinte, index.Index.ComputePrecedente(clair), StringComparison.Ordinal))
                {
                    precedente++;
                }
                else
                {
                    autres++;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var rapport = new RapportVerificationIndexAveugle(colonne.Nom, lignes, courantes, precedente, autres, illisibles);
        LogVerification(journal, colonne.Nom, lignes, courantes, precedente, autres, illisibles, rapport.Complet);
        return rapport;
    }

    [LoggerMessage(EventId = 5110, Level = LogLevel.Information,
        Message = "Recalcul de l'index aveugle {Colonne} avec la clé courante ; clé précédente configurée : {AvecClePrecedente}.")]
    private static partial void LogDebut(ILogger journal, string colonne, bool avecClePrecedente);

    [LoggerMessage(EventId = 5111, Level = LogLevel.Warning, Message = "Empreinte non recalculée : {Colonne}, ligne {Cle} : {TypeErreur} : {Message}")]
    private static partial void LogErreur(ILogger journal, string colonne, string cle, string typeErreur, string message);

    [LoggerMessage(EventId = 5112, Level = LogLevel.Information,
        Message = "Recalcul de l'index aveugle {Colonne} terminé : {Examinees} ligne(s) examinée(s), {Recalculees} recalculée(s), {AJour} déjà à jour, {Concurrentes} modifiée(s) entre-temps, {Collisions} collision(s), {Erreurs} erreur(s) ; complet : {Complet}.")]
    private static partial void LogFin(ILogger journal, string colonne, long examinees, long recalculees, long aJour, long concurrentes, long collisions, long erreurs, bool complet);

    [LoggerMessage(EventId = 5113, Level = LogLevel.Information,
        Message = "Vérification de l'index aveugle {Colonne} : {Lignes} ligne(s), {Courantes} empreinte(s) de la clé courante, {ClePrecedente} de la clé précédente, {Autres} autre(s), {Illisibles} source(s) illisible(s) ; complet : {Complet}.")]
    private static partial void LogVerification(ILogger journal, string colonne, long lignes, long courantes, long clePrecedente, long autres, long illisibles, bool complet);
}

/// <summary>
/// Journal des clés d'un index aveugle (#514, P3-1) : au démarrage, la valeur de contrôle (<see cref="BlindIndex.Kcv"/>) de
/// chaque clé configurée, jamais la clé. La procédure d'incident compare ces valeurs entre le service, le recalcul et la
/// vérification, et à celle de la clé compromise.
/// </summary>
public static partial class JournalIndexAveugle
{
    /// <summary>Catégorie du journal.</summary>
    public const string Categorie = "Sepp.IndexAveugle";

    /// <summary>Journalise la KCV de la clé courante et, pendant une rotation, celle de la clé précédente.</summary>
    /// <param name="journal">Journal.</param>
    /// <param name="index">Nom de l'index (colonne d'empreinte), sans donnée.</param>
    /// <param name="cles">Clés de l'index.</param>
    public static void JournaliserCles(ILogger journal, string index, BlindIndex cles)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(cles);
        LogCleCourante(journal, index, cles.Kcv);
        if (cles.KcvPrecedente is { } precedente)
        {
            LogClePrecedente(journal, index, precedente);
        }
    }

    [LoggerMessage(EventId = 5114, Level = LogLevel.Information, Message = "Index aveugle {Index} : clé courante, KCV {Kcv}.")]
    private static partial void LogCleCourante(ILogger journal, string index, string kcv);

    [LoggerMessage(EventId = 5115, Level = LogLevel.Information, Message = "Index aveugle {Index} : clé précédente acceptée en lecture, KCV {Kcv}.")]
    private static partial void LogClePrecedente(ILogger journal, string index, string kcv);
}
