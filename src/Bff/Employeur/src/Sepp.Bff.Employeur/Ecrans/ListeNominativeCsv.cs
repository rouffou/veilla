using System.Globalization;
using System.Text;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Bff.Employeur.Ecrans;

/// <summary>
/// POR-06 : export CSV d'une version de liste nominative, produit par le BFF à partir des données JSON du service
/// Postes et risques (aucun service ne fournit encore de document ; le PDF/A viendra du service Documents, DOC-02).
/// UTF-8 avec BOM et séparateur « ; » (ouverture directe dans un tableur belge) ; en-têtes dans la langue de
/// l'utilisateur ; aucune donnée d'identification nationale (NISS) ; cellules neutralisées contre l'injection de formules.
/// </summary>
public static class ListeNominativeCsv
{
    public const string TypeContenu = "text/csv; charset=utf-8";

    private static readonly Dictionary<Language, string[]> EnTetes = new()
    {
        [Language.Fr] = ["Nom", "Prénom", "Poste", "Risques", "Date de la dernière évaluation", "Origine"],
        [Language.Nl] = ["Naam", "Voornaam", "Functie", "Risico's", "Datum van de laatste beoordeling", "Oorsprong"],
        [Language.De] = ["Name", "Vorname", "Arbeitsplatz", "Risiken", "Datum der letzten Beurteilung", "Herkunft"],
        [Language.En] = ["Last name", "First name", "Position", "Risks", "Date of last assessment", "Origin"],
    };

    public static byte[] Generer(ListeNominativeDetail detail, Language langue)
    {
        var csv = new StringBuilder();
        Ligne(csv, EnTetes[langue]);
        foreach (var l in detail.Lignes)
        {
            Ligne(csv,
            [
                l.Nom ?? string.Empty,
                l.Prenom ?? string.Empty,
                l.Poste ?? string.Empty,
                string.Join(", ", l.CodesRisques),
                l.DateDerniereEvaluation?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                l.Origine,
            ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    public static string NomFichier(ListeNominativeEcran liste) =>
        string.Create(CultureInfo.InvariantCulture,
            $"liste-nominative-{Kebab(liste.Type)}-v{liste.Version}-{liste.DateReference:yyyy-MM-dd}.csv");

    /// <summary>Échappement RFC 4180 et neutralisation des cellules commençant par = + - @ (injection de formules, OWASP).</summary>
    public static string Cellule(string valeur)
    {
        if (valeur.Length > 0 && valeur[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            valeur = "'" + valeur;
        }

        return valeur.IndexOfAny([';', '"', '\n', '\r', ',']) >= 0 ? $"\"{valeur.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : valeur;
    }

    private static void Ligne(StringBuilder csv, IEnumerable<string> cellules) =>
        csv.Append(string.Join(';', cellules.Select(Cellule))).Append("\r\n");

    private static string Kebab(string valeur)
    {
        var sb = new StringBuilder();
        foreach (var c in valeur)
        {
            if (char.IsUpper(c) && sb.Length > 0)
            {
                sb.Append('-');
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }
}
