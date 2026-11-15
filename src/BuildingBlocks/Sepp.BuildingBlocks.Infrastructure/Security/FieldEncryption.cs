using System.Linq.Expressions;
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

    /// <summary>
    /// Préfixe des valeurs chiffrées avec la DEK courante (<c>v2:&lt;dekId&gt;:</c>) : il ne contient aucun secret et sert
    /// au rechiffrement à écarter, en SQL, les valeurs déjà à jour.
    /// </summary>
    public string PrefixeCourant => $"{Courant}:{keys.CurrentKeyId}:";

    /// <summary>Indique si la valeur doit être rechiffrée avec la DEK courante (rotation, ou ancien format <c>v1</c>).</summary>
    public bool NeedsRotation(string ciphertext) => !ciphertext.StartsWith(PrefixeCourant, StringComparison.Ordinal);

    /// <summary>
    /// Rechiffre une valeur avec la DEK courante (format <c>v2</c>) ; une valeur déjà à jour est rendue telle quelle.
    /// </summary>
    /// <exception cref="CryptographicException">Valeur altérée, ou clé (DEK ou clé <c>v1</c>) non configurée.</exception>
    public string Rechiffrer(string ciphertext) => NeedsRotation(ciphertext) ? Encrypt(Decrypt(ciphertext)) : ciphertext;

    private static byte[] DonneesAssociees(string version, string keyId) =>
        Encoding.UTF8.GetBytes(version == Courant ? $"{Courant}:{keyId}" : keyId);
}

/// <summary>
/// Index de recherche par hachage à clé (HMAC-SHA256) : permet de retrouver une personne par NISS
/// sans stocker le NISS en clair ni en hachage simple, sensible aux attaques par dictionnaire (DAT-06).
/// En Azure, la clé est enveloppée par la clé racine du service (ADR 0012).
/// </summary>
/// <remarks>
/// Rotation (#473) : pendant le recalcul de l'index, une clé précédente peut être configurée. Seule la clé courante
/// calcule l'empreinte écrite ; la recherche et le contrôle de doublon portent sur les deux empreintes
/// (<see cref="Empreintes"/>), si bien qu'une ligne pas encore recalculée reste trouvée.
/// </remarks>
public sealed class BlindIndex
{
    private readonly byte[] _cle;
    private readonly byte[]? _clePrecedente;

    /// <param name="key">Clé courante (256 bits) : calcule l'empreinte écrite.</param>
    /// <param name="clePrecedente">Clé précédente, en lecture seule pendant une rotation ; <c>null</c> hors rotation.</param>
    public BlindIndex(byte[] key, byte[]? clePrecedente = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (clePrecedente is not null && CryptographicOperations.FixedTimeEquals(key, clePrecedente))
        {
            throw new ArgumentException("La clé précédente de l'index aveugle doit différer de la clé courante.", nameof(clePrecedente));
        }

        _cle = key;
        _clePrecedente = clePrecedente;
    }

    /// <summary>Vrai pendant une rotation : une clé précédente est configurée.</summary>
    public bool AvecClePrecedente => _clePrecedente is not null;

    /// <summary>Empreinte avec la clé courante : la seule qui soit écrite.</summary>
    public string Compute(string value) => Calculer(_cle, value);

    /// <summary>Empreinte avec la clé précédente ; <c>null</c> hors rotation.</summary>
    public string? ComputePrecedente(string value) => _clePrecedente is null ? null : Calculer(_clePrecedente, value);

    /// <summary>Empreintes acceptées en lecture : clé courante d'abord, puis clé précédente pendant une rotation.</summary>
    public IReadOnlyList<string> Empreintes(string value) =>
        _clePrecedente is null ? [Compute(value)] : [Compute(value), Calculer(_clePrecedente, value)];

    private static string Calculer(byte[] cle, string value)
    {
        var normalized = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(HMACSHA256.HashData(cle, Encoding.UTF8.GetBytes(normalized)));
    }
}

/// <summary>
/// Marqueur commun à tous les convertisseurs EF Core qui chiffrent une colonne avec <see cref="FieldEncryptor"/> (#482) :
/// le rechiffrement (<see cref="ColonnesChiffrees"/>) traite exactement les colonnes qui le portent, et refuse un modèle où
/// le marqueur et le suffixe <c>_chiffre</c>/<c>_chiffree</c> ne vont pas ensemble, ou un convertisseur qui utilise
/// <see cref="FieldEncryptor"/> sans le porter.
/// </summary>
#pragma warning disable CA1040 // Interface de marquage voulue : détectée sur l'instance du convertisseur dans le modèle EF Core.
public interface IConvertisseurChiffrement;
#pragma warning restore CA1040

/// <summary>Convertisseur EF Core : la colonne ne contient que le texte chiffré.</summary>
public sealed class EncryptedStringConverter(FieldEncryptor encryptor)
    : ValueConverter<string, string>(v => encryptor.Encrypt(v), v => encryptor.Decrypt(v)), IConvertisseurChiffrement;

/// <summary>
/// Convertisseur EF Core chiffré pour une valeur qui n'est pas une chaîne (value object, énumération, contenu sérialisé) :
/// la valeur est d'abord mise sous forme de texte, puis chiffrée ; la colonne ne contient que le texte chiffré.
/// </summary>
public sealed class ConvertisseurChiffre<TModele> : ValueConverter<TModele, string>, IConvertisseurChiffrement
{
    /// <param name="chiffreur">Chiffreur des champs du service.</param>
    /// <param name="versTexte">Mise sous forme de texte, avant chiffrement.</param>
    /// <param name="depuisTexte">Lecture du texte, après déchiffrement.</param>
    public ConvertisseurChiffre(FieldEncryptor chiffreur, Func<TModele, string> versTexte, Func<string, TModele> depuisTexte)
        : base(VersFournisseur(chiffreur, versTexte), DepuisFournisseur(chiffreur, depuisTexte))
    {
    }

    private static Expression<Func<TModele, string>> VersFournisseur(FieldEncryptor chiffreur, Func<TModele, string> versTexte)
    {
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(versTexte);
        return v => chiffreur.Encrypt(versTexte(v));
    }

    private static Expression<Func<string, TModele>> DepuisFournisseur(FieldEncryptor chiffreur, Func<string, TModele> depuisTexte)
    {
        ArgumentNullException.ThrowIfNull(chiffreur);
        ArgumentNullException.ThrowIfNull(depuisTexte);
        return v => depuisTexte(chiffreur.Decrypt(v));
    }
}
