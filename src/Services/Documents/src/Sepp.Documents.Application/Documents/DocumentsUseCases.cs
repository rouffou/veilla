using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Application.Securite;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Application.Documents;

public sealed record SignatureDto(Guid Id, string SignataireId, string Type, DateTimeOffset Date, string Preuve, string EmpreinteSignee);

public sealed record DocumentDto(
    Guid Id,
    Guid ModeleId,
    string CodeModele,
    int VersionModele,
    Language Langue,
    string MotifLangue,
    string Zone,
    string ServiceProprietaire,
    string ObjetType,
    Guid ObjetId,
    string TypeDestinataire,
    Guid DestinataireId,
    string? Exemplaire,
    string Format,
    long Taille,
    string Empreinte,
    DateTimeOffset Date,
    string AutoriteHorodatage,
    StatutDocument Statut,
    DateTimeOffset? PublieLe,
    IReadOnlyList<SignatureDto> Signatures)
{
    public static DocumentDto From(Document d) => new(
        d.Id, d.ModeleId, d.CodeModele, d.VersionModele, d.Langue, d.MotifLangue, d.Zone.Code(), d.ServiceProprietaire, d.ObjetType, d.ObjetId,
        d.TypeDestinataire.Code(), d.DestinataireId, d.Exemplaire, d.Format, d.Taille, d.Empreinte, d.Date, d.AutoriteHorodatage, d.Statut, d.PublieLe,
        d.Signatures.Select(s => new SignatureDto(s.Id, s.SignataireId, s.Type, s.Date, s.Preuve, s.EmpreinteSignee)).ToList());
}

/// <summary>Génération à la demande (courriers, rapports, formulaires) ; langue selon NF-41.</summary>
/// <param name="Langue">Langue imposée (facultative).</param>
/// <param name="Regime">Régime linguistique de l'affilié (facultatif : lu auprès du service Affiliés sinon).</param>
/// <param name="ChoixTravailleur">Langue choisie par le travailleur (facultative : lue auprès du service Personnes sinon).</param>
/// <param name="Publier">Mettre immédiatement le document à disposition de son destinataire.</param>
public sealed record GenererDocument(
    string CodeModele,
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    string ServiceProprietaire,
    string ObjetType,
    Guid ObjetId,
    IReadOnlyDictionary<string, ValeurChamp> Valeurs,
    Language? Langue,
    RegimeLinguistique? Regime,
    Language? ChoixTravailleur,
    Guid? AffilieId,
    Guid? PersonneId,
    bool Publier);

public sealed class GenererDocumentHandler(
    GenerateurDocuments generateur,
    IModeleRepository modeles,
    AccesDocuments acces,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox) : ICommandHandler<GenererDocument, DocumentDto>
{
    public async Task<Result<DocumentDto>> HandleAsync(GenererDocument command, CancellationToken cancellationToken)
    {
        var versions = await modeles.ListAsync(command.CodeModele.Trim().ToUpperInvariant(), cancellationToken);
        if (versions.Count == 0)
        {
            return acces.EstExterne
                ? Error.Forbidden("documents.interdit", "Droits insuffisants.")
                : Error.NotFound("modele.inconnu", $"Modèle {command.CodeModele} inconnu.");
        }

        if (acces.VerifierGeneration(versions[0].Zone) is { } refus)
        {
            return refus;
        }

        if (command.Publier && command.TypeDestinataire == TypeDestinataire.Dossier)
        {
            return Error.Validation("document.dossier-non-publie", "Un exemplaire versé au dossier n'est pas publié.");
        }

        Result<DocumentGenere> resultat;
        try
        {
            resultat = await generateur.GenererAsync(new DemandeGeneration(
                command.CodeModele, command.TypeDestinataire, command.DestinataireId, command.ServiceProprietaire, command.ObjetType, command.ObjetId,
                _ => command.Valeurs, new ContexteLinguistique(command.Langue, command.Regime, null, command.ChoixTravailleur),
                command.AffilieId ?? (command.TypeDestinataire == TypeDestinataire.Affilie ? command.DestinataireId : null),
                command.PersonneId ?? (command.TypeDestinataire != TypeDestinataire.Affilie ? command.DestinataireId : null)), cancellationToken);
        }
        catch (DomainException ex)
        {
            return Error.Validation("document.invalide", ex.Message);
        }

        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        if (command.Publier)
        {
            generateur.Publier(resultat.Value.Document, outbox);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DocumentDto.From(resultat.Value.Document);
    }
}

public sealed record PublierDocument(Guid Id);

public sealed class PublierDocumentHandler(IDocumentRepository documents, GenerateurDocuments generateur, AccesDocuments acces, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<PublierDocument, Unit>
{
    public async Task<Result<Unit>> HandleAsync(PublierDocument command, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(command.Id, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("document.inconnu", "Document inconnu.");
        }

        if (acces.VerifierGeneration(document.Zone) is { } refus)
        {
            return refus;
        }

        try
        {
            generateur.Publier(document, outbox);
        }
        catch (DomainException ex)
        {
            return Error.Conflict("document.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record ObtenirDocument(Guid Id);

public sealed class ObtenirDocumentHandler(IDocumentRepository documents, AccesDocuments acces) : IQueryHandler<ObtenirDocument, DocumentDto>
{
    public async Task<Result<DocumentDto>> HandleAsync(ObtenirDocument query, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(query.Id, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("document.inconnu", "Document inconnu.");
        }

        return acces.VerifierLecture(document) is { } refus ? refus : DocumentDto.From(document);
    }
}

/// <summary>Documents d'un objet (par ex. une décision) ou d'un destinataire, limités aux zones lisibles par l'utilisateur.</summary>
public sealed record ListerDocuments(string? ObjetType, Guid? ObjetId, TypeDestinataire? TypeDestinataire, Guid? DestinataireId);

public sealed class ListerDocumentsHandler(IDocumentRepository documents, AccesDocuments acces, ICurrentUser currentUser)
    : IQueryHandler<ListerDocuments, IReadOnlyList<DocumentDto>>
{
    public async Task<Result<IReadOnlyList<DocumentDto>>> HandleAsync(ListerDocuments query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsLire))
        {
            return Error.Forbidden("documents.interdit", "Droits insuffisants sur les documents.");
        }

        if (query.ObjetId is null && query.DestinataireId is null)
        {
            return Error.Validation("documents.filtre", "Indiquez un objet (objetType, objetId) ou un destinataire (typeDestinataire, destinataireId).");
        }

        var liste = await documents.ListAsync(new FiltreDocuments(query.ObjetType, query.ObjetId, query.TypeDestinataire, query.DestinataireId), cancellationToken);
        return liste.Where(d => acces.VerifierLecture(d) is null).OrderByDescending(d => d.Date).Select(DocumentDto.From).ToList();
    }
}

/// <summary>Contenu PDF d'un document : déchiffré avec la clé de sa zone après contrôle des droits et trace d'audit (NF-04).</summary>
public sealed record LireContenuDocument(Guid Id, string? Motif);

public sealed record ContenuDocumentDto(string NomFichier, byte[] Contenu, string Empreinte);

public sealed class LireContenuDocumentHandler(
    IDocumentRepository documents,
    IStockageDocuments stockage,
    IChiffrementDocuments chiffrement,
    IJournalAcces journal,
    AccesDocuments acces) : IQueryHandler<LireContenuDocument, ContenuDocumentDto>
{
    public async Task<Result<ContenuDocumentDto>> HandleAsync(LireContenuDocument query, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(query.Id, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("document.inconnu", "Document inconnu.");
        }

        if (acces.VerifierLecture(document) is { } refus)
        {
            return refus;
        }

        if (MotifAcces.Verifier(query.Motif, brisDeGlace: false) is { } motifInvalide)
        {
            return motifInvalide;
        }

        var stocke = await stockage.LireAsync(document.StockageUri, cancellationToken);
        if (stocke is null || GenerateurDocuments.Empreinte(stocke) != document.EmpreinteStockage)
        {
            return Error.Conflict("document.altere", "Le contenu archivé est introuvable ou altéré : lancez la vérification d'intégrité.");
        }

        byte[] clair;
        try
        {
            clair = chiffrement.Dechiffrer(document.Zone, document.Id, stocke);
        }
        catch (ContenuAltereException)
        {
            return Error.Conflict("document.altere", "Le contenu archivé est altéré : lancez la vérification d'intégrité.");
        }

        if (GenerateurDocuments.Empreinte(clair) != document.Empreinte)
        {
            return Error.Conflict("document.altere", "L'empreinte du contenu ne correspond plus à celle de l'archivage.");
        }

        // La trace est validée avant de rendre le contenu : si elle échoue, la lecture échoue.
        await journal.EnregistrerLectureAsync(document.Zone, document.Id, query.Motif, cancellationToken);
        return new ContenuDocumentDto($"{document.CodeModele.ToUpperInvariant()}-{document.Id}.pdf", clair, document.Empreinte);
    }
}

/// <summary>NF-21 : vérification d'intégrité d'un document archivé (empreintes, déchiffrement authentifié).</summary>
public sealed record VerifierIntegriteDocument(Guid Id);

public sealed record IntegriteDto(
    Guid DocumentId,
    bool Integre,
    bool ObjetPresent,
    string EmpreinteStockageAttendue,
    string? EmpreinteStockageCalculee,
    bool DechiffrementAuthentifie,
    string EmpreinteAttendue,
    string? EmpreinteCalculee,
    DateTimeOffset HorodatageArchivage,
    string AutoriteHorodatage,
    DateTimeOffset VerifieLe,
    string Conclusion);

public sealed class VerifierIntegriteDocumentHandler(
    IDocumentRepository documents,
    IStockageDocuments stockage,
    IChiffrementDocuments chiffrement,
    AccesDocuments acces,
    TimeProvider horloge) : IQueryHandler<VerifierIntegriteDocument, IntegriteDto>
{
    public async Task<Result<IntegriteDto>> HandleAsync(VerifierIntegriteDocument query, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(query.Id, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("document.inconnu", "Document inconnu.");
        }

        if (acces.VerifierIntegrite(document) is { } refus)
        {
            return refus;
        }

        var stocke = await stockage.LireAsync(document.StockageUri, cancellationToken);
        string? empreinteStockage = stocke is null ? null : GenerateurDocuments.Empreinte(stocke);
        string? empreinte = null;
        var authentifie = false;
        if (stocke is not null)
        {
            try
            {
                // Déchiffrement en mémoire uniquement : seule l'empreinte sort de la vérification.
                empreinte = GenerateurDocuments.Empreinte(chiffrement.Dechiffrer(document.Zone, document.Id, stocke));
                authentifie = true;
            }
            catch (ContenuAltereException)
            {
                authentifie = false;
            }
        }

        var integre = stocke is not null && empreinteStockage == document.EmpreinteStockage && authentifie && empreinte == document.Empreinte;
        var conclusion = stocke is null ? "Objet archivé introuvable."
            : integre ? "Document intègre : empreintes identiques à celles de l'archivage."
            : "Document altéré : le contenu stocké ne correspond plus à celui archivé.";
        return new IntegriteDto(document.Id, integre, stocke is not null, document.EmpreinteStockage, empreinteStockage, authentifie, document.Empreinte,
            empreinte, document.Date, document.AutoriteHorodatage, horloge.GetUtcNow(), conclusion);
    }
}

/// <summary>DOC-02 : signature électronique qualifiée du document par l'utilisateur courant (preuve détachée).</summary>
public sealed record SignerDocument(Guid Id);

public sealed class SignerDocumentHandler(
    IDocumentRepository documents,
    ISignatureQualifiee signature,
    IJournalAcces journal,
    AccesDocuments acces,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : ICommandHandler<SignerDocument, SignatureDto>
{
    public async Task<Result<SignatureDto>> HandleAsync(SignerDocument command, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(command.Id, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("document.inconnu", "Document inconnu.");
        }

        if (acces.VerifierGeneration(document.Zone) is { } refus)
        {
            return refus;
        }

        if (document.Signatures.Any(s => s.SignataireId == currentUser.UserId))
        {
            return Error.Conflict("document.deja-signe", "Vous avez déjà signé ce document.");
        }

        var preuve = await signature.SignerAsync(document.Id, document.Empreinte, currentUser.UserId, cancellationToken);
        var signee = document.Signer(currentUser.UserId, preuve.Type, preuve.Date, preuve.Preuve);
        journal.Enregistrer(ActionAudit.Modification, document.Zone, document.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SignatureDto(signee.Id, signee.SignataireId, signee.Type, signee.Date, signee.Preuve, signee.EmpreinteSignee);
    }
}
