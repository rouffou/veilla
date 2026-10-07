using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Documents.Domain.Fusion;

/// <summary>Type d'un champ de fusion déclaré par le modèle (DOC-01).</summary>
public enum TypeChamp
{
    Texte,
    Date,
    Nombre,
    Booleen,
    Liste,
}

/// <summary>Champ de fusion déclaré : seuls les champs déclarés peuvent apparaître dans le modèle et être fournis.</summary>
public sealed record DefinitionChamp(string Nom, TypeChamp Type, bool Obligatoire);

/// <summary>Valeur fournie pour un champ : un texte (dates au format ISO <c>aaaa-mm-jj</c>, nombres et booléens invariants) ou une liste.</summary>
public sealed record ValeurChamp
{
    private ValeurChamp(string? texte, IReadOnlyList<string>? elements)
    {
        Texte = texte;
        Elements = elements;
    }

    public string? Texte { get; }

    public IReadOnlyList<string>? Elements { get; }

    public static ValeurChamp Simple(string? texte) => new(texte, null);

    public static ValeurChamp Liste(IEnumerable<string> elements) => new(null, elements.ToList());

    public static ValeurChamp Date(DateOnly date) => new(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null);
}

public enum TypeBloc
{
    Titre,
    SousTitre,
    Paragraphe,
    Puce,
    Separateur,
}

/// <summary>Bloc de texte mis en page : la structure vient du modèle, jamais des valeurs fusionnées.</summary>
public sealed record Bloc(TypeBloc Type, string Texte);

/// <summary>Résultat de la fusion, indépendant du format de rendu (PDF/A).</summary>
public sealed record DocumentFusionne(IReadOnlyList<Bloc> Blocs)
{
    /// <summary>Texte intégral, une ligne par bloc (aperçu, tests).</summary>
    public string TexteIntegral => string.Join('\n', Blocs.Select(b => b.Texte));
}

/// <summary>
/// Moteur de fusion simple et sûr (DOC-01) : aucune expression ni exécution de code dans les modèles.
/// </summary>
/// <remarks>
/// Syntaxe, ligne par ligne :
/// <list type="bullet">
/// <item><c># titre</c>, <c>## sous-titre</c>, <c>- puce</c>, <c>---</c> séparateur ; une ligne vide termine un paragraphe ;</item>
/// <item><c>{{champ}}</c> insère la valeur d'un champ déclaré (une liste est jointe par des virgules) ;</item>
/// <item><c>{{#si champ}}</c> … <c>{{#sinon}}</c> … <c>{{/si}}</c> : condition sur un booléen vrai ou une valeur non vide, sur des lignes seules ;</item>
/// <item><c>{{#chaque liste}}</c> … <c>{{/chaque}}</c> : répète les lignes pour chaque élément, <c>{{.}}</c> désignant l'élément courant.</item>
/// </list>
/// Échappement : la structure (titres, puces, paragraphes) est analysée sur le modèle <em>avant</em> la substitution ; une
/// valeur est toujours insérée comme du texte brut (caractères de contrôle et retours à la ligne neutralisés, longueur
/// bornée) et ne peut créer ni balise, ni bloc, ni champ. Toute balise inconnue, tout champ non déclaré et toute valeur
/// non déclarée sont refusés.
/// </remarks>
public static partial class MoteurFusion
{
    public const int LongueurMaximaleModele = 50_000;
    public const int LongueurMaximaleValeur = 2_000;
    public const int ElementsMaximaux = 100;

    /// <summary>Vérifie un modèle : balises connues et équilibrées, champs déclarés, types cohérents. Liste vide si valide.</summary>
    public static IReadOnlyList<string> Analyser(string contenu, IReadOnlyCollection<DefinitionChamp> champs)
    {
        var erreurs = new List<string>();
        var declares = Declarations(champs, erreurs);
        if (string.IsNullOrWhiteSpace(contenu))
        {
            erreurs.Add("Le contenu du modèle est vide.");
            return erreurs;
        }

        if (contenu.Length > LongueurMaximaleModele)
        {
            erreurs.Add($"Le contenu du modèle dépasse {LongueurMaximaleModele} caractères.");
            return erreurs;
        }

        _ = Analyse(contenu, declares, erreurs);
        return erreurs;
    }

    /// <summary>Fusionne les valeurs dans le modèle ; lève une <see cref="DomainException"/> détaillant toutes les erreurs.</summary>
    public static DocumentFusionne Fusionner(
        string contenu,
        IReadOnlyCollection<DefinitionChamp> champs,
        IReadOnlyDictionary<string, ValeurChamp> valeurs,
        Language langue)
    {
        var erreurs = new List<string>();
        var declares = Declarations(champs, erreurs);
        var noeuds = Analyse(contenu ?? string.Empty, declares, erreurs);
        var valeursTypees = Valider(declares, valeurs, langue, erreurs);
        if (erreurs.Count > 0)
        {
            throw new DomainException(string.Join(" ", erreurs));
        }

        var blocs = new List<Bloc>();
        var paragraphe = new StringBuilder();
        Rendre(noeuds, valeursTypees, element: null, blocs, paragraphe);
        TerminerParagraphe(blocs, paragraphe);
        return new DocumentFusionne(blocs);
    }

    private static Dictionary<string, DefinitionChamp> Declarations(IReadOnlyCollection<DefinitionChamp> champs, List<string> erreurs)
    {
        var declares = new Dictionary<string, DefinitionChamp>(StringComparer.Ordinal);
        foreach (var champ in champs)
        {
            if (!NomChamp().IsMatch(champ.Nom))
            {
                erreurs.Add($"Nom de champ invalide : '{champ.Nom}' (minuscules, chiffres et _, 50 caractères au plus).");
            }
            else if (!declares.TryAdd(champ.Nom, champ))
            {
                erreurs.Add($"Le champ '{champ.Nom}' est déclaré deux fois.");
            }
        }

        return declares;
    }

    // ---- Analyse du modèle -------------------------------------------------------------------------------------------

    private abstract record Noeud;

    private sealed record Ligne(TypeLigne Type, IReadOnlyList<Segment> Segments) : Noeud;

    private sealed record Condition(string Champ, List<Noeud> Alors, List<Noeud> Sinon) : Noeud;

    private sealed record Repetition(string Champ, List<Noeud> Corps) : Noeud;

    private enum TypeLigne
    {
        Texte,
        Vide,
        Titre,
        SousTitre,
        Puce,
        Separateur,
    }

    private abstract record Segment;

    private sealed record Litteral(string Texte) : Segment;

    private sealed record Champ(string Nom) : Segment;

    private sealed record ElementCourant : Segment;

    private sealed class Cadre(Noeud? noeud, List<Noeud> cible)
    {
        public Noeud? Noeud { get; } = noeud;

        public List<Noeud> Cible { get; set; } = cible;
    }

    private static List<Noeud> Analyse(string contenu, Dictionary<string, DefinitionChamp> declares, List<string> erreurs)
    {
        var racine = new List<Noeud>();
        var pile = new Stack<Cadre>();
        pile.Push(new Cadre(null, racine));
        var numero = 0;
        foreach (var brute in contenu.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            numero++;
            var ligne = brute.TrimEnd();
            var nue = ligne.Trim();
            if (OuvertureBloc().Match(nue) is { Success: true } ouverture)
            {
                var nom = ouverture.Groups["nom"].Value;
                var estRepetition = ouverture.Groups["mot"].Value == "chaque";
                if (!declares.TryGetValue(nom, out var definition))
                {
                    erreurs.Add($"Ligne {numero} : champ non déclaré '{nom}'.");
                }
                else if (estRepetition && definition.Type != TypeChamp.Liste)
                {
                    erreurs.Add($"Ligne {numero} : {{{{#chaque {nom}}}}} exige un champ de type Liste.");
                }

                Noeud noeud = estRepetition ? new Repetition(nom, []) : new Condition(nom, [], []);
                pile.Peek().Cible.Add(noeud);
                pile.Push(new Cadre(noeud, noeud is Repetition r ? r.Corps : ((Condition)noeud).Alors));
                continue;
            }

            if (nue == "{{#sinon}}")
            {
                if (pile.Peek().Noeud is Condition condition && pile.Peek().Cible == condition.Alors)
                {
                    pile.Peek().Cible = condition.Sinon;
                }
                else
                {
                    erreurs.Add($"Ligne {numero} : {{{{#sinon}}}} hors d'un bloc {{{{#si}}}}.");
                }

                continue;
            }

            if (nue is "{{/si}}" or "{{/chaque}}")
            {
                var attendu = nue == "{{/si}}" ? typeof(Condition) : typeof(Repetition);
                if (pile.Count > 1 && pile.Peek().Noeud!.GetType() == attendu)
                {
                    pile.Pop();
                }
                else
                {
                    erreurs.Add($"Ligne {numero} : fermeture {nue} sans ouverture correspondante.");
                }

                continue;
            }

            var dansRepetition = pile.Any(c => c.Noeud is Repetition);
            pile.Peek().Cible.Add(AnalyserLigne(ligne, numero, declares, dansRepetition, erreurs));
        }

        if (pile.Count > 1)
        {
            erreurs.Add("Bloc {{#si}} ou {{#chaque}} non fermé.");
        }

        return racine;
    }

    private static Ligne AnalyserLigne(string ligne, int numero, Dictionary<string, DefinitionChamp> declares, bool dansRepetition, List<string> erreurs)
    {
        var (type, texte) = ligne.TrimStart() switch
        {
            "" => (TypeLigne.Vide, string.Empty),
            "---" => (TypeLigne.Separateur, string.Empty),
            var l when l.StartsWith("## ", StringComparison.Ordinal) => (TypeLigne.SousTitre, l[3..]),
            var l when l.StartsWith("# ", StringComparison.Ordinal) => (TypeLigne.Titre, l[2..]),
            var l when l.StartsWith("- ", StringComparison.Ordinal) => (TypeLigne.Puce, l[2..]),
            var l => (TypeLigne.Texte, l),
        };

        var segments = new List<Segment>();
        var position = 0;
        foreach (Match balise in Balise().Matches(texte))
        {
            AjouterLitteral(segments, texte[position..balise.Index], numero, erreurs);
            position = balise.Index + balise.Length;
            var nom = balise.Groups[1].Value.Trim();
            if (nom == ".")
            {
                if (!dansRepetition)
                {
                    erreurs.Add($"Ligne {numero} : {{{{.}}}} n'est permis que dans un bloc {{{{#chaque}}}}.");
                }

                segments.Add(new ElementCourant());
            }
            else if (!NomChamp().IsMatch(nom))
            {
                erreurs.Add($"Ligne {numero} : balise non autorisée '{{{{{nom}}}}}' (seuls les champs déclarés sont permis).");
            }
            else if (!declares.ContainsKey(nom))
            {
                erreurs.Add($"Ligne {numero} : champ non déclaré '{nom}'.");
            }
            else
            {
                segments.Add(new Champ(nom));
            }
        }

        AjouterLitteral(segments, texte[position..], numero, erreurs);
        return new Ligne(type, segments);
    }

    private static void AjouterLitteral(List<Segment> segments, string texte, int numero, List<string> erreurs)
    {
        if (texte.Contains("{{", StringComparison.Ordinal))
        {
            erreurs.Add($"Ligne {numero} : balise « {{{{ » non fermée.");
        }

        if (texte.Length > 0)
        {
            segments.Add(new Litteral(texte));
        }
    }

    // ---- Valeurs ----------------------------------------------------------------------------------------------------

    private sealed record ValeurTypee(string Texte, IReadOnlyList<string> Elements, bool Vraie);

    private static Dictionary<string, ValeurTypee> Valider(
        Dictionary<string, DefinitionChamp> declares,
        IReadOnlyDictionary<string, ValeurChamp> valeurs,
        Language langue,
        List<string> erreurs)
    {
        var culture = Culture(langue);
        var resultat = new Dictionary<string, ValeurTypee>(StringComparer.Ordinal);
        foreach (var nom in valeurs.Keys.Where(n => !declares.ContainsKey(n)))
        {
            erreurs.Add($"Valeur fournie pour un champ non déclaré : '{nom}'.");
        }

        foreach (var (nom, definition) in declares)
        {
            valeurs.TryGetValue(nom, out var valeur);
            var typee = Typer(nom, definition.Type, valeur, langue, culture, erreurs);
            if (definition.Obligatoire && !typee.Vraie && definition.Type != TypeChamp.Booleen)
            {
                erreurs.Add($"Le champ obligatoire '{nom}' n'est pas renseigné.");
            }
            else if (definition.Obligatoire && definition.Type == TypeChamp.Booleen && valeur?.Texte is null)
            {
                erreurs.Add($"Le champ obligatoire '{nom}' n'est pas renseigné.");
            }

            resultat[nom] = typee;
        }

        return resultat;
    }

    private static ValeurTypee Typer(string nom, TypeChamp type, ValeurChamp? valeur, Language langue, CultureInfo culture, List<string> erreurs)
    {
        var vide = new ValeurTypee(string.Empty, [], false);
        if (valeur is null)
        {
            return vide;
        }

        if (type == TypeChamp.Liste)
        {
            if (valeur.Elements is null)
            {
                erreurs.Add($"Le champ '{nom}' attend une liste.");
                return vide;
            }

            if (valeur.Elements.Count > ElementsMaximaux)
            {
                erreurs.Add($"Le champ '{nom}' compte plus de {ElementsMaximaux} éléments.");
                return vide;
            }

            var elements = valeur.Elements.Select(e => Neutraliser(nom, e, erreurs)).Where(e => e.Length > 0).ToList();
            return new ValeurTypee(string.Join(", ", elements), elements, elements.Count > 0);
        }

        if (valeur.Elements is not null)
        {
            erreurs.Add($"Le champ '{nom}' n'accepte pas de liste.");
            return vide;
        }

        var texte = Neutraliser(nom, valeur.Texte, erreurs);
        if (texte.Length == 0)
        {
            return vide;
        }

        switch (type)
        {
            case TypeChamp.Date:
                if (!DateOnly.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    erreurs.Add($"Le champ '{nom}' attend une date au format aaaa-mm-jj.");
                    return vide;
                }

                var format = langue == Language.De ? "d. MMMM yyyy" : "d MMMM yyyy";
                return new ValeurTypee(date.ToString(format, culture), [], true);
            case TypeChamp.Nombre:
                if (!decimal.TryParse(texte, NumberStyles.Number, CultureInfo.InvariantCulture, out var nombre))
                {
                    erreurs.Add($"Le champ '{nom}' attend un nombre.");
                    return vide;
                }

                return new ValeurTypee(nombre.ToString("G", culture), [], true);
            case TypeChamp.Booleen:
                if (!bool.TryParse(texte, out var booleen))
                {
                    erreurs.Add($"Le champ '{nom}' attend « true » ou « false ».");
                    return vide;
                }

                return new ValeurTypee(OuiNon(booleen, langue), [], booleen);
            default:
                return new ValeurTypee(texte, [], true);
        }
    }

    /// <summary>Insertion en texte brut : caractères de contrôle et retours à la ligne remplacés, espaces réduites, longueur bornée.</summary>
    private static string Neutraliser(string nom, string? valeur, List<string> erreurs)
    {
        if (string.IsNullOrEmpty(valeur))
        {
            return string.Empty;
        }

        if (valeur.Length > LongueurMaximaleValeur)
        {
            erreurs.Add($"La valeur du champ '{nom}' dépasse {LongueurMaximaleValeur} caractères.");
            return string.Empty;
        }

        var nettoye = new string(valeur.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return Espaces().Replace(nettoye, " ").Trim();
    }

    private static string OuiNon(bool valeur, Language langue) => (valeur, langue) switch
    {
        (true, Language.Nl) => "ja",
        (false, Language.Nl) => "nee",
        (true, Language.De) => "ja",
        (false, Language.De) => "nein",
        (true, Language.En) => "yes",
        (false, Language.En) => "no",
        (true, _) => "oui",
        (false, _) => "non",
    };

    /// <summary>Culture d'affichage des dates et nombres d'un document (NF-40).</summary>
    public static CultureInfo Culture(Language langue) => CultureInfo.GetCultureInfo(langue switch
    {
        Language.Nl => "nl-BE",
        Language.De => "de-BE",
        Language.En => "en-GB",
        _ => "fr-BE",
    });

    // ---- Rendu ------------------------------------------------------------------------------------------------------

    private static void Rendre(List<Noeud> noeuds, Dictionary<string, ValeurTypee> valeurs, string? element, List<Bloc> blocs, StringBuilder paragraphe)
    {
        foreach (var noeud in noeuds)
        {
            switch (noeud)
            {
                case Condition condition:
                    Rendre(valeurs[condition.Champ].Vraie ? condition.Alors : condition.Sinon, valeurs, element, blocs, paragraphe);
                    break;
                case Repetition repetition:
                    foreach (var item in valeurs[repetition.Champ].Elements)
                    {
                        Rendre(repetition.Corps, valeurs, item, blocs, paragraphe);
                    }

                    break;
                case Ligne ligne:
                    RendreLigne(ligne, valeurs, element, blocs, paragraphe);
                    break;
            }
        }
    }

    private static void RendreLigne(Ligne ligne, Dictionary<string, ValeurTypee> valeurs, string? element, List<Bloc> blocs, StringBuilder paragraphe)
    {
        var texte = string.Concat(ligne.Segments.Select(s => s switch
        {
            Litteral l => l.Texte,
            Champ c => valeurs[c.Nom].Texte,
            ElementCourant => element ?? string.Empty,
            _ => string.Empty,
        })).Trim();

        switch (ligne.Type)
        {
            case TypeLigne.Vide:
                TerminerParagraphe(blocs, paragraphe);
                break;
            case TypeLigne.Texte:
                if (texte.Length > 0)
                {
                    paragraphe.Append(paragraphe.Length > 0 ? " " : string.Empty).Append(texte);
                }

                break;
            default:
                TerminerParagraphe(blocs, paragraphe);
                var type = ligne.Type switch
                {
                    TypeLigne.Titre => TypeBloc.Titre,
                    TypeLigne.SousTitre => TypeBloc.SousTitre,
                    TypeLigne.Puce => TypeBloc.Puce,
                    _ => TypeBloc.Separateur,
                };
                if (texte.Length > 0 || type == TypeBloc.Separateur)
                {
                    blocs.Add(new Bloc(type, texte));
                }

                break;
        }
    }

    private static void TerminerParagraphe(List<Bloc> blocs, StringBuilder paragraphe)
    {
        if (paragraphe.Length > 0)
        {
            blocs.Add(new Bloc(TypeBloc.Paragraphe, paragraphe.ToString()));
            paragraphe.Clear();
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,49}$")]
    private static partial Regex NomChamp();

    [GeneratedRegex(@"^\{\{#(?<mot>si|chaque) (?<nom>[a-z][a-z0-9_]{0,49})\}\}$")]
    private static partial Regex OuvertureBloc();

    [GeneratedRegex(@"\{\{([^{}]*)\}\}")]
    private static partial Regex Balise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();
}
