using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Application;

public interface IModeleRepository
{
    Task<Modele?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Version publiée d'un modèle dans une langue, s'il y en a une.</summary>
    Task<Modele?> PublieAsync(string code, Language langue, CancellationToken cancellationToken);

    /// <summary>Toutes les versions d'un code (toutes langues), ou de tous les modèles si <paramref name="code"/> est nul.</summary>
    Task<IReadOnlyList<Modele>> ListAsync(string? code, CancellationToken cancellationToken);

    void Add(Modele modele);
}

/// <summary>Filtre de recherche des documents : par objet documenté ou par destinataire.</summary>
public sealed record FiltreDocuments(string? ObjetType, Guid? ObjetId, TypeDestinataire? TypeDestinataire, Guid? DestinataireId);

public interface IDocumentRepository
{
    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Document?> ParCleIdempotenceAsync(string cle, CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> ListAsync(FiltreDocuments filtre, CancellationToken cancellationToken);

    void Add(Document document);
}

/// <summary>Métadonnées inscrites dans le PDF (titre, langue, auteur, date, référence du document).</summary>
public sealed record MetadonneesPdf(string Titre, Language Langue, string Auteur, DateTimeOffset Date, string Reference);

/// <summary>PDF produit et son format (par ex. <c>PDF/A-1a</c>).</summary>
public sealed record RenduPdf(byte[] Contenu, string Format);

/// <summary>Rendu PDF/A d'un document fusionné (NF-21).</summary>
public interface IRenduPdf
{
    RenduPdf Rendre(DocumentFusionne document, MetadonneesPdf metadonnees);
}

/// <summary>
/// Zones servies par cette instance du service Documents (décision #356 : une instance par zone). Hors développement et
/// test, une instance sert exactement une zone, celle de son environnement (<c>SEPP__ZONE</c>) ; en local, une instance
/// peut servir les trois. Une instance refuse de générer ou de gérer un modèle d'une zone qu'elle ne sert pas
/// (<c>422 documents.zone-non-servie</c>) et ne trouve pas les documents d'une autre zone (404).
/// </summary>
public interface IZonesServies
{
    IReadOnlyCollection<ZoneDocument> Zones { get; }

    bool Sert(ZoneDocument zone);
}

/// <summary>Contenu chiffré et identifiant de la clé racine (kid versionné) qui a enveloppé sa clé de données.</summary>
public sealed record ContenuChiffre(byte[] Octets, string CleId);

/// <summary>
/// Chiffrement authentifié du contenu par zone (§14.3, ARC-45, ADR 0009) : chaque document est chiffré avec sa propre clé
/// de données, enveloppée par la clé racine de sa zone ; le contenu est lié à son document, à sa zone et à la version de
/// la clé racine, si bien qu'un objet déplacé ou altéré ne se déchiffre pas.
/// </summary>
public interface IChiffrementDocuments
{
    Task<ContenuChiffre> ChiffrerAsync(ZoneDocument zone, Guid documentId, byte[] clair, CancellationToken cancellationToken);

    /// <exception cref="ContenuAltereException">Contenu altéré, tronqué ou chiffré pour un autre document ou une autre zone.</exception>
    Task<byte[]> DechiffrerAsync(ZoneDocument zone, Guid documentId, byte[] chiffre, CancellationToken cancellationToken);
}

/// <summary>Clé de données enveloppée par la clé racine d'une zone, avec l'identifiant versionné (kid) de cette clé racine.</summary>
public sealed record CleEnveloppee(string Kid, byte[] Octets);

/// <summary>
/// Enveloppe des clés de données par la clé racine <c>documents-kek</c> de la zone (ARC-44, ADR 0009) : la clé racine ne
/// quitte jamais le HSM ; seules les clés de données enveloppées sont stockées, avec la version de la clé racine pour
/// pouvoir les déballer après une rotation.
/// </summary>
public interface IEnveloppeCles
{
    /// <summary>Enveloppe la clé de données avec la version courante de la clé racine de la zone.</summary>
    Task<CleEnveloppee> EnvelopperAsync(ZoneDocument zone, ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken);

    /// <summary>Déballe une clé de données avec la version <paramref name="kid"/> de la clé racine de la zone.</summary>
    /// <exception cref="ContenuAltereException">Kid étranger à la clé racine de la zone, version inconnue ou clé enveloppée invalide.</exception>
    Task<byte[]> DesenvelopperAsync(ZoneDocument zone, string kid, byte[] cleEnveloppee, CancellationToken cancellationToken);
}

/// <summary>Le contenu stocké ne correspond plus à ce qui a été archivé.</summary>
public sealed class ContenuAltereException : Exception
{
    public ContenuAltereException(string message) : base(message)
    {
    }

    public ContenuAltereException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ContenuAltereException()
    {
    }
}

/// <summary>
/// Stockage objet à valeur probante (DOC-02, NF-21) : un objet est écrit une seule fois et n'est jamais réécrit
/// (immutabilité WORM en Azure, fichier en lecture seule en développement).
/// </summary>
public interface IStockageDocuments
{
    /// <returns>URI de l'objet stocké.</returns>
    /// <exception cref="InvalidOperationException">Un objet existe déjà sous ce nom.</exception>
    Task<string> EcrireAsync(ZoneDocument zone, string nom, byte[] contenu, IReadOnlyDictionary<string, string> metadonnees, CancellationToken cancellationToken);

    /// <returns>Le contenu, ou <c>null</c> si l'objet est introuvable.</returns>
    Task<byte[]?> LireAsync(string uri, CancellationToken cancellationToken);
}

/// <summary>Horodatage d'une empreinte par une autorité (NF-21).</summary>
public sealed record Horodatage(DateTimeOffset Date, string Autorite, string Jeton);

public interface IServiceHorodatage
{
    Task<Horodatage> HorodaterAsync(string empreinte, CancellationToken cancellationToken);
}

/// <summary>Preuve de signature délivrée par le prestataire (DOC-02).</summary>
public sealed record PreuveSignature(string Type, DateTimeOffset Date, string Preuve);

/// <summary>Signature électronique qualifiée (eID, itsme) ; simulateur en dehors de la production.</summary>
public interface ISignatureQualifiee
{
    Task<PreuveSignature> SignerAsync(Guid documentId, string empreinte, string signataireId, CancellationToken cancellationToken);
}

/// <summary>Régime linguistique de l'affilié et langue du travailleur, lus auprès des services propriétaires (NF-41).</summary>
public sealed record InformationsLinguistiques(RegimeLinguistique? Regime, Language? LangueAffilie, Language? LangueTravailleur);

public interface ISourceLinguistique
{
    Task<InformationsLinguistiques> ObtenirAsync(Guid? affilieId, Guid? personneId, CancellationToken cancellationToken);
}

/// <summary>
/// Journal d'audit des accès aux documents (NF-04) dans la zone de chaque document : le service Documents sert des
/// documents des trois zones, la zone de la trace est donc celle du document et non celle du service.
/// </summary>
public interface IJournalAcces
{
    void Enregistrer(ActionAudit action, ZoneDocument zone, Guid documentId, string? motif = null);

    Task EnregistrerLectureAsync(ZoneDocument zone, Guid documentId, string? motif, CancellationToken cancellationToken);
}

/// <summary>Périmètre d'un utilisateur externe (ADR 0005) : affiliés du claim <c>affilie_id</c>, personne du claim <c>personne_id</c>.</summary>
public interface IPerimetreExterne
{
    IReadOnlyCollection<Guid> Affilies { get; }

    Guid? PersonneId { get; }
}
