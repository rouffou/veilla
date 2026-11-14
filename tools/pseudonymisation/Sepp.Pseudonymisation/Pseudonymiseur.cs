using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sepp.Pseudonymisation;

/// <summary>
/// Générateur de valeurs fictives déterministes à clé secrète (NF-14). Une même valeur d'origine donne toujours la même
/// valeur fictive pour une même clé, dans tous les services : la « table de correspondance » est la fonction HMAC-SHA256
/// elle-même, qui n'est jamais stockée. Les valeurs qui doivent rester uniques (NISS, numéros BCE) passent par une
/// correspondance en mémoire avec résolution des collisions, propre à l'exécution.
/// </summary>
public sealed class Pseudonymiseur
{
    public const string TexteSupprime = "[supprimé par la pseudonymisation]";

    private static readonly string[] Prenoms =
    [
        "Alice", "Bruno", "Camille", "David", "Emma", "Francois", "Gaelle", "Hugo", "Ines", "Jules", "Katia", "Louis",
        "Marie", "Nadia", "Olivier", "Pauline", "Quentin", "Rose", "Samuel", "Thea", "Ulrich", "Valerie", "William",
        "Xavier", "Yasmine", "Zoe", "Adrien", "Berenice", "Cedric", "Delphine", "Etienne", "Fanny", "Gilles", "Helene",
        "Isabelle", "Julien", "Karim", "Laura", "Mathieu", "Noemie", "Oscar", "Priscilla", "Raphael", "Sophie",
        "Thomas", "Ursula", "Victor", "Wendy", "Yannick", "Zelie",
    ];

    private static readonly string[] Noms =
    [
        "Exemple", "Fictif", "Modele", "Demo", "Test", "Specimen", "Imaginaire", "Echantillon", "Lambda", "Anonyme",
        "Dupont-Fictif", "Martin-Test", "Durand-Demo", "Leroy-Modele", "Moreau-Exemple", "Simon-Fictif", "Laurent-Test",
        "Lefebvre-Demo", "Michel-Modele", "Garcia-Exemple", "Peeters-Fictif", "Janssens-Test", "Maes-Demo",
        "Jacobs-Modele", "Mertens-Exemple", "Willems-Fictif", "Claes-Test", "Goossens-Demo", "Wouters-Modele",
        "De Smet-Exemple", "Dubois-Fictif", "Lambert-Test", "Bertrand-Demo", "Roux-Modele", "Vincent-Exemple",
        "Fontaine-Fictif", "Rousseau-Test", "Girard-Demo", "Andre-Modele", "Lecomte-Exemple",
    ];

    private static readonly string[] Rues =
    [
        "Rue de l'Exemple", "Avenue du Modele", "Chaussee de la Demonstration", "Boulevard des Tests",
        "Place du Specimen", "Rue Imaginaire", "Avenue Fictive", "Rue de la Formation", "Impasse du Pseudonyme",
        "Quai de la Recette", "Voorbeeldstraat", "Testlaan", "Fictieve Straat", "Modelplein",
    ];

    private readonly byte[] _cle;
    private readonly Dictionary<(string Domaine, string Original), string> _correspondance = [];
    private readonly Dictionary<string, HashSet<string>> _utilises = [];

    public Pseudonymiseur(byte[] cle)
    {
        ArgumentNullException.ThrowIfNull(cle);
        if (cle.Length < 32)
        {
            throw new ArgumentException("La clé de pseudonymisation doit faire au moins 256 bits.", nameof(cle));
        }

        _cle = [.. cle];
    }

    public static Pseudonymiseur DepuisBase64(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new InvalidOperationException("Pseudonymisation:Cle manquante (variable PSEUDONYMISATION__CLE, base64, 256 bits au moins).");
        }

        try
        {
            return new Pseudonymiseur(Convert.FromBase64String(valeur));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Pseudonymisation:Cle doit être en base64.", ex);
        }
    }

    public int Entier(string domaine, string valeur, int exclusif, int rang = 0, int tentative = 0)
    {
        var hash = Hash(domaine, valeur, tentative);
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(rang * 4, 4)) % (uint)exclusif);
    }

    public string Prenom(string seed) => Prenoms[Entier("prenom", seed, Prenoms.Length)];

    public string Nom(string seed) => Noms[Entier("nom", seed, Noms.Length)];

    public string NomComplet(string seed) => $"{Prenom(seed)} {Nom(seed)}";

    public string Email(string seed) =>
        $"{Slug(Prenom(seed))}.{Slug(Nom(seed))}.{Entier("email", seed, 10_000).ToString("D4", CultureInfo.InvariantCulture)}@exemple.invalid";

    public string Telephone(string seed) =>
        $"+32 2 000 {Entier("tel", seed, 100).ToString("D2", CultureInfo.InvariantCulture)} {Entier("tel", seed, 100, 1).ToString("D2", CultureInfo.InvariantCulture)}";

    public string Rue(string seed) => Rues[Entier("rue", seed, Rues.Length)];

    public string NumeroRue(string seed) => (Entier("numero", seed, 200) + 1).ToString(CultureInfo.InvariantCulture);

    public string Boite(string seed) => "b" + (Entier("boite", seed, 20) + 1).ToString(CultureInfo.InvariantCulture);

    public string Denomination(string seed) => $"Entreprise fictive {Entier("denomination", seed, 1_000_000).ToString("D6", CultureInfo.InvariantCulture)}";

    public string NomLieu(string prefixe, string seed) => $"{prefixe} fictif {Entier("lieu", seed, 1_000_000).ToString("D6", CultureInfo.InvariantCulture)}";

    /// <summary>Identifiant technique d'utilisateur (created_by, auteur…) : jeton stable, non réversible sans la clé.</summary>
    public string Utilisateur(string original) =>
        original is "system" or "anonymous" ? original : "utilisateur-" + Convert.ToHexString(Hash("utilisateur", original, 0).AsSpan(0, 6)).ToLowerInvariant();

    /// <summary>Décalage en jours, entre -120 et +120 hors zéro, propre à la personne (même valeur dans tous les services).</summary>
    public int DecalageJours(string seed)
    {
        var v = Entier("decalage", seed, 240);
        return v < 120 ? v - 120 : v - 119;
    }

    public DateOnly DecalerDate(DateOnly date, string seed) => date.AddDays(DecalageJours(seed));

    /// <summary>Coordonnée géographique déplacée de quelques kilomètres au plus.</summary>
    public double DecalerCoordonnee(double valeur, string seed, string axe) =>
        valeur + ((Entier("coord-" + axe, seed, 1001) - 500) / 10_000d);

    public string NumeroBce(string original) =>
        Correspondre("bce", original, t => NumeroAvecControle(original, "bce", t, ((char)('0' + Entier("bce1", original, 2, 1, t))).ToString()));

    public string NumeroUniteEtablissement(string original) =>
        Correspondre("ue", original, t => NumeroAvecControle(original, "ue", t, ((char)('2' + Entier("ue1", original, 7, 1, t))).ToString()));

    /// <summary>
    /// NISS fictif valide (contrôle modulo 97) cohérent avec la date de naissance fictive et le sexe, unique pour l'exécution.
    /// </summary>
    public string Niss(string original, DateOnly naissance, string sexe)
    {
        var apres2000 = naissance.Year >= 2000;
        var date = string.Create(CultureInfo.InvariantCulture, $"{naissance.Year % 100:D2}{naissance.Month:D2}{naissance.Day:D2}");
        return Correspondre("niss", original, t =>
        {
            var sequence = sexe switch
            {
                "Masculin" => (2 * Entier("niss", original, 498, 0, t)) + 1,
                "Feminin" => (2 * Entier("niss", original, 498, 0, t)) + 2,
                _ => Entier("niss", original, 996, 0, t) + 1,
            };
            var corps = date + sequence.ToString("D3", CultureInfo.InvariantCulture);
            var valeur = long.Parse(corps, CultureInfo.InvariantCulture) + (apres2000 ? 2_000_000_000L : 0);
            return corps + (97 - (valeur % 97)).ToString("D2", CultureInfo.InvariantCulture);
        });
    }

    private string NumeroAvecControle(string original, string domaine, int tentative, string premier)
    {
        var chiffres = premier + Entier("num-" + domaine, original, 10_000_000, 0, tentative).ToString("D7", CultureInfo.InvariantCulture);
        var baseNumero = long.Parse(chiffres, CultureInfo.InvariantCulture);
        return chiffres + (97 - (baseNumero % 97)).ToString("D2", CultureInfo.InvariantCulture);
    }

    private string Correspondre(string domaine, string original, Func<int, string> generer)
    {
        if (_correspondance.TryGetValue((domaine, original), out var existant))
        {
            return existant;
        }

        if (!_utilises.TryGetValue(domaine, out var utilises))
        {
            utilises = [];
            _utilises[domaine] = utilises;
        }

        for (var tentative = 0; tentative < 10_000; tentative++)
        {
            var candidat = generer(tentative);
            if (utilises.Add(candidat))
            {
                _correspondance[(domaine, original)] = candidat;
                return candidat;
            }
        }

        throw new InvalidOperationException($"Plus de valeur fictive disponible pour le domaine « {domaine} » : espace épuisé.");
    }

    private byte[] Hash(string domaine, string valeur, int tentative) =>
        HMACSHA256.HashData(_cle, Encoding.UTF8.GetBytes($"{domaine}\u001f{valeur}\u001f{tentative.ToString(CultureInfo.InvariantCulture)}"));

    private static string Slug(string texte) =>
        new([.. texte.ToLowerInvariant().Where(char.IsAsciiLetter)]);
}
