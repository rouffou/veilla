using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Stockage;

/// <summary>Configuration <c>Documents:Stockage</c>.</summary>
public sealed class OptionsStockage
{
    /// <summary><c>Local</c> (système de fichiers, développement et tests) ou <c>AzureBlob</c> (obligatoire, sans défaut).</summary>
    public string? Type { get; set; }

    /// <summary>Racine du stockage local.</summary>
    public string? Racine { get; set; }

    /// <summary>Crée les conteneurs absents (Azurite, tests) ; en Azure, Terraform les crée avec leur stratégie WORM.</summary>
    public bool CreerConteneurs { get; set; }

    public static string Conteneur(ZoneDocument zone) => $"documents-{zone.Code()}";
}

/// <summary>
/// Stockage local pour le développement et les tests : un fichier par document, créé sans jamais écraser un fichier
/// existant puis passé en lecture seule (émulation de l'écriture unique WORM, sans valeur probante).
/// URI : <c>local://documents-&lt;zone&gt;/&lt;chemin&gt;</c>.
/// </summary>
public sealed class StockageLocal(OptionsStockage options) : IStockageDocuments
{
    public const string Schema = "local";

    private readonly string _racine = Path.GetFullPath(options.Racine ?? Path.Combine(Path.GetTempPath(), "sepp-documents"));

    public async Task<string> EcrireAsync(ZoneDocument zone, string nom, byte[] contenu, IReadOnlyDictionary<string, string> metadonnees, CancellationToken cancellationToken)
    {
        var relatif = $"{OptionsStockage.Conteneur(zone)}/{nom}";
        var chemin = Chemin(relatif);
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        try
        {
            await using (var fichier = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await fichier.WriteAsync(contenu, cancellationToken);
            }
        }
        catch (IOException ex) when (File.Exists(chemin))
        {
            throw new InvalidOperationException($"Un document est déjà archivé sous {relatif} : écriture unique.", ex);
        }

        File.SetAttributes(chemin, FileAttributes.ReadOnly);
        return $"{Schema}://{relatif}";
    }

    public async Task<byte[]?> LireAsync(string uri, CancellationToken cancellationToken)
    {
        if (!uri.StartsWith($"{Schema}://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("URI de stockage étrangère au stockage local.");
        }

        var chemin = Chemin(uri[(Schema.Length + 3)..]);
        return File.Exists(chemin) ? await File.ReadAllBytesAsync(chemin, cancellationToken) : null;
    }

    /// <summary>Chemin physique d'un objet (tests d'intégrité).</summary>
    public string Chemin(string relatif)
    {
        var chemin = Path.GetFullPath(Path.Combine(_racine, relatif));
        return chemin.StartsWith(_racine + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? chemin
            : throw new InvalidOperationException("Chemin de stockage hors de la racine.");
    }
}

/// <summary>
/// Stockage Azure Blob (DOC-02, NF-21) : un conteneur par zone (<c>documents-standard</c>, <c>documents-medicale</c>,
/// <c>documents-psychosociale</c>) créé par Terraform (<c>infra/modules/storage</c>) avec une stratégie d'immutabilité
/// WORM à rétention temporelle. Accès par identité managée (<c>Documents:BlobEndpoint</c>, rôle Storage Blob Data
/// Contributor) ; une chaîne de connexion <c>ConnectionStrings:DocumentsBlob</c> sert à Azurite en local et en test.
/// Chaque objet est écrit avec la condition <c>If-None-Match: *</c> : un document archivé n'est jamais réécrit.
/// </summary>
public sealed class StockageAzureBlob : IStockageDocuments
{
    private readonly BlobServiceClient _service;
    private readonly bool _creerConteneurs;

    public StockageAzureBlob(BlobServiceClient service, OptionsStockage options)
    {
        _service = service;
        _creerConteneurs = options.CreerConteneurs;
    }

    public static BlobServiceClient Client(string? connexion, string? pointTerminaison) =>
        !string.IsNullOrWhiteSpace(connexion) ? new BlobServiceClient(connexion)
        : !string.IsNullOrWhiteSpace(pointTerminaison) ? new BlobServiceClient(new Uri(pointTerminaison), new DefaultAzureCredential())
        : throw new InvalidOperationException("Stockage AzureBlob : Documents:BlobEndpoint (identité managée) ou ConnectionStrings:DocumentsBlob requis.");

    public async Task<string> EcrireAsync(ZoneDocument zone, string nom, byte[] contenu, IReadOnlyDictionary<string, string> metadonnees, CancellationToken cancellationToken)
    {
        var conteneur = _service.GetBlobContainerClient(OptionsStockage.Conteneur(zone));
        if (_creerConteneurs)
        {
            await conteneur.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        var blob = conteneur.GetBlobClient(nom);
        try
        {
            await blob.UploadAsync(new BinaryData(contenu), new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                Metadata = metadonnees.ToDictionary(),
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
            }, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            throw new InvalidOperationException($"Un document est déjà archivé sous {conteneur.Name}/{nom} : écriture unique.", ex);
        }

        return blob.Uri.ToString();
    }

    public async Task<byte[]?> LireAsync(string uri, CancellationToken cancellationToken)
    {
        var parties = new BlobUriBuilder(new Uri(uri));
        var blob = _service.GetBlobContainerClient(parties.BlobContainerName).GetBlobClient(parties.BlobName);
        try
        {
            var reponse = await blob.DownloadContentAsync(cancellationToken);
            return reponse.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
