using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Chiffrement;

/// <summary>
/// Opérations sur la clé racine dans Azure Key Vault ou Managed HSM (<c>wrapKey</c>, <c>unwrapKey</c>, RSA-OAEP-256).
/// Abstraites pour tester <see cref="EnveloppeKeyVault"/> sans appel Azure.
/// </summary>
public interface IOperationsCleRacine
{
    /// <summary>Enveloppe avec la version courante de la clé désignée par son URI sans version.</summary>
    /// <returns>Le kid versionné renvoyé par le coffre et la clé enveloppée.</returns>
    Task<CleEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken);

    /// <summary>Déballe avec la version désignée par le kid versionné.</summary>
    /// <exception cref="ContenuAltereException">Clé enveloppée refusée par le coffre ou version inconnue.</exception>
    Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken);
}

/// <summary>
/// Enveloppe des clés de données par la clé racine <c>documents-kek</c> de la zone (ARC-44, ADR 0009) : clé RSA-HSM 3072
/// non exportable dans le Key Vault Premium ou le Managed HSM des zones sensibles, clé RSA dans le coffre standard.
/// <c>Documents:Enveloppe:CleKek</c> donne l'URI de la clé <b>sans version</b> : l'enveloppe utilise la version courante
/// (rotation automatique) et l'archive conserve le kid versionné, qui reste utilisable pour déballer après rotation.
/// </summary>
/// <remarks>
/// Le kid lu dans une archive n'est pas authentifié avant le déballage : il est confronté à la clé racine configurée pour
/// la zone (même coffre, même nom, version alphanumérique) avant tout appel, pour qu'une archive altérée ne puisse pas
/// diriger l'identité managée vers un autre coffre, une autre clé ou une autre zone.
/// </remarks>
public sealed partial class EnveloppeKeyVault : IEnveloppeCles
{
    private readonly Dictionary<ZoneDocument, string> _prefixes;
    private readonly Dictionary<ZoneDocument, Uri> _cles;
    private readonly IOperationsCleRacine _operations;

    public EnveloppeKeyVault(IReadOnlyDictionary<ZoneDocument, Uri> cles, IOperationsCleRacine operations)
    {
        _cles = cles.ToDictionary(c => c.Key, c => LireUriCle(c.Value.AbsoluteUri, c.Key));
        _prefixes = _cles.ToDictionary(c => c.Key, c => $"{c.Value.AbsoluteUri.TrimEnd('/')}/");
        _operations = operations;
        var doublon = _cles.GroupBy(c => c.Value.AbsoluteUri.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null)
        {
            throw new InvalidOperationException($"La même clé racine est configurée pour les zones {string.Join(", ", doublon.Select(c => c.Key.Code()))} : une clé par zone (§14.3).");
        }
    }

    /// <summary>Contrôle l'URI de la clé racine : HTTPS, forme <c>https://&lt;coffre&gt;/keys/&lt;nom&gt;</c>, sans version.</summary>
    public static Uri LireUriCle(string? valeur, ZoneDocument zone)
    {
        if (!Uri.TryCreate(valeur, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)
            || !KeyVaultKeyIdentifier.TryCreate(uri, out var id) || string.IsNullOrEmpty(id.Name))
        {
            throw new InvalidOperationException(
                $"Documents:Enveloppe:CleKek de la zone {zone.Code()} doit être l'URI HTTPS d'une clé Key Vault ou Managed HSM (https://<coffre>/keys/<nom>).");
        }

        if (!string.IsNullOrEmpty(id.Version))
        {
            throw new InvalidOperationException(
                $"Documents:Enveloppe:CleKek de la zone {zone.Code()} doit désigner la clé sans version : la version courante enveloppe, le kid versionné est conservé.");
        }

        return new Uri($"{id.VaultUri.AbsoluteUri.TrimEnd('/')}/keys/{id.Name}");
    }

    public async Task<CleEnveloppee> EnvelopperAsync(ZoneDocument zone, ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken)
    {
        var copie = cleDonnees.ToArray();
        try
        {
            var resultat = await _operations.EnvelopperAsync(Cle(zone), copie, cancellationToken);
            var kid = KidCanonique(zone, resultat.Kid)
                ?? throw new InvalidOperationException($"Le coffre a renvoyé un kid étranger à la clé racine de la zone {zone.Code()}.");
            return new CleEnveloppee(kid, resultat.Octets);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copie);
        }
    }

    public Task<byte[]> DesenvelopperAsync(ZoneDocument zone, string kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        _ = Cle(zone);
        var canonique = KidCanonique(zone, kid)
            ?? throw new ContenuAltereException($"Kid étranger à la clé racine de la zone {zone.Code()}.");
        return _operations.DesenvelopperAsync(new Uri(canonique), cleEnveloppee, cancellationToken);
    }

    /// <summary>Kid versionné de la clé racine de la zone, sous forme canonique, ou <c>null</c> s'il lui est étranger.</summary>
    private string? KidCanonique(ZoneDocument zone, string kid)
    {
        var prefixe = _prefixes[zone];
        if (!kid.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var version = kid[prefixe.Length..];
        return Version().IsMatch(version) ? prefixe + version : null;
    }

    private Uri Cle(ZoneDocument zone) =>
        _cles.TryGetValue(zone, out var cle)
            ? cle
            : throw new InvalidOperationException($"La zone {zone.Code()} n'est pas servie par cette instance : aucune clé racine.");

    [GeneratedRegex("^[0-9A-Za-z]{1,64}$")]
    private static partial Regex Version();
}

/// <summary>
/// Opérations réelles par <see cref="CryptographyClient"/> et <see cref="DefaultAzureCredential"/> (identité managée de
/// l'instance, rôle « Key Vault Crypto Service Encryption User » porté sur la clé). Fonctionne avec Key Vault et Managed HSM.
/// </summary>
public sealed class OperationsCleRacineAzure : IOperationsCleRacine
{
    private readonly TokenCredential _identite = new DefaultAzureCredential();
    private readonly ConcurrentDictionary<string, CryptographyClient> _parVersion = new(StringComparer.Ordinal);

    public async Task<CleEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken)
    {
        // Client recréé à chaque enveloppe : un client sans version mémorise la version lue, il ne verrait pas la rotation.
        var client = new CryptographyClient(cleSansVersion, _identite);
        var resultat = await client.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, cleDonnees, cancellationToken);
        return new CleEnveloppee(resultat.KeyId, resultat.EncryptedKey);
    }

    public async Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        var client = _parVersion.GetOrAdd(kid.AbsoluteUri, k => new CryptographyClient(new Uri(k), _identite));
        try
        {
            var resultat = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, cleEnveloppee, cancellationToken);
            return resultat.Key;
        }
        catch (RequestFailedException ex) when (ex.Status is 400 or 404)
        {
            throw new ContenuAltereException("Clé enveloppée refusée par le coffre ou version de la clé racine inconnue.", ex);
        }
    }
}
