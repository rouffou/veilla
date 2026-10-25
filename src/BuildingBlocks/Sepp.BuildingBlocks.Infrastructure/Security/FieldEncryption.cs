using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Clés de chiffrement des champs d'un service (ARC-44, ARC-45, ADR 0012). Une clé de données (DEK) courante chiffre ;
/// les anciennes DEK restent disponibles pour déchiffrer pendant la rotation. En Azure, les DEK sont enveloppées par la
/// clé racine non exportable du service dans le coffre de sa zone et déballées au démarrage
/// (<see cref="ChiffrementChamps"/>) : aucune clé en clair n'est stockée dans le coffre, le code ni l'image (CTR-16).
/// </summary>
public interface IFieldKeyProvider
{
    /// <summary>Identifiant de la DEK courante (format <c>v2</c>).</summary>
    string CurrentKeyId { get; }

    /// <summary>DEK du format <c>v2</c>.</summary>
    byte[] GetKey(string keyId);

    /// <summary>
    /// Ancienne clé AES du format <c>v1</c> (secret Key Vault, avant l'ADR 0012) : lecture seule des valeurs antérieures,
    /// tant que ces clés sont configurées.
    /// </summary>
    byte[] GetLegacyKey(string keyId) => throw new CryptographicException($"Clé de chiffrement historique '{keyId}' inconnue.");
}

/// <summary>
/// Chiffrement authentifié AES-256-GCM des champs sensibles (NISS, contenu clinique, contenu psychosocial), en plus du
/// chiffrement du stockage (ARC-45). Format courant : <c>v2:&lt;dekId&gt;:&lt;base64(nonce|chiffré|tag)&gt;</c>, données
/// associées <c>v2:&lt;dekId&gt;</c>. Le format historique <c>v1:&lt;keyId&gt;:…</c> (clé AES en secret, données associées
/// <c>&lt;keyId&gt;</c>) reste lisible si ses clés sont configurées, mais n'est plus jamais écrit.
/// </summary>
/// <remarks>Aucun appel au coffre par valeur : les DEK sont en mémoire (ADR 0012).</remarks>
public sealed class FieldEncryptor(IFieldKeyProvider keys)
{
    private const string Courant = "v2";
    private const string Historique = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Encrypt(string plaintext)
    {
        var keyId = keys.CurrentKeyId;
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + data.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(keys.GetKey(keyId), TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize, data.Length), output.AsSpan(NonceSize + data.Length), DonneesAssociees(Courant, keyId));
        return $"{Courant}:{keyId}:{Convert.ToBase64String(output)}";
    }

    public string Decrypt(string ciphertext)
    {
        var parts = ciphertext.Split(':', 3);
        if (parts is not [var version and (Courant or Historique), var keyId, var payload])
        {
            throw new CryptographicException("Format de champ chiffré inconnu.");
        }

        var input = Convert.FromBase64String(payload);
        if (input.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Champ chiffré tronqué.");
        }

        var key = version == Courant ? keys.GetKey(keyId) : keys.GetLegacyKey(keyId);
        var length = input.Length - NonceSize - TagSize;
        var plaintext = new byte[length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize, length), input.AsSpan(NonceSize + length), plaintext, DonneesAssociees(version, keyId));
        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>Indique si la valeur doit être rechiffrée avec la DEK courante (rotation, ou ancien format <c>v1</c>).</summary>
    public bool NeedsRotation(string ciphertext) => !ciphertext.StartsWith($"{Courant}:{keys.CurrentKeyId}:", StringComparison.Ordinal);

    private static byte[] DonneesAssociees(string version, string keyId) =>
        Encoding.UTF8.GetBytes(version == Courant ? $"{Courant}:{keyId}" : keyId);
}

/// <summary>
/// Index de recherche par hachage à clé (HMAC-SHA256) : permet de retrouver une personne par NISS
/// sans stocker le NISS en clair ni en hachage simple, sensible aux attaques par dictionnaire (DAT-06).
/// En Azure, la clé est enveloppée par la clé racine du service (ADR 0012) ; elle n'est pas tournée, car tout changement
/// de clé exige de recalculer l'index.
/// </summary>
public sealed class BlindIndex(byte[] key)
{
    public string Compute(string value)
    {
        var normalized = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalized)));
    }
}

/// <summary>Convertisseur EF Core : la colonne ne contient que le texte chiffré.</summary>
public sealed class EncryptedStringConverter(FieldEncryptor encryptor)
    : ValueConverter<string, string>(v => encryptor.Encrypt(v), v => encryptor.Decrypt(v));
