using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Colonne traitée par <see cref="MoteurLots"/> : chaque valeur non nulle de <paramref name="Source"/> est transformée,
/// et le résultat est écrit dans <paramref name="Destination"/> (la même colonne pour un rechiffrement, une autre pour le
/// recalcul d'un index aveugle).
/// </summary>
/// <param name="Nom">Nom qualifié pour le rapport et le journal (aucune donnée).</param>
/// <param name="Schema">Schéma de la table, ou <c>null</c> pour le schéma courant.</param>
/// <param name="Table">Table.</param>
/// <param name="ColonneCle">Clé primaire (une seule colonne) : curseur des lots.</param>
/// <param name="Source">Colonne lue et passée à la transformation.</param>
/// <param name="Destination">Colonne écrite.</param>
/// <param name="PrefixeExclu">
/// Facultatif : les valeurs de la source qui commencent par ce préfixe sont déjà à jour et ne sont pas lues (filtre SQL).
/// </param>
internal sealed record CibleLots(
    string Nom, string? Schema, string Table, string ColonneCle, string Source, string Destination, string? PrefixeExclu = null);

/// <summary>Compteurs d'une colonne traitée par <see cref="MoteurLots"/>.</summary>
/// <param name="Examinees">Lignes lues.</param>
/// <param name="Ecrites">Nouvelles valeurs écrites.</param>
/// <param name="AJour">Lignes dont la destination valait déjà le résultat de la transformation (rien écrit).</param>
/// <param name="Concurrentes">Lignes modifiées ou supprimées par le service entre la lecture et l'écriture (non écrasées).</param>
/// <param name="Collisions">Écritures refusées par un index unique (comptées aussi dans <paramref name="Erreurs"/>).</param>
/// <param name="Erreurs">Lignes non traitées (transformation impossible, erreur de base), ou colonne inaccessible.</param>
internal sealed record BilanLots(long Examinees, long Ecrites, long AJour, long Concurrentes, long Collisions, long Erreurs);

/// <summary>
/// Moteur de lots commun au rechiffrement des champs (#396) et au recalcul d'un index aveugle (#473) : curseur sur la clé
/// primaire, lots dans des transactions courtes à attente de verrou bornée (<c>lock_timeout</c>), rejouable (une valeur à
/// jour n'est pas réécrite), et erreurs isolées ligne par ligne. Chaque mise à jour est conditionnée aux valeurs lues :
/// une ligne modifiée entre-temps par le service n'est jamais écrasée. Une écriture refusée par un index unique
/// (SQLSTATE 23505) est une <b>collision</b> : comptée en erreur, la ligne reste inchangée.
/// </summary>
/// <remarks>
/// Écriture directe en SQL, hors du suivi EF Core : ni colonnes d'audit, ni événement. Aucune valeur ni clé n'est passée
/// au rapport d'erreur : seulement la colonne, l'identifiant technique de la ligne, le type d'erreur et le code SQLSTATE.
/// </remarks>
internal static class MoteurLots
{
    /// <summary>SQLSTATE d'une violation d'unicité (PostgreSQL).</summary>
    public const string ViolationUnicite = "23505";

    /// <summary>SQLSTATE d'un verrou non obtenu dans le délai (<c>lock_not_available</c>, PostgreSQL).</summary>
    public const string VerrouNonObtenu = "55P03";

    /// <summary>Contrôle des paramètres communs, avant tout accès à la base.</summary>
    public static void Valider(OptionsRechiffrement options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.VerrousMaxParLot, 1, nameof(options.VerrousMaxParLot));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TailleLot, 1, nameof(options.TailleLot));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TailleLot, 10_000, nameof(options.TailleLot));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.DelaiVerrou, TimeSpan.Zero, nameof(options.DelaiVerrou));
    }

    /// <summary>Traite toute la colonne <paramref name="cible"/>, lot par lot.</summary>
    /// <param name="connexion">Connexion ouverte.</param>
    /// <param name="cible">Colonnes lue et écrite.</param>
    /// <param name="transformation">
    /// Nouvelle valeur de la destination, calculée depuis la source. Une <see cref="CryptographicException"/> ou une
    /// <see cref="FormatException"/> laisse la ligne en erreur, sans arrêter le traitement.
    /// </param>
    /// <param name="options">Taille des lots, pause, délai de verrou.</param>
    /// <param name="signaler">Reçoit chaque erreur (sans valeur ni clé).</param>
    /// <param name="horloge">Horloge des pauses entre lots.</param>
    /// <param name="cancellationToken">Annulation : les lots déjà validés sont acquis, la relance reprend le reste.</param>
    public static async Task<BilanLots> ExecuterAsync(
        DbConnection connexion,
        CibleLots cible,
        Func<string, string> transformation,
        OptionsRechiffrement options,
        Action<ErreurRechiffrement> signaler,
        TimeProvider horloge,
        CancellationToken cancellationToken)
    {
        var sql = new Requetes(cible, options.TailleLot);
        long examinees = 0, ecrites = 0, aJour = 0, concurrentes = 0, collisions = 0, erreurs = 0;
        object? curseur = null;

        try
        {
            while (true)
            {
                var lot = await LireLotAsync(connexion, sql, cible, curseur, cancellationToken);
                var aEcrire = new List<Ecriture>(lot.Count);
                foreach (var ligne in lot)
                {
                    string nouvelle;
                    try
                    {
                        nouvelle = transformation(ligne.Source);
                    }
                    catch (Exception erreur) when (erreur is CryptographicException or FormatException)
                    {
                        // Clé non configurée, valeur altérée ou illisible : la ligne reste en place, le traitement continue.
                        erreurs++;
                        signaler(new ErreurRechiffrement(cible.Nom, Texte(ligne.Cle), erreur.GetType().Name,
                            erreur is CryptographicException ? erreur.Message : "Valeur chiffrée illisible (base64)."));
                        continue;
                    }

                    if (string.Equals(nouvelle, ligne.Destination, StringComparison.Ordinal))
                    {
                        aJour++;
                    }
                    else
                    {
                        aEcrire.Add(new Ecriture(ligne, nouvelle));
                    }
                }

                try
                {
                    var (e, m) = await EcrireAsync(connexion, sql, aEcrire, options.DelaiVerrou, cancellationToken);
                    ecrites += e;
                    concurrentes += m;
                }
                catch (DbException)
                {
                    // Lot annulé (troncature, contrainte, collision, verrou…) : reprise ligne par ligne, chacune dans sa
                    // transaction, pour n'écarter que la ou les lignes en cause, sans sous-transaction. Après
                    // VerrousMaxParLot verrous non obtenus (55P03), la reprise s'arrête (#502) : un verrou de table tenu par
                    // le service coûterait sinon DelaiVerrou par ligne. Les lignes restantes sont comptées en erreur, non
                    // tentées, et reprises à la prochaine exécution.
                    var verrous = 0;
                    foreach (var ecriture in aEcrire)
                    {
                        if (verrous >= options.VerrousMaxParLot)
                        {
                            erreurs++;
                            signaler(new ErreurRechiffrement(cible.Nom, Texte(ecriture.Ligne.Cle), "RepriseArretee",
                                $"Reprise du lot arrêtée après {options.VerrousMaxParLot.ToString(CultureInfo.InvariantCulture)} verrou(s) non obtenu(s) (SQLSTATE {VerrouNonObtenu}) : ligne non tentée."));
                            continue;
                        }

                        try
                        {
                            var (e, m) = await EcrireAsync(connexion, sql, [ecriture], options.DelaiVerrou, cancellationToken);
                            ecrites += e;
                            concurrentes += m;
                        }
                        catch (DbException erreur)
                        {
                            erreurs++;
                            if (erreur.SqlState == VerrouNonObtenu)
                            {
                                verrous++;
                            }

                            var collision = erreur.SqlState == ViolationUnicite;
                            if (collision)
                            {
                                collisions++;
                            }

                            signaler(new ErreurRechiffrement(cible.Nom, Texte(ecriture.Ligne.Cle), erreur.GetType().Name,
                                collision ? $"Collision sur un index unique : valeur non écrite (SQLSTATE {ViolationUnicite})." : RechiffrementChamps.MessageBase(erreur)));
                        }
                    }
                }

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
        }
        catch (DbException erreur)
        {
            // Colonne inaccessible (droits, table absente, connexion perdue) : comptée en erreur, l'appelant passe à la suivante.
            erreurs++;
            signaler(new ErreurRechiffrement(cible.Nom, string.Empty, erreur.GetType().Name, RechiffrementChamps.MessageBase(erreur)));
        }

        return new BilanLots(examinees, ecrites, aJour, concurrentes, collisions, erreurs);
    }

    private static async Task<List<Ligne>> LireLotAsync(DbConnection connexion, Requetes sql, CibleLots cible, object? curseur, CancellationToken cancellationToken)
    {
        var lot = new List<Ligne>();
        await using var lecture = Sql.Commande(connexion, null, curseur is null ? sql.PremierLot : sql.LotSuivant);
        if (cible.PrefixeExclu is { } prefixe)
        {
            Sql.Parametre(lecture, "prefixe", prefixe);
        }

        if (curseur is not null)
        {
            Sql.Parametre(lecture, "curseur", curseur);
        }

        await using var lecteur = await lecture.ExecuteReaderAsync(cancellationToken);
        while (await lecteur.ReadAsync(cancellationToken))
        {
            var source = lecteur.GetString(1);
            var destination = sql.MemeColonne ? source : lecteur.IsDBNull(2) ? null : lecteur.GetString(2);
            lot.Add(new Ligne(lecteur.GetValue(0), source, destination));
        }

        return lot;
    }

    /// <summary>Écrit des valeurs dans une transaction courte, avec une attente de verrou bornée.</summary>
    private static async Task<(long Ecrites, long Modifiees)> EcrireAsync(
        DbConnection connexion, Requetes sql, IReadOnlyList<Ecriture> ecritures, TimeSpan delaiVerrou, CancellationToken cancellationToken)
    {
        if (ecritures.Count == 0)
        {
            return (0, 0);
        }

        long ecrites = 0, modifiees = 0;
        await using var transaction = await connexion.BeginTransactionAsync(cancellationToken);
        await Sql.DelaiVerrouAsync(connexion, transaction, delaiVerrou, cancellationToken);
        foreach (var ecriture in ecritures)
        {
            var nulle = !sql.MemeColonne && ecriture.Ligne.Destination is null;
            await using var miseAJour = Sql.Commande(connexion, transaction, nulle ? sql.MiseAJourDestinationNulle : sql.MiseAJour);
            Sql.Parametre(miseAJour, "nouvelle", ecriture.Nouvelle);
            Sql.Parametre(miseAJour, "cle", ecriture.Ligne.Cle);
            Sql.Parametre(miseAJour, "source", ecriture.Ligne.Source);
            if (!sql.MemeColonne && !nulle)
            {
                Sql.Parametre(miseAJour, "ancienne", ecriture.Ligne.Destination!);
            }

            if (await miseAJour.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                ecrites++;
            }
            else
            {
                // Modifiée ou supprimée par le service depuis la lecture : sa nouvelle valeur a été écrite par le service.
                modifiees++;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return (ecrites, modifiees);
    }

    private static string Texte(object identifiant) => Convert.ToString(identifiant, CultureInfo.InvariantCulture) ?? string.Empty;

    private sealed record Ligne(object Cle, string Source, string? Destination);

    private sealed record Ecriture(Ligne Ligne, string Nouvelle);

    /// <summary>Requêtes d'une cible (identifiants cités, valeurs en paramètres).</summary>
    private sealed class Requetes
    {
        public Requetes(CibleLots cible, int taille)
        {
            var table = Sql.Table(cible.Schema, cible.Table);
            var cle = Sql.Identifiant(cible.ColonneCle);
            var source = Sql.Identifiant(cible.Source);
            var destination = Sql.Identifiant(cible.Destination);
            MemeColonne = string.Equals(cible.Source, cible.Destination, StringComparison.Ordinal);

            var colonnes = MemeColonne ? $"{cle}, {source}" : $"{cle}, {source}, {destination}";
            var filtre = $"{source} IS NOT NULL" + (cible.PrefixeExclu is null ? string.Empty : $" AND NOT starts_with({source}, @prefixe)");
            var limite = $" ORDER BY {cle} LIMIT {taille.ToString(CultureInfo.InvariantCulture)}";
            PremierLot = $"SELECT {colonnes} FROM {table} WHERE {filtre}{limite}";
            LotSuivant = $"SELECT {colonnes} FROM {table} WHERE {filtre} AND {cle} > @curseur{limite}";

            var miseAJour = $"UPDATE {table} SET {destination} = @nouvelle WHERE {cle} = @cle AND {source} = @source";
            MiseAJour = MemeColonne ? miseAJour : $"{miseAJour} AND {destination} = @ancienne";
            MiseAJourDestinationNulle = $"{miseAJour} AND {destination} IS NULL";
        }

        public bool MemeColonne { get; }

        public string PremierLot { get; }

        public string LotSuivant { get; }

        public string MiseAJour { get; }

        public string MiseAJourDestinationNulle { get; }
    }
}
