using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Chiffrement;

/// <summary>
/// Chiffrement authentifié AES-256-GCM du contenu de chaque document avec la clé de sa zone (§14.3, ARC-45). Clés lues
/// par zone dans <c>Documents:Chiffrement:&lt;zone&gt;:Encryption</c> (<c>CurrentKeyId</c>, <c>Keys:&lt;id&gt;</c>, format
/// du socle <see cref="ConfigurationFieldKeyProvider"/>), en Azure depuis le Key Vault de la zone (clés HSM pour les zones
/// médicale et psychosociale, ARC-44). Une clé ne peut servir qu'à une seule zone.
/// </summary>
/// <remarks>
/// Format de l'objet stocké : <c>SEPPDOC1</c> | longueur de l'identifiant de clé (1 octet) | identifiant de clé (UTF-8) |
/// nonce (12 octets) | étiquette d'authentification (16 octets) | contenu chiffré. Données associées authentifiées :
/// <c>documents|&lt;zone&gt;|&lt;document&gt;|&lt;clé&gt;</c> — un objet copié vers un autre document ou une autre zone ne se
/// déchiffre pas. Un nonce aléatoire par document ; la rotation des clés laisse les anciennes disponibles en déchiffrement.
/// </remarks>
public sealed class ChiffrementParZone : IChiffrementDocuments
{
    private const int TailleNonce = 12;
    private const int TailleEtiquette = 16;
    private static readonly byte[] Signature = "SEPPDOC1"u8.ToArray();

    private readonly Dictionary<ZoneDocument, IFieldKeyProvider> _cles;

    public ChiffrementParZone(IConfiguration configuration)
    {
        _cles = Zones.Toutes.ToDictionary(z => z, z => (IFieldKeyProvider)new ConfigurationFieldKeyProvider(configuration.GetSection($"Documents:Chiffrement:{z.Code()}")));
        var empreintes = new Dictionary<string, ZoneDocument>(StringComparer.Ordinal);
        foreach (var zone in Zones.Toutes)
        {
            foreach (var cle in configuration.GetSection($"Documents:Chiffrement:{zone.Code()}:Encryption:Keys").GetChildren())
            {
                var empreinte = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(cle.Value!)));
                if (empreintes.TryGetValue(empreinte, out var autre) && autre != zone)
                {
                    throw new InvalidOperationException($"La même clé de chiffrement est configurée pour les zones {autre.Code()} et {zone.Code()} : une clé par zone (§14.3).");
                }

                empreintes[empreinte] = zone;
            }
        }
    }

    public ContenuChiffre Chiffrer(ZoneDocument zone, Guid documentId, byte[] clair)
    {
        var fournisseur = _cles[zone];
        var cleId = fournisseur.CurrentKeyId;
        var id = Encoding.UTF8.GetBytes(cleId);
        if (id.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Identifiant de clé trop long.");
        }

        var entete = Signature.Length + 1 + id.Length;
        var sortie = new byte[entete + TailleNonce + TailleEtiquette + clair.Length];
        Signature.CopyTo(sortie, 0);
        sortie[Signature.Length] = (byte)id.Length;
        id.CopyTo(sortie, Signature.Length + 1);
        var nonce = sortie.AsSpan(entete, TailleNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(fournisseur.GetKey(cleId), TailleEtiquette);
        aes.Encrypt(nonce, clair, sortie.AsSpan(entete + TailleNonce + TailleEtiquette), sortie.AsSpan(entete + TailleNonce, TailleEtiquette),
            DonneesAssociees(zone, documentId, cleId));
        return new ContenuChiffre(sortie, cleId);
    }

    public byte[] Dechiffrer(ZoneDocument zone, Guid documentId, byte[] chiffre)
    {
        if (chiffre.Length < Signature.Length + 1 || !chiffre.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new ContenuAltereException("Format de document chiffré inconnu.");
        }

        var longueurId = chiffre[Signature.Length];
        var entete = Signature.Length + 1 + longueurId;
        if (chiffre.Length < entete + TailleNonce + TailleEtiquette)
        {
            throw new ContenuAltereException("Document chiffré tronqué.");
        }

        var cleId = Encoding.UTF8.GetString(chiffre, Signature.Length + 1, longueurId);
        byte[] cle;
        try
        {
            cle = _cles[zone].GetKey(cleId);
        }
        catch (CryptographicException ex)
        {
            throw new ContenuAltereException($"Clé '{cleId}' inconnue pour la zone {zone.Code()}.", ex);
        }

        var clair = new byte[chiffre.Length - entete - TailleNonce - TailleEtiquette];
        try
        {
            using var aes = new AesGcm(cle, TailleEtiquette);
            aes.Decrypt(chiffre.AsSpan(entete, TailleNonce), chiffre.AsSpan(entete + TailleNonce + TailleEtiquette), chiffre.AsSpan(entete + TailleNonce, TailleEtiquette),
                clair, DonneesAssociees(zone, documentId, cleId));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new ContenuAltereException("Échec de l'authentification du contenu : document altéré ou étranger à cette zone.", ex);
        }

        return clair;
    }

    private static byte[] DonneesAssociees(ZoneDocument zone, Guid documentId, string cleId)
    {
        var texte = Encoding.UTF8.GetBytes($"documents|{zone.Code()}|{documentId:N}|{cleId}");
        var longueur = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(longueur, texte.Length);
        return [.. longueur, .. texte];
    }
}
