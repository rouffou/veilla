using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;

using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Chiffrement;

/// <summary>
/// Enveloppe locale pour le développement et les tests, <b>sans valeur de sécurité</b> : la clé racine de chaque zone est
/// lue en configuration (<c>Documents:Enveloppe:CleKek[:&lt;zone&gt;]</c> : <c>Courante</c>, <c>Versions:&lt;version&gt;</c>)
/// et enveloppe la clé de données en AES-256-GCM. Plusieurs versions permettent d'exercer la rotation : la version
/// courante enveloppe, toutes les versions déballent. Refusée hors développement et test.
/// </summary>
/// <remarks>
/// Kid : <c>local:&lt;zone&gt;:&lt;version&gt;</c>. Clé enveloppée : nonce (12 octets) | étiquette (16 octets) | clé
/// chiffrée ; données associées : <c>documents-kek|&lt;kid&gt;</c>.
/// </remarks>
public sealed class EnveloppeLocale : IEnveloppeCles
{
    private const int TailleCle = 32;
    private const int TailleNonce = 12;
    private const int TailleEtiquette = 16;

    private readonly Dictionary<ZoneDocument, (string Courante, Dictionary<string, byte[]> Versions)> _cles;

    public EnveloppeLocale(IReadOnlyDictionary<ZoneDocument, (string Courante, Dictionary<string, byte[]> Versions)> cles)
    {
        _cles = cles.ToDictionary();
        var empreintes = new Dictionary<string, ZoneDocument>(StringComparer.Ordinal);
        foreach (var (zone, (courante, versions)) in _cles)
        {
            if (!versions.ContainsKey(courante))
            {
                throw new InvalidOperationException($"Version courante '{courante}' de la clé racine locale de la zone {zone.Code()} absente de Versions.");
            }

            foreach (var (version, cle) in versions)
            {
                if (cle.Length != TailleCle || version.Contains(':', StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Clé racine locale '{version}' de la zone {zone.Code()} invalide (256 bits, version sans « : »).");
                }

                var empreinte = Convert.ToHexString(SHA256.HashData(cle));
                if (empreintes.TryGetValue(empreinte, out var autre) && autre != zone)
                {
                    throw new InvalidOperationException($"La même clé racine est configurée pour les zones {autre.Code()} et {zone.Code()} : une clé par zone (§14.3).");
                }

                empreintes[empreinte] = zone;
            }
        }
    }

    /// <summary>Lit, pour chaque zone, la section <c>Courante</c> et <c>Versions:&lt;version&gt;</c>.</summary>
    public static EnveloppeLocale Depuis(IReadOnlyDictionary<ZoneDocument, IConfigurationSection> sections) =>
        new(sections.ToDictionary(s => s.Key, s =>
        {
            var courante = s.Value["Courante"];
            if (string.IsNullOrWhiteSpace(courante))
            {
                throw new InvalidOperationException($"{s.Value.Path}:Courante manquant.");
            }

            var versions = s.Value.GetSection("Versions").GetChildren()
                .ToDictionary(v => v.Key, v => Convert.FromBase64String(v.Value ?? string.Empty), StringComparer.Ordinal);
            return (courante, versions);
        }));

    public Task<CleEnveloppee> EnvelopperAsync(ZoneDocument zone, ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken)
    {
        var (courante, versions) = Cles(zone);
        var kid = Kid(zone, courante);
        var sortie = new byte[TailleNonce + TailleEtiquette + cleDonnees.Length];
        var nonce = sortie.AsSpan(0, TailleNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(versions[courante], TailleEtiquette);
        aes.Encrypt(nonce, cleDonnees.Span, sortie.AsSpan(TailleNonce + TailleEtiquette), sortie.AsSpan(TailleNonce, TailleEtiquette), DonneesAssociees(kid));
        return Task.FromResult(new CleEnveloppee(kid, sortie));
    }

    public Task<byte[]> DesenvelopperAsync(ZoneDocument zone, string kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        var (_, versions) = Cles(zone);
        var prefixe = $"local:{zone.Code()}:";
        if (!kid.StartsWith(prefixe, StringComparison.Ordinal) || !versions.TryGetValue(kid[prefixe.Length..], out var cle))
        {
            throw new ContenuAltereException($"Kid étranger à la clé racine de la zone {zone.Code()} ou version inconnue.");
        }

        if (cleEnveloppee.Length <= TailleNonce + TailleEtiquette)
        {
            throw new ContenuAltereException("Clé enveloppée tronquée.");
        }

        var cleDonnees = new byte[cleEnveloppee.Length - TailleNonce - TailleEtiquette];
        try
        {
            using var aes = new AesGcm(cle, TailleEtiquette);
            aes.Decrypt(cleEnveloppee.AsSpan(0, TailleNonce), cleEnveloppee.AsSpan(TailleNonce + TailleEtiquette), cleEnveloppee.AsSpan(TailleNonce, TailleEtiquette),
                cleDonnees, DonneesAssociees(kid));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            CryptographicOperations.ZeroMemory(cleDonnees);
            throw new ContenuAltereException("Clé enveloppée invalide.", ex);
        }

        return Task.FromResult(cleDonnees);
    }

    private (string Courante, Dictionary<string, byte[]> Versions) Cles(ZoneDocument zone) =>
        _cles.TryGetValue(zone, out var cles)
            ? cles
            : throw new InvalidOperationException($"La zone {zone.Code()} n'est pas servie par cette instance : aucune clé racine.");

    private static string Kid(ZoneDocument zone, string version) => $"local:{zone.Code()}:{version}";

    private static byte[] DonneesAssociees(string kid) => Encoding.UTF8.GetBytes($"documents-kek|{kid}");
}
