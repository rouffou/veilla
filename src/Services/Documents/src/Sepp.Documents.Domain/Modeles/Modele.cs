using System.Text.RegularExpressions;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Fusion;

namespace Sepp.Documents.Domain.Modeles;

/// <summary>Nature du modèle (DOC-01).</summary>
public enum TypeModele
{
    Courrier,
    Rapport,
    Formulaire,
}

/// <summary>Cycle de vie d'une version de modèle : brouillon → validé → publié, puis retiré par la publication suivante (DOC-01).</summary>
public enum StatutModele
{
    Brouillon,
    Valide,
    Publie,
    Retire,
}

/// <summary>
/// Version d'un modèle de document dans une langue (§15.3 <c>modele</c> : id, code, langue, version, contenu, statut).
/// Une ligne par couple (code, langue, version) ; une seule version publiée par code et par langue. Une version validée
/// ou publiée n'est plus modifiable : on en crée une nouvelle (DOC-01, traçabilité des documents produits).
/// </summary>
public sealed partial class Modele : AggregateRoot
{
    public const int LongueurMaximaleLibelle = 200;
    public const int LongueurMaximaleDescription = 1_000;

    private readonly List<ChampModele> _champs = [];

    private Modele()
    {
    }

    private Modele(Guid id, string code, Language langue, int version, TypeModele type, ZoneDocument zone, string libelle, string? description, string contenu)
        : base(id)
    {
        Code = code;
        Langue = langue;
        Version = version;
        Type = type;
        Zone = zone;
        Libelle = libelle;
        Description = description;
        Contenu = contenu;
        Statut = StatutModele.Brouillon;
    }

    public string Code { get; private set; } = string.Empty;

    public Language Langue { get; private set; }

    public int Version { get; private set; }

    public TypeModele Type { get; private set; }

    /// <summary>Zone des documents produits : détermine qui valide le modèle et la clé de chiffrement des documents.</summary>
    public ZoneDocument Zone { get; private set; }

    public string Libelle { get; private set; } = string.Empty;

    /// <summary>Note à l'intention des valideurs (par ex. « modèle de départ à valider »), jamais imprimée.</summary>
    public string? Description { get; private set; }

    public string Contenu { get; private set; } = string.Empty;

    public StatutModele Statut { get; private set; }

    public string? ValidePar { get; private set; }

    public DateTimeOffset? ValideLe { get; private set; }

    public string? PubliePar { get; private set; }

    public DateTimeOffset? PublieLe { get; private set; }

    public IReadOnlyList<ChampModele> Champs => _champs.AsReadOnly();

    public IReadOnlyList<DefinitionChamp> Definitions => _champs.Select(c => c.Definition).ToList();

    /// <summary>Crée la version 1 d'un modèle, en brouillon.</summary>
    public static Modele Creer(
        string code,
        Language langue,
        TypeModele type,
        ZoneDocument zone,
        string libelle,
        string? description,
        string contenu,
        IEnumerable<ChampDeclare> champs)
    {
        var modele = new Modele(NewId(), NormaliserCode(code), langue, 1, type, zone, Requis(libelle, "libellé", LongueurMaximaleLibelle),
            Facultatif(description, LongueurMaximaleDescription), contenu);
        modele.RemplacerChamps(champs);
        modele.VerifierContenu(contenu);
        return modele;
    }

    /// <summary>Nouvelle version en brouillon à partir de celle-ci (validée, publiée ou retirée), contenu et champs repris.</summary>
    public Modele NouvelleVersion(int numero)
    {
        if (numero <= Version)
        {
            throw new DomainException($"La nouvelle version du modèle {Code} doit être supérieure à {Version}.");
        }

        var copie = new Modele(NewId(), Code, Langue, numero, Type, Zone, Libelle, Description, Contenu);
        copie.RemplacerChamps(_champs.Select(c => new ChampDeclare(c.Nom, c.TypeChamp, c.Obligatoire, c.Libelle)));
        return copie;
    }

    /// <summary>Modifie un brouillon (DOC-01 : éditeur de modèles). Le contenu doit rester valide pour le moteur de fusion.</summary>
    public void ModifierBrouillon(string libelle, string? description, string contenu, IEnumerable<ChampDeclare> champs)
    {
        if (Statut != StatutModele.Brouillon)
        {
            throw new DomainException($"Seul un brouillon est modifiable (version {Version} du modèle {Code} : {Statut}). Créez une nouvelle version.");
        }

        Libelle = Requis(libelle, "libellé", LongueurMaximaleLibelle);
        Description = Facultatif(description, LongueurMaximaleDescription);
        RemplacerChamps(champs);
        VerifierContenu(contenu);
        Contenu = contenu;
    }

    /// <summary>Validation avant publication (DOC-01) ; le droit du valideur selon la zone est vérifié par le cas d'usage.</summary>
    public void Valider(string valideur, DateTimeOffset date)
    {
        if (Statut != StatutModele.Brouillon)
        {
            throw new DomainException($"Seul un brouillon peut être validé (statut actuel : {Statut}).");
        }

        VerifierContenu(Contenu);
        Statut = StatutModele.Valide;
        ValidePar = valideur;
        ValideLe = date;
    }

    /// <summary>Renvoie une version validée en brouillon (correction demandée avant publication).</summary>
    public void RenvoyerEnBrouillon()
    {
        if (Statut != StatutModele.Valide)
        {
            throw new DomainException("Seule une version validée et non publiée peut être renvoyée en brouillon.");
        }

        Statut = StatutModele.Brouillon;
        ValidePar = null;
        ValideLe = null;
    }

    /// <summary>Publication d'une version validée : elle devient la version utilisée pour générer les documents.</summary>
    public void Publier(string auteur, DateTimeOffset date)
    {
        if (Statut != StatutModele.Valide)
        {
            throw new DomainException("Un modèle doit être validé avant d'être publié (DOC-01).");
        }

        Statut = StatutModele.Publie;
        PubliePar = auteur;
        PublieLe = date;
    }

    /// <summary>Retrait de la version publiée, remplacée par une version plus récente.</summary>
    public void Retirer()
    {
        if (Statut != StatutModele.Publie)
        {
            throw new DomainException("Seule une version publiée peut être retirée.");
        }

        Statut = StatutModele.Retire;
    }

    /// <summary>Fusion des valeurs dans le contenu de cette version (champs déclarés uniquement).</summary>
    public DocumentFusionne Fusionner(IReadOnlyDictionary<string, ValeurChamp> valeurs) =>
        MoteurFusion.Fusionner(Contenu, Definitions, valeurs, Langue);

    public static string NormaliserCode(string code)
    {
        var normalise = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return CodeValide().IsMatch(normalise)
            ? normalise
            : throw new DomainException("Code de modèle invalide : majuscules, chiffres, « . », « - » et « _ », 100 caractères au plus.");
    }

    private void RemplacerChamps(IEnumerable<ChampDeclare> champs)
    {
        var liste = champs.ToList();
        var definitions = liste.Select(c => new DefinitionChamp(c.Nom, c.Type, c.Obligatoire)).ToList();
        var erreurs = MoteurFusion.Analyser("-", definitions);
        if (erreurs.Count > 0)
        {
            throw new DomainException(string.Join(" ", erreurs));
        }

        _champs.Clear();
        _champs.AddRange(liste.Select(c => new ChampModele(NewId(), c.Nom, c.Type, c.Obligatoire, Requis(c.Libelle, $"libellé du champ {c.Nom}", LongueurMaximaleLibelle))));
    }

    private void VerifierContenu(string contenu)
    {
        var erreurs = MoteurFusion.Analyser(contenu, Definitions);
        if (erreurs.Count > 0)
        {
            throw new DomainException(string.Join(" ", erreurs));
        }
    }

    private static string Requis(string? valeur, string nom, int longueur) =>
        string.IsNullOrWhiteSpace(valeur) || valeur.Trim().Length > longueur
            ? throw new DomainException($"Le {nom} est obligatoire ({longueur} caractères au plus).")
            : valeur.Trim();

    private static string? Facultatif(string? valeur, int longueur) =>
        string.IsNullOrWhiteSpace(valeur) ? null
        : valeur.Trim().Length > longueur ? throw new DomainException($"Texte limité à {longueur} caractères.")
        : valeur.Trim();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{0,99}$")]
    private static partial Regex CodeValide();
}

/// <summary>Champ demandé à la création ou à la modification d'un modèle.</summary>
public sealed record ChampDeclare(string Nom, TypeChamp Type, bool Obligatoire, string Libelle);

/// <summary>Champ de fusion typé d'une version de modèle (DOC-01).</summary>
public sealed class ChampModele : Entity
{
    private ChampModele()
    {
    }

    internal ChampModele(Guid id, string nom, TypeChamp type, bool obligatoire, string libelle) : base(id)
    {
        Nom = nom;
        TypeChamp = type;
        Obligatoire = obligatoire;
        Libelle = libelle;
    }

    public string Nom { get; private set; } = string.Empty;

    public TypeChamp TypeChamp { get; private set; }

    public bool Obligatoire { get; private set; }

    public string Libelle { get; private set; } = string.Empty;

    public DefinitionChamp Definition => new(Nom, TypeChamp, Obligatoire);
}
