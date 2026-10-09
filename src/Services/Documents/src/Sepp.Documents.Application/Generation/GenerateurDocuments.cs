using System.Security.Cryptography;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Documents;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Application.Generation;

/// <summary>Demande de génération d'un document à partir d'un modèle publié.</summary>
/// <param name="Valeurs">Valeurs des champs, selon la langue retenue (libellés traduits).</param>
/// <param name="Langue">Langue demandée, régime de l'affilié ou choix du travailleur (NF-41) ; complétés au besoin auprès des services propriétaires.</param>
/// <param name="AffilieId">Affilié concerné (régime linguistique).</param>
/// <param name="PersonneId">Travailleur concerné (langue choisie).</param>
public sealed record DemandeGeneration(
    string CodeModele,
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    string ServiceProprietaire,
    string ObjetType,
    Guid ObjetId,
    Func<Language, IReadOnlyDictionary<string, ValeurChamp>> Valeurs,
    ContexteLinguistique Langue,
    Guid? AffilieId,
    Guid? PersonneId,
    string? Exemplaire = null,
    string? CleIdempotence = null);

/// <summary>Document généré, et s'il existait déjà pour la même clé d'idempotence.</summary>
public sealed record DocumentGenere(Document Document, bool Existant);

/// <summary>
/// Génération et archivage probant d'un document (DOC-01, DOC-02, NF-21) : fusion du modèle publié dans la langue retenue
/// (NF-41), rendu PDF/A, empreinte SHA-256, chiffrement avec la clé de la zone, écriture unique dans le stockage objet,
/// horodatage, trace d'audit. Les droits de l'utilisateur sont vérifiés par l'appelant ; l'unité de travail est validée
/// par l'appelant (plusieurs exemplaires dans une seule transaction).
/// </summary>
public sealed class GenerateurDocuments(
    IModeleRepository modeles,
    IDocumentRepository documents,
    IRenduPdf rendu,
    IChiffrementDocuments chiffrement,
    IStockageDocuments stockage,
    IServiceHorodatage horodatage,
    ISourceLinguistique sourceLinguistique,
    IJournalAcces journal,
    TimeProvider horloge)
{
    public const string Auteur = "SEPP — service Documents";

    public async Task<Result<DocumentGenere>> GenererAsync(DemandeGeneration demande, CancellationToken cancellationToken)
    {
        if (demande.CleIdempotence is { } cle && await documents.ParCleIdempotenceAsync(cle, cancellationToken) is { } existant)
        {
            return new DocumentGenere(existant, true);
        }

        var choix = RegleLinguistique.Determiner(await CompleterAsync(demande, cancellationToken), demande.TypeDestinataire);
        var modele = await modeles.PublieAsync(demande.CodeModele.Trim().ToUpperInvariant(), choix.Langue, cancellationToken);
        if (modele is null)
        {
            return Error.Validation("documents.modele-non-publie",
                $"Aucune version publiée du modèle {demande.CodeModele} en {choix.Langue} ({choix.Motif}).");
        }

        DocumentFusionne fusion;
        try
        {
            fusion = modele.Fusionner(demande.Valeurs(choix.Langue));
        }
        catch (DomainException ex)
        {
            return Error.Validation("fusion.invalide", ex.Message);
        }

        var id = Document.NouvelIdentifiant();
        var date = horloge.GetUtcNow();
        var pdf = rendu.Rendre(fusion, new MetadonneesPdf(modele.Libelle, choix.Langue, Auteur, date, id.ToString()));
        var empreinte = Empreinte(pdf.Contenu);
        var chiffre = chiffrement.Chiffrer(modele.Zone, id, pdf.Contenu);
        var empreinteStockage = Empreinte(chiffre.Octets);
        var jeton = await horodatage.HorodaterAsync(empreinte, cancellationToken);
        var uri = await stockage.EcrireAsync(modele.Zone, $"{date:yyyy}/{date:MM}/{id}.pdf.enc", chiffre.Octets,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["document_id"] = id.ToString(),
                ["empreinte_sha256"] = empreinte,
                ["empreinte_stockage_sha256"] = empreinteStockage,
                ["cle"] = chiffre.CleId,
                ["format"] = pdf.Format,
            },
            cancellationToken);

        var document = Document.Archiver(id, new DocumentArchive(
            modele.Id, modele.Code, modele.Version, choix.Langue, choix.Motif, modele.Zone, demande.ServiceProprietaire, demande.ObjetType,
            demande.ObjetId, demande.TypeDestinataire, demande.DestinataireId, demande.Exemplaire, demande.CleIdempotence, uri, empreinte,
            empreinteStockage, pdf.Format, pdf.Contenu.LongLength, chiffre.CleId, jeton.Date, jeton.Autorite, jeton.Jeton));
        documents.Add(document);
        journal.Enregistrer(ActionAudit.Creation, modele.Zone, id);
        return new DocumentGenere(document, false);
    }

    /// <summary>
    /// Met le document à disposition de son destinataire et publie <see cref="DocumentPublie"/> (transmis à Communications).
    /// L'objet métier du document (<c>ObjetType</c>, <c>ObjetId</c> : par exemple <c>decision</c> et l'identifiant de la
    /// décision pour le formulaire d'évaluation de santé) accompagne la publication, pour que le processus de reprise du
    /// service Obligations corrèle le formulaire à sa décision (saga « examen de reprise », ARC-33). Ce sont des
    /// identifiants et un code, jamais un contenu (ARC-06).
    /// </summary>
    public void Publier(Document document, IIntegrationEventOutbox outbox)
    {
        document.Publier(horloge.GetUtcNow());
        outbox.Add(new DocumentPublie(
            document.Id, document.Zone.Code(), document.TypeDestinataire.Code(), document.DestinataireId, document.CodeModele, document.ObjetType, document.ObjetId));
    }

    public static string Empreinte(byte[] contenu) => Convert.ToHexStringLower(SHA256.HashData(contenu));

    private async Task<ContexteLinguistique> CompleterAsync(DemandeGeneration demande, CancellationToken cancellationToken)
    {
        var contexte = demande.Langue;
        if (contexte.LangueDemandee is not null || (contexte.Regime is not null && (contexte.ChoixTravailleur is not null || demande.PersonneId is null)))
        {
            return contexte;
        }

        var infos = await sourceLinguistique.ObtenirAsync(demande.AffilieId, demande.PersonneId, cancellationToken);
        return contexte with
        {
            Regime = contexte.Regime ?? infos.Regime,
            LangueAffilie = contexte.LangueAffilie ?? infos.LangueAffilie,
            ChoixTravailleur = contexte.ChoixTravailleur ?? infos.LangueTravailleur,
        };
    }
}
