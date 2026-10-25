using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Opérations sur une clé racine dans Azure Key Vault ou Managed HSM (<c>wrapKey</c>, <c>unwrapKey</c>, RSA-OAEP-256).
/// Abstraites pour tester <see cref="CleRacineKeyVault"/> sans appel Azure.
/// </summary>
public interface IOperationsCleRacine
{
    /// <summary>Enveloppe avec la version courante de la clé désignée par son URI sans version.</summary>
    /// <returns>Le kid versionné renvoyé par le coffre et la clé enveloppée.</returns>
    Task<CleDonneesEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken);

    /// <summary>Déballe avec la version désignée par le kid versionné.</summary>
    /// <exception cref="RequestFailedException">
    /// Refus du coffre, transmis tel quel : <see cref="CleRacineKeyVault"/> le classe selon son statut HTTP.
    /// </exception>
    Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken);
}

/// <summary>
/// Clé racine RSA non exportable d'un Key Vault ou d'un Managed HSM (ARC-44, ADR 0009, ADR 0012). La configuration donne
/// l'URI de la clé <b>sans version</b> : l'enveloppe utilise la version courante (rotation automatique) et la donnée
/// conserve le kid versionné, qui reste utilisable pour déballer après rotation.
/// </summary>
/// <remarks>
/// Le kid lu avec une clé enveloppée n'est pas authentifié avant le déballage : il est confronté à la clé configurée
/// (même coffre, même nom, version alphanumérique) avant tout appel, pour qu'une donnée altérée ne puisse pas diriger
/// l'identité managée vers un autre coffre ou une autre clé (SSRF).
/// </remarks>
public sealed partial class CleRacineKeyVault : ICleRacine
{
    private readonly string _prefixe;
    private readonly string _designation;
    private readonly IOperationsCleRacine _operations;

    /// <param name="cle">URI de la clé sans version.</param>
    /// <param name="operations">Opérations sur le coffre.</param>
    /// <param name="designation">Désignation de la clé dans les messages (paramètre de configuration, zone).</param>
    public CleRacineKeyVault(Uri cle, IOperationsCleRacine operations, string designation)
    {
        _designation = designation;
        Cle = LireUriCle(cle.AbsoluteUri, designation);
        _prefixe = $"{Cle.AbsoluteUri.TrimEnd('/')}/";
        _operations = operations;
    }

    /// <summary>URI canonique de la clé, sans version.</summary>
    public Uri Cle { get; }

    /// <summary>Contrôle l'URI d'une clé racine : HTTPS, forme <c>https://&lt;coffre&gt;/keys/&lt;nom&gt;</c>, sans version.</summary>
    public static Uri LireUriCle(string? valeur, string designation)
    {
        if (!Uri.TryCreate(valeur, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.AbsolutePath.StartsWith("/keys/", StringComparison.OrdinalIgnoreCase)
            || !KeyVaultKeyIdentifier.TryCreate(uri, out var id) || string.IsNullOrEmpty(id.Name))
        {
            throw new InvalidOperationException(
                $"{designation} doit être l'URI HTTPS d'une clé Key Vault ou Managed HSM (https://<coffre>/keys/<nom>).");
        }

        if (!string.IsNullOrEmpty(id.Version))
        {
            throw new InvalidOperationException(
                $"{designation} doit désigner la clé sans version : la version courante enveloppe, le kid versionné est conservé.");
        }

        return new Uri($"{id.VaultUri.AbsoluteUri.TrimEnd('/')}/keys/{id.Name}");
    }

    public async Task<CleDonneesEnveloppee> EnvelopperAsync(ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken)
    {
        var copie = cleDonnees.ToArray();
        try
        {
            var resultat = await _operations.EnvelopperAsync(Cle, copie, cancellationToken);
            var kid = KidCanonique(resultat.Kid)
                ?? throw new InvalidOperationException($"Le coffre a renvoyé un kid étranger à la clé racine ({_designation}).");
            return new CleDonneesEnveloppee(kid, resultat.Octets);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copie);
        }
    }

    /// <remarks>
    /// Classement des refus du coffre (#377) : 400 (clé enveloppée invalide) signale une donnée altérée ; 404 (version
    /// supprimée ou purgée) et 401/403 (droits manquants, version désactivée) sont des erreurs d'exploitation, la donnée
    /// n'est pas en cause et redevient lisible une fois la version restaurée ou l'accès rétabli. Les autres statuts
    /// (indisponibilité du coffre, limitation) remontent tels quels.
    /// </remarks>
    public async Task<byte[]> DesenvelopperAsync(string kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        var canonique = KidCanonique(kid)
            ?? throw new EnveloppeAltereeException($"Kid étranger à la clé racine ({_designation}).");
        try
        {
            return await _operations.DesenvelopperAsync(new Uri(canonique), cleEnveloppee, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            throw new EnveloppeAltereeException("Clé enveloppée refusée par le coffre.", ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new CleRacineInaccessibleException(canonique, CauseCleRacineInaccessible.Introuvable, ex);
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new CleRacineInaccessibleException(canonique, CauseCleRacineInaccessible.AccesRefuse, ex);
        }
    }

    /// <summary>Kid versionné de la clé racine, sous forme canonique, ou <c>null</c> s'il lui est étranger.</summary>
    public string? KidCanonique(string kid)
    {
        if (!kid.StartsWith(_prefixe, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var version = kid[_prefixe.Length..];
        return Version().IsMatch(version) ? _prefixe + version : null;
    }

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

    public async Task<CleDonneesEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken)
    {
        // Client recréé à chaque enveloppe : un client sans version mémorise la version lue, il ne verrait pas la rotation.
        var client = new CryptographyClient(cleSansVersion, _identite);
        var resultat = await client.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, cleDonnees, cancellationToken);
        return new CleDonneesEnveloppee(resultat.KeyId, resultat.EncryptedKey);
    }

    public async Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        // Les refus du coffre (RequestFailedException) sont classés par CleRacineKeyVault (#377).
        var client = _parVersion.GetOrAdd(kid.AbsoluteUri, k => new CryptographyClient(new Uri(k), _identite));
        var resultat = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, cleEnveloppee, cancellationToken);
        return resultat.Key;
    }
}
