using System.Text.RegularExpressions;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Domain.Documents;

/// <summary>Destinataire d'un document (contrat <c>documents.document-publie</c> : <c>TypeDestinataire</c>).</summary>
public enum TypeDestinataire
{
    /// <summary>L'employeur (affilié).</summary>
    Affilie,

    /// <summary>Le travailleur lui-même.</summary>
    Personne,

    /// <summary>Exemplaire versé au dossier (de santé, psychosocial…), jamais envoyé.</summary>
    Dossier,
}

public enum StatutDocument
{
    /// <summary>Généré, archivé, non encore mis à disposition du destinataire.</summary>
    Archive,

    /// <summary>Mis à disposition du destinataire (événement <c>DocumentPublie</c>).</summary>
    Publie,
}

public static class TypesDestinataire
{
    public static string Code(this TypeDestinataire type) => type switch
    {
        TypeDestinataire.Affilie => "affilie",
        TypeDestinataire.Personne => "personne",
        TypeDestinataire.Dossier => "dossier",
        _ => throw new DomainException($"Type de destinataire inconnu : {type}."),
    };
}

/// <summary>
/// Document généré et archivé à valeur probante (DOC-02, NF-21 ; §15.3 <c>document</c> : id, modele_id, zone,
/// service_proprietaire, objet_id, stockage_uri, empreinte, format, date). Le contenu n'est jamais en base : il est
/// chiffré avec la clé de sa zone puis écrit une fois pour toutes dans le stockage objet (WORM). L'empreinte SHA-256 du
/// PDF et celle de l'objet stocké permettent de vérifier l'intégrité à tout moment.
/// </summary>
public sealed partial class Document : AggregateRoot
{
    private readonly List<Signature> _signatures = [];

    private Document()
    {
    }

    private Document(Guid id) : base(id)
    {
    }

    public Guid ModeleId { get; private set; }

    public string CodeModele { get; private set; } = string.Empty;

    public int VersionModele { get; private set; }

    public Language Langue { get; private set; }

    /// <summary>Motif du choix de la langue (NF-41), par ex. « régime linguistique de l'affilié : néerlandais ».</summary>
    public string MotifLangue { get; private set; } = string.Empty;

    public ZoneDocument Zone { get; private set; }

    /// <summary>Service à l'origine de l'objet documenté (par ex. <c>surveillance-medicale</c>).</summary>
    public string ServiceProprietaire { get; private set; } = string.Empty;

    /// <summary>Type de l'objet documenté (par ex. <c>decision</c>).</summary>
    public string ObjetType { get; private set; } = string.Empty;

    public Guid ObjetId { get; private set; }

    public TypeDestinataire TypeDestinataire { get; private set; }

    public Guid DestinataireId { get; private set; }

    /// <summary>Exemplaire (par ex. <c>employeur</c>, <c>travailleur</c>, <c>dossier</c>) ; facultatif.</summary>
    public string? Exemplaire { get; private set; }

    /// <summary>Clé d'idempotence de la génération : un même objet ne produit qu'un document par exemplaire.</summary>
    public string? CleIdempotence { get; private set; }

    public string StockageUri { get; private set; } = string.Empty;

    /// <summary>Empreinte SHA-256 (hexadécimal) du PDF en clair.</summary>
    public string Empreinte { get; private set; } = string.Empty;

    /// <summary>Empreinte SHA-256 de l'objet stocké (chiffré) : détecte une altération sans déchiffrer.</summary>
    public string EmpreinteStockage { get; private set; } = string.Empty;

    /// <summary>Format produit, par ex. <c>PDF/A-1a</c>.</summary>
    public string Format { get; private set; } = string.Empty;

    public long Taille { get; private set; }

    /// <summary>Identifiant de la clé de chiffrement de la zone utilisée.</summary>
    public string CleChiffrement { get; private set; } = string.Empty;

    /// <summary>Date de génération (horodatage, NF-21).</summary>
    public DateTimeOffset Date { get; private set; }

    public string AutoriteHorodatage { get; private set; } = string.Empty;

    /// <summary>Jeton d'horodatage délivré par l'autorité (base64), lié à l'empreinte.</summary>
    public string JetonHorodatage { get; private set; } = string.Empty;

    public StatutDocument Statut { get; private set; }

    public DateTimeOffset? PublieLe { get; private set; }

    public IReadOnlyList<Signature> Signatures => _signatures.AsReadOnly();

    public static Guid NouvelIdentifiant() => NewId();

    public static Document Archiver(Guid id, DocumentArchive archive)
    {
        if (!Sha256().IsMatch(archive.Empreinte) || !Sha256().IsMatch(archive.EmpreinteStockage))
        {
            throw new DomainException("Empreinte SHA-256 invalide.");
        }

        if (archive.DestinataireId == Guid.Empty || archive.ObjetId == Guid.Empty)
        {
            throw new DomainException("Le destinataire et l'objet du document sont obligatoires.");
        }

        return new Document(id)
        {
            ModeleId = archive.ModeleId,
            CodeModele = archive.CodeModele,
            VersionModele = archive.VersionModele,
            Langue = archive.Langue,
            MotifLangue = archive.MotifLangue,
            Zone = archive.Zone,
            ServiceProprietaire = Texte(archive.ServiceProprietaire, "service propriétaire", 100),
            ObjetType = Texte(archive.ObjetType, "type d'objet", 100),
            ObjetId = archive.ObjetId,
            TypeDestinataire = archive.TypeDestinataire,
            DestinataireId = archive.DestinataireId,
            Exemplaire = string.IsNullOrWhiteSpace(archive.Exemplaire) ? null : Texte(archive.Exemplaire, "exemplaire", 50),
            CleIdempotence = string.IsNullOrWhiteSpace(archive.CleIdempotence) ? null : Texte(archive.CleIdempotence, "clé d'idempotence", 200),
            StockageUri = archive.StockageUri,
            Empreinte = archive.Empreinte,
            EmpreinteStockage = archive.EmpreinteStockage,
            Format = archive.Format,
            Taille = archive.Taille,
            CleChiffrement = archive.CleChiffrement,
            Date = archive.Date,
            AutoriteHorodatage = archive.AutoriteHorodatage,
            JetonHorodatage = archive.JetonHorodatage,
            Statut = StatutDocument.Archive,
        };
    }

    /// <summary>Mise à disposition du destinataire ; un exemplaire « dossier » ne s'envoie pas.</summary>
    public void Publier(DateTimeOffset date)
    {
        if (TypeDestinataire == TypeDestinataire.Dossier)
        {
            throw new DomainException("L'exemplaire versé au dossier n'est pas publié vers un destinataire.");
        }

        if (Statut == StatutDocument.Publie)
        {
            throw new DomainException("Le document est déjà publié.");
        }

        Statut = StatutDocument.Publie;
        PublieLe = date;
    }

    /// <summary>Ajoute une signature électronique (preuve détachée : le PDF archivé n'est jamais réécrit, WORM).</summary>
    public Signature Signer(string signataireId, string type, DateTimeOffset date, string preuve)
    {
        if (_signatures.Any(s => s.SignataireId == signataireId))
        {
            throw new DomainException("Ce signataire a déjà signé le document.");
        }

        var signature = new Signature(NewId(), Texte(signataireId, "signataire", 100), Texte(type, "type de signature", 50), date,
            Texte(preuve, "preuve", 8_000), Empreinte);
        _signatures.Add(signature);
        return signature;
    }

    private static string Texte(string? valeur, string nom, int longueur) =>
        string.IsNullOrWhiteSpace(valeur) || valeur.Trim().Length > longueur
            ? throw new DomainException($"Le {nom} est obligatoire ({longueur} caractères au plus).")
            : valeur.Trim();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}

/// <summary>Données d'un document après rendu, chiffrement, stockage et horodatage.</summary>
public sealed record DocumentArchive(
    Guid ModeleId,
    string CodeModele,
    int VersionModele,
    Language Langue,
    string MotifLangue,
    ZoneDocument Zone,
    string ServiceProprietaire,
    string ObjetType,
    Guid ObjetId,
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    string? Exemplaire,
    string? CleIdempotence,
    string StockageUri,
    string Empreinte,
    string EmpreinteStockage,
    string Format,
    long Taille,
    string CleChiffrement,
    DateTimeOffset Date,
    string AutoriteHorodatage,
    string JetonHorodatage);

/// <summary>
/// Signature électronique d'un document (§15.3 <c>signature</c> : id, document_id, signataire_id, type, date, preuve).
/// La preuve est délivrée par le prestataire de signature qualifiée et porte sur l'empreinte du document.
/// </summary>
public sealed class Signature : Entity
{
    private Signature()
    {
    }

    internal Signature(Guid id, string signataireId, string type, DateTimeOffset date, string preuve, string empreinteSignee) : base(id)
    {
        SignataireId = signataireId;
        Type = type;
        Date = date;
        Preuve = preuve;
        EmpreinteSignee = empreinteSignee;
    }

    public string SignataireId { get; private set; } = string.Empty;

    /// <summary>Par ex. <c>qualifiee-simulee</c> (simulateur) ou <c>qualifiee-eid</c>, <c>qualifiee-itsme</c>.</summary>
    public string Type { get; private set; } = string.Empty;

    public DateTimeOffset Date { get; private set; }

    public string Preuve { get; private set; } = string.Empty;

    public string EmpreinteSignee { get; private set; } = string.Empty;
}
