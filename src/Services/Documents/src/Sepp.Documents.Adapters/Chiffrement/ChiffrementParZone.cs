using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;

using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Chiffrement;

/// <summary>
/// Chiffrement par enveloppe du contenu de chaque document (§14.3, ARC-44, ARC-45, ADR 0009) : une clé de données
/// AES-256 aléatoire par document, enveloppée par la clé racine <c>documents-kek</c> de la zone (<see cref="IEnveloppeCles"/>).
/// La clé de données n'existe en clair qu'en mémoire, le temps de l'opération, puis est effacée.
/// </summary>
/// <remarks>
/// <para>
/// Format <c>SEPPDOC2</c> (entiers gros-boutistes) : signature <c>SEPPDOC2</c> (8 octets) | longueur du kid (2 octets) |
/// kid versionné de la clé racine (UTF-8) | longueur de la clé enveloppée (2 octets) | clé de données enveloppée |
/// nonce (12 octets) | étiquette d'authentification (16 octets) | contenu chiffré (AES-256-GCM).
/// Données associées authentifiées : <c>documents|SEPPDOC2|&lt;zone&gt;|&lt;document&gt;|&lt;kid&gt;</c> (préfixé de sa
/// longueur) suivi de l'en-tête complet (signature, kid, clé enveloppée) : un objet copié vers un autre document ou une
/// autre zone, un kid ou une clé enveloppée modifiés ne se déchiffrent pas.
/// </para>
/// <para>
/// Lecture conservée du format historique <c>SEPPDOC1</c> (clé AES de zone, sans enveloppe) si les anciennes clés sont
/// configurées dans <c>Documents:Chiffrement:&lt;zone&gt;:Encryption:Keys:&lt;id&gt;</c> ; elles ne servent plus jamais à
/// chiffrer. Une même clé ne peut pas être configurée pour deux zones.
/// </para>
/// </remarks>
public sealed class ChiffrementParZone : IChiffrementDocuments
{
    private const int TailleCleDonnees = 32;
    private const int TailleNonce = 12;
    private const int TailleEtiquette = 16;
    private const int TailleMaxKid = 1024;
    private const int TailleMaxCleEnveloppee = 4096;
    private static readonly byte[] SignatureV1 = "SEPPDOC1"u8.ToArray();
    private static readonly byte[] SignatureV2 = "SEPPDOC2"u8.ToArray();

    private readonly IEnveloppeCles _enveloppe;
    private readonly IZonesServies _zones;
    private readonly Dictionary<ZoneDocument, Dictionary<string, byte[]>> _anciennesCles;

    public ChiffrementParZone(IEnveloppeCles enveloppe, IConfiguration configuration, IZonesServies zonesServies)
    {
        _enveloppe = enveloppe;
        _zones = zonesServies;

        // Anciennes clés SEPPDOC1 des seules zones servies (décision #356) : une instance ne détient jamais la clé d'une
        // autre zone.
        _anciennesCles = [];
        var empreintes = new Dictionary<string, ZoneDocument>(StringComparer.Ordinal);
        foreach (var zone in Zones.Toutes.Where(zonesServies.Sert))
        {
            var cles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var cle in configuration.GetSection($"Documents:Chiffrement:{zone.Code()}:Encryption:Keys").GetChildren())
            {
                var octets = Convert.FromBase64String(cle.Value ?? string.Empty);
                if (octets.Length != TailleCleDonnees)
                {
                    throw new InvalidOperationException($"L'ancienne clé de chiffrement '{cle.Key}' de la zone {zone.Code()} doit faire 256 bits.");
                }

                var empreinte = Convert.ToHexString(SHA256.HashData(octets));
                if (empreintes.TryGetValue(empreinte, out var autre) && autre != zone)
                {
                    throw new InvalidOperationException($"La même clé de chiffrement est configurée pour les zones {autre.Code()} et {zone.Code()} : une clé par zone (§14.3).");
                }

                empreintes[empreinte] = zone;
                cles[cle.Key] = octets;
            }

            _anciennesCles[zone] = cles;
        }
    }

    public async Task<ContenuChiffre> ChiffrerAsync(ZoneDocument zone, Guid documentId, byte[] clair, CancellationToken cancellationToken)
    {
        VerifierZone(zone);
        var cleDonnees = RandomNumberGenerator.GetBytes(TailleCleDonnees);
        try
        {
            var enveloppee = await _enveloppe.EnvelopperAsync(zone, cleDonnees, cancellationToken);
            var kid = Encoding.UTF8.GetBytes(enveloppee.Kid);
            if (kid.Length is 0 or > TailleMaxKid || enveloppee.Octets.Length is 0 or > TailleMaxCleEnveloppee)
            {
                throw new InvalidOperationException("Kid ou clé enveloppée de taille invalide.");
            }

            var entete = SignatureV2.Length + 2 + kid.Length + 2 + enveloppee.Octets.Length;
            var sortie = new byte[entete + TailleNonce + TailleEtiquette + clair.Length];
            var position = 0;
            SignatureV2.CopyTo(sortie, position);
            position += SignatureV2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(sortie.AsSpan(position, 2), (ushort)kid.Length);
            position += 2;
            kid.CopyTo(sortie, position);
            position += kid.Length;
            BinaryPrimitives.WriteUInt16BigEndian(sortie.AsSpan(position, 2), (ushort)enveloppee.Octets.Length);
            position += 2;
            enveloppee.Octets.CopyTo(sortie, position);

            var nonce = sortie.AsSpan(entete, TailleNonce);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(cleDonnees, TailleEtiquette);
            aes.Encrypt(nonce, clair, sortie.AsSpan(entete + TailleNonce + TailleEtiquette), sortie.AsSpan(entete + TailleNonce, TailleEtiquette),
                DonneesAssocieesV2(zone, documentId, enveloppee.Kid, sortie.AsSpan(0, entete)));
            return new ContenuChiffre(sortie, enveloppee.Kid);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleDonnees);
        }
    }

    public async Task<byte[]> DechiffrerAsync(ZoneDocument zone, Guid documentId, byte[] chiffre, CancellationToken cancellationToken)
    {
        VerifierZone(zone);
        if (chiffre.Length >= SignatureV2.Length && chiffre.AsSpan(0, SignatureV2.Length).SequenceEqual(SignatureV2))
        {
            return await DechiffrerV2Async(zone, documentId, chiffre, cancellationToken);
        }

        if (chiffre.Length >= SignatureV1.Length && chiffre.AsSpan(0, SignatureV1.Length).SequenceEqual(SignatureV1))
        {
            return DechiffrerV1(zone, documentId, chiffre);
        }

        throw new ContenuAltereException("Format de document chiffré inconnu.");
    }

    private async Task<byte[]> DechiffrerV2Async(ZoneDocument zone, Guid documentId, byte[] chiffre, CancellationToken cancellationToken)
    {
        var position = SignatureV2.Length;
        if (!LireBloc(chiffre, ref position, TailleMaxKid, out var kidOctets) || !LireBloc(chiffre, ref position, TailleMaxCleEnveloppee, out var enveloppee))
        {
            throw new ContenuAltereException("Document chiffré tronqué.");
        }

        var entete = position;
        if (chiffre.Length < entete + TailleNonce + TailleEtiquette)
        {
            throw new ContenuAltereException("Document chiffré tronqué.");
        }

        string kid;
        try
        {
            kid = new UTF8Encoding(false, true).GetString(kidOctets.Span);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ContenuAltereException("Kid illisible.", ex);
        }

        // Le kid n'est pas encore authentifié : l'adaptateur d'enveloppe le confronte à la clé racine de la zone avant tout appel.
        var cleDonnees = await _enveloppe.DesenvelopperAsync(zone, kid, enveloppee.ToArray(), cancellationToken);
        try
        {
            if (cleDonnees.Length != TailleCleDonnees)
            {
                throw new ContenuAltereException("Clé de données déballée de taille invalide.");
            }

            var clair = new byte[chiffre.Length - entete - TailleNonce - TailleEtiquette];
            try
            {
                using var aes = new AesGcm(cleDonnees, TailleEtiquette);
                aes.Decrypt(chiffre.AsSpan(entete, TailleNonce), chiffre.AsSpan(entete + TailleNonce + TailleEtiquette), chiffre.AsSpan(entete + TailleNonce, TailleEtiquette),
                    clair, DonneesAssocieesV2(zone, documentId, kid, chiffre.AsSpan(0, entete)));
            }
            catch (AuthenticationTagMismatchException ex)
            {
                throw new ContenuAltereException("Échec de l'authentification du contenu : document altéré ou étranger à cette zone.", ex);
            }

            return clair;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleDonnees);
        }
    }

    /// <summary>Lecture du format historique <c>SEPPDOC1</c> : clé AES de zone, sans enveloppe.</summary>
    private byte[] DechiffrerV1(ZoneDocument zone, Guid documentId, byte[] chiffre)
    {
        if (chiffre.Length < SignatureV1.Length + 1)
        {
            throw new ContenuAltereException("Document chiffré tronqué.");
        }

        var longueurId = chiffre[SignatureV1.Length];
        var entete = SignatureV1.Length + 1 + longueurId;
        if (chiffre.Length < entete + TailleNonce + TailleEtiquette)
        {
            throw new ContenuAltereException("Document chiffré tronqué.");
        }

        var cleId = Encoding.UTF8.GetString(chiffre, SignatureV1.Length + 1, longueurId);
        if (!_anciennesCles[zone].TryGetValue(cleId, out var cle))
        {
            throw new ContenuAltereException($"Ancienne clé '{cleId}' inconnue pour la zone {zone.Code()}.");
        }

        var clair = new byte[chiffre.Length - entete - TailleNonce - TailleEtiquette];
        try
        {
            using var aes = new AesGcm(cle, TailleEtiquette);
            aes.Decrypt(chiffre.AsSpan(entete, TailleNonce), chiffre.AsSpan(entete + TailleNonce + TailleEtiquette), chiffre.AsSpan(entete + TailleNonce, TailleEtiquette),
                clair, DonneesAssocieesV1(zone, documentId, cleId));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new ContenuAltereException("Échec de l'authentification du contenu : document altéré ou étranger à cette zone.", ex);
        }

        return clair;
    }

    private void VerifierZone(ZoneDocument zone)
    {
        if (!_zones.Sert(zone))
        {
            throw new InvalidOperationException($"La zone {zone.Code()} n'est pas servie par cette instance : aucune clé racine.");
        }
    }

    private static bool LireBloc(byte[] source, ref int position, int tailleMax, out ReadOnlyMemory<byte> bloc)
    {
        bloc = default;
        if (source.Length < position + 2)
        {
            return false;
        }

        var longueur = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(position, 2));
        if (longueur == 0 || longueur > tailleMax || source.Length < position + 2 + longueur)
        {
            return false;
        }

        bloc = source.AsMemory(position + 2, longueur);
        position += 2 + longueur;
        return true;
    }

    private static byte[] DonneesAssocieesV2(ZoneDocument zone, Guid documentId, string kid, ReadOnlySpan<byte> entete)
    {
        var contexte = Encoding.UTF8.GetBytes($"documents|SEPPDOC2|{zone.Code()}|{documentId:N}|{kid}");
        var aad = new byte[4 + contexte.Length + entete.Length];
        BinaryPrimitives.WriteInt32BigEndian(aad, contexte.Length);
        contexte.CopyTo(aad, 4);
        entete.CopyTo(aad.AsSpan(4 + contexte.Length));
        return aad;
    }

    private static byte[] DonneesAssocieesV1(ZoneDocument zone, Guid documentId, string cleId)
    {
        var texte = Encoding.UTF8.GetBytes($"documents|{zone.Code()}|{documentId:N}|{cleId}");
        var longueur = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(longueur, texte.Length);
        return [.. longueur, .. texte];
    }
}
