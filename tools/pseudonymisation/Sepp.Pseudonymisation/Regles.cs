using System.Globalization;
using System.Text.Json.Nodes;

using Sepp.BuildingBlocks.Infrastructure.Security;

namespace Sepp.Pseudonymisation;

/// <summary>Technique appliquée à une colonne (voir docs/conformite/pseudonymisation.md).</summary>
public enum Technique
{
    /// <summary>Lecture seule : la colonne sert de contexte aux autres règles, elle n'est pas modifiée.</summary>
    Conserver,
    Nom,
    Prenom,
    NomComplet,
    Email,
    Telephone,
    Rue,
    NumeroRue,
    Boite,
    Denomination,
    NomLieu,
    Utilisateur,
    NumeroBce,
    NumeroUniteEtablissement,
    Coordonnee,
    DateDecalee,
    /// <summary>Texte libre en clair remplacé par un texte neutre (NULL reste NULL).</summary>
    TexteSupprime,
    /// <summary>Colonne jsonb remplacée par <c>{}</c>.</summary>
    JsonVide,
    /// <summary>Champ chiffré déchiffré par la clé source puis rechiffré par la clé cible, valeur conservée.</summary>
    ChiffreRechiffre,
    /// <summary>Champ chiffré dont le contenu est remplacé par un texte neutre, chiffré par la clé cible.</summary>
    ChiffreSupprime,
    /// <summary>Champ chiffré mis à NULL.</summary>
    ChiffreNul,
    /// <summary>Champ chiffré contenant du JSON : les chaînes des propriétés nommées (paramètre) sont neutralisées.</summary>
    ChiffreJson,
    /// <summary>NISS chiffré remplacé par un NISS fictif valide, cohérent avec la date de naissance décalée et le sexe.</summary>
    NissChiffre,
    /// <summary>Index aveugle recalculé avec la clé cible à partir du NISS fictif.</summary>
    NissIndexAveugle,
}

public sealed record RegleColonne(string Colonne, Technique Technique, string? Parametre = null);

public sealed record RegleTable(string Table, IReadOnlyList<RegleColonne> Colonnes, string ColonneCle = "id");

/// <summary>Règles déclarées pour un service : tables traitées, tables vidées, chiffrement requis.</summary>
public sealed record ReglesService(string Nom, IReadOnlyList<RegleTable> Tables, IReadOnlyList<string> TablesAVider, bool Chiffrement, bool IndexAveugle = false);

/// <summary>Contexte d'une ligne en cours de pseudonymisation.</summary>
public sealed class ContexteLigne(
    string table, object cle, IReadOnlyDictionary<string, object?> originales, Pseudonymiseur pseudonymiseur, FieldEncryptor? source, FieldEncryptor? cible, BlindIndex? indexCible)
{
    public string Table { get; } = table;

    public string Seed { get; } = Convert.ToString(cle, CultureInfo.InvariantCulture)!;

    public IReadOnlyDictionary<string, object?> Originales { get; } = originales;

    public Pseudonymiseur P { get; } = pseudonymiseur;

    public FieldEncryptor Source => source ?? throw new InvalidOperationException($"Aucune clé source configurée pour la table {Table}.");

    public FieldEncryptor Cible => cible ?? throw new InvalidOperationException($"Aucune clé cible configurée pour la table {Table}.");

    public BlindIndex IndexCible => indexCible ?? throw new InvalidOperationException("Aucune clé d'index aveugle cible configurée.");

    /// <summary>NISS fictif calculé pour la ligne (en clair, jamais écrit tel quel).</summary>
    public string? NissFictif { get; set; }
}

/// <summary>Application des techniques.</summary>
public static class Techniques
{
    /// <summary>Valeur de remplacement ; <see cref="Ignorer"/> signifie « ne pas modifier ».</summary>
    public static object? Appliquer(RegleColonne regle, ContexteLigne ctx)
    {
        ctx.Originales.TryGetValue(regle.Colonne, out var original);
        var texte = original as string;
        var absent = original is null or DBNull;
        var seed = ctx.Seed;
        var p = ctx.P;

        return regle.Technique switch
        {
            Technique.Conserver => Ignorer,
            Technique.Nom => p.Nom(seed),
            Technique.Prenom => p.Prenom(seed),
            Technique.NomComplet => p.NomComplet(seed),
            Technique.Email => absent ? null : p.Email(seed),
            Technique.Telephone => absent ? null : p.Telephone(seed),
            Technique.Rue => absent ? null : p.Rue(seed),
            Technique.NumeroRue => absent ? null : p.NumeroRue(seed),
            Technique.Boite => absent ? null : p.Boite(seed),
            Technique.Denomination => absent ? null : p.Denomination(seed),
            Technique.NomLieu => absent ? null : p.NomLieu(regle.Parametre ?? "Lieu", seed),
            Technique.Utilisateur => absent ? null : p.Utilisateur(texte!),
            Technique.NumeroBce => absent ? null : p.NumeroBce(texte!),
            Technique.NumeroUniteEtablissement => absent ? null : p.NumeroUniteEtablissement(texte!),
            Technique.Coordonnee => absent ? null : p.DecalerCoordonnee(Convert.ToDouble(original, CultureInfo.InvariantCulture), seed, regle.Parametre ?? "x"),
            Technique.DateDecalee => absent ? null : p.DecalerDate(Date(original), seed),
            Technique.TexteSupprime => absent ? null : Pseudonymiseur.TexteSupprime,
            Technique.JsonVide => "{}",
            Technique.ChiffreRechiffre => absent ? null : ctx.Cible.Encrypt(ctx.Source.Decrypt(texte!)),
            Technique.ChiffreSupprime => absent ? null : ctx.Cible.Encrypt(Pseudonymiseur.TexteSupprime),
            Technique.ChiffreNul => null,
            Technique.ChiffreJson => absent ? null : ctx.Cible.Encrypt(NeutraliserJson(ctx.Source.Decrypt(texte!), regle.Parametre)),
            Technique.NissChiffre => NissChiffre(ctx, texte),
            Technique.NissIndexAveugle => ctx.IndexCible.Compute(ctx.NissFictif ?? throw new InvalidOperationException("NissIndexAveugle exige NissChiffre avant.")),
            _ => throw new InvalidOperationException($"Technique inconnue : {regle.Technique}."),
        };
    }

    /// <summary>Marqueur interne : la colonne n'est pas mise à jour.</summary>
    public static readonly object Ignorer = new();

    public static DateOnly Date(object? valeur) => valeur switch
    {
        DateOnly d => d,
        DateTime t => DateOnly.FromDateTime(t),
        _ => throw new InvalidOperationException("Date attendue."),
    };

    /// <summary>Remplace par un texte neutre les chaînes des propriétés nommées (liste séparée par des virgules), à tout niveau.</summary>
    public static string NeutraliserJson(string json, string? proprietes)
    {
        var noms = new HashSet<string>((proprietes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        var noeud = JsonNode.Parse(json);
        Parcourir(noeud, noms);
        return noeud?.ToJsonString() ?? "null";
    }

    private static void Parcourir(JsonNode? noeud, HashSet<string> noms)
    {
        switch (noeud)
        {
            case JsonObject objet:
                foreach (var nom in objet.Select(kv => kv.Key).ToList())
                {
                    if (noms.Contains(nom) && objet[nom] is JsonValue valeur && valeur.TryGetValue<string>(out _))
                    {
                        objet[nom] = Pseudonymiseur.TexteSupprime;
                    }
                    else
                    {
                        Parcourir(objet[nom], noms);
                    }
                }

                break;
            case JsonArray tableau:
                foreach (var element in tableau)
                {
                    Parcourir(element, noms);
                }

                break;
        }
    }

    private static string NissChiffre(ContexteLigne ctx, string? chiffre)
    {
        var original = ctx.Source.Decrypt(chiffre ?? throw new InvalidOperationException("niss_chiffre absent."));
        var naissance = ctx.P.DecalerDate(Date(ctx.Originales["date_naissance"]), ctx.Seed);
        var sexe = ctx.Originales.TryGetValue("sexe", out var s) ? s as string ?? string.Empty : string.Empty;
        ctx.NissFictif = ctx.P.Niss(original, naissance, sexe);
        return ctx.Cible.Encrypt(ctx.NissFictif);
    }
}
