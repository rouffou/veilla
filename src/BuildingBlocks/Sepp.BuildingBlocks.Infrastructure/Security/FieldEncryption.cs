using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Clés de chiffrement applicatif d'une zone (ARC-45). Une clé courante chiffre ; les anciennes clés restent
/// disponibles pour déchiffrer pendant la rotation (ARC-44). En Azure, les clés sont des secrets Key Vault
/// injectés par référence dans la configuration, jamais présents dans le code ni l'image (CTR-16).
/// </summary>
public interface IFieldKeyProvider
{
    string CurrentKeyId { get; }

    byte[] GetKey(string keyId);
}

/// <summary>Clés lues dans la section <c>Encryption</c> : <c>CurrentKeyId</c> et <c>Keys:&lt;id&gt;</c> (256 bits en base64).</summary>
public sealed class ConfigurationFieldKeyProvider : IFieldKeyProvider
{
    private readonly Dictionary<string, byte[]> _keys;

    public ConfigurationFieldKeyProvider(IConfiguration configuration)
    {
        var section = configuration.GetSection("Encryption");
        CurrentKeyId = section["CurrentKeyId"] ?? throw new InvalidOperationException("Encryption:CurrentKeyId manquant.");
        _keys = section.GetSection("Keys").GetChildren().ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value!), StringComparer.Ordinal);
        foreach (var (id, key) in _keys)
        {
            if (key.Length != 32)
            {
                throw new InvalidOperationException($"La clé de chiffrement '{id}' doit faire 256 bits.");
            }
        }

        _ = GetKey(CurrentKeyId);
    }

    public string CurrentKeyId { get; }

    public byte[] GetKey(string keyId) =>
        _keys.TryGetValue(keyId, out var key) ? key : throw new CryptographicException($"Clé de chiffrement '{keyId}' inconnue.");
}

/// <summary>
/// Chiffrement authentifié AES-256-GCM des champs sensibles (NISS, contenu clinique, contenu psychosocial),
/// en plus du chiffrement du stockage (ARC-45). Format : <c>v1:&lt;keyId&gt;:&lt;base64(nonce|chiffré|tag)&gt;</c>.
/// </summary>
public sealed class FieldEncryptor(IFieldKeyProvider keys)
{
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
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize, data.Length), output.AsSpan(NonceSize + data.Length), Encoding.UTF8.GetBytes(keyId));
        return $"v1:{keyId}:{Convert.ToBase64String(output)}";
    }

    public string Decrypt(string ciphertext)
    {
        var parts = ciphertext.Split(':', 3);
        if (parts is not ["v1", var keyId, var payload])
        {
            throw new CryptographicException("Format de champ chiffré inconnu.");
        }

        var input = Convert.FromBase64String(payload);
        if (input.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Champ chiffré tronqué.");
        }

        var length = input.Length - NonceSize - TagSize;
        var plaintext = new byte[length];
        using var aes = new AesGcm(keys.GetKey(keyId), TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize, length), input.AsSpan(NonceSize + length), plaintext, Encoding.UTF8.GetBytes(keyId));
        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>Indique si la valeur doit être rechiffrée avec la clé courante (rotation).</summary>
    public bool NeedsRotation(string ciphertext) => !ciphertext.StartsWith($"v1:{keys.CurrentKeyId}:", StringComparison.Ordinal);
}

/// <summary>
/// Index de recherche par hachage à clé (HMAC-SHA256) : permet de retrouver une personne par NISS
/// sans stocker le NISS en clair ni en hachage simple, sensible aux attaques par dictionnaire (DAT-06).
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
