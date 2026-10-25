using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Clé racine locale pour le développement et les tests, <b>sans valeur de sécurité</b> : la clé racine est lue en
/// configuration (<c>Courante</c>, <c>Versions:&lt;version&gt;</c>, 256 bits en base64) et enveloppe la clé de données
/// en AES-256-GCM. Plusieurs versions permettent d'exercer la rotation : la version courante enveloppe, toutes les
/// versions déballent. Refusée hors développement et test (<see cref="VerifierEnvironnement"/>).
/// </summary>
/// <remarks>
/// Kid : <c>&lt;préfixe&gt;&lt;version&gt;</c>. Clé enveloppée : nonce (12 octets) | étiquette (16 octets) | clé
/// chiffrée ; données associées : <c>&lt;usage&gt;|&lt;kid&gt;</c>.
/// </remarks>
public sealed class CleRacineLocale : ICleRacine
{
    private const int TailleCle = 32;
    private const int TailleNonce = 12;
    private const int TailleEtiquette = 16;

    private readonly string _prefixeKid;
    private readonly string _usage;
    private readonly string _courante;
    private readonly Dictionary<string, byte[]> _versions;

    public CleRacineLocale(string prefixeKid, string usage, string courante, IReadOnlyDictionary<string, byte[]> versions)
    {
        _prefixeKid = prefixeKid;
        _usage = usage;
        _courante = courante;
        _versions = versions.ToDictionary(StringComparer.Ordinal);
        if (!_versions.ContainsKey(courante))
        {
            throw new InvalidOperationException($"Version courante '{courante}' de la clé racine locale ({usage}) absente de Versions.");
        }

        foreach (var (version, cle) in _versions)
        {
            if (cle.Length != TailleCle || version.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Clé racine locale '{version}' ({usage}) invalide (256 bits, version sans « : »).");
            }
        }
    }

    /// <summary>Refuse la clé racine locale hors développement et test (ARC-44).</summary>
    public static void VerifierEnvironnement(IHostEnvironment environnement, string parametre)
    {
        if (!environnement.IsDevelopment() && !environnement.IsEnvironment("Test") && !environnement.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                $"{parametre} « Local » (clé racine en configuration) est réservé au développement et aux tests : utiliser « KeyVault » (ARC-44).");
        }
    }

    /// <summary>Lit la section <c>Courante</c> et <c>Versions:&lt;version&gt;</c>.</summary>
    public static CleRacineLocale Depuis(IConfigurationSection section, string prefixeKid, string usage)
    {
        var courante = section["Courante"];
        if (string.IsNullOrWhiteSpace(courante))
        {
            throw new InvalidOperationException($"{section.Path}:Courante manquant.");
        }

        var versions = section.GetSection("Versions").GetChildren()
            .ToDictionary(v => v.Key, v => Convert.FromBase64String(v.Value ?? string.Empty), StringComparer.Ordinal);
        return new CleRacineLocale(prefixeKid, usage, courante, versions);
    }

    public Task<CleDonneesEnveloppee> EnvelopperAsync(ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken)
    {
        var kid = _prefixeKid + _courante;
        var sortie = new byte[TailleNonce + TailleEtiquette + cleDonnees.Length];
        var nonce = sortie.AsSpan(0, TailleNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_versions[_courante], TailleEtiquette);
        aes.Encrypt(nonce, cleDonnees.Span, sortie.AsSpan(TailleNonce + TailleEtiquette), sortie.AsSpan(TailleNonce, TailleEtiquette), DonneesAssociees(kid));
        return Task.FromResult(new CleDonneesEnveloppee(kid, sortie));
    }

    public Task<byte[]> DesenvelopperAsync(string kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        if (!kid.StartsWith(_prefixeKid, StringComparison.Ordinal) || !_versions.TryGetValue(kid[_prefixeKid.Length..], out var cle))
        {
            throw new EnveloppeAltereeException($"Kid étranger à la clé racine locale ({_usage}) ou version inconnue.");
        }

        if (cleEnveloppee.Length <= TailleNonce + TailleEtiquette)
        {
            throw new EnveloppeAltereeException("Clé enveloppée tronquée.");
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
            throw new EnveloppeAltereeException("Clé enveloppée invalide.", ex);
        }

        return Task.FromResult(cleDonnees);
    }

    private byte[] DonneesAssociees(string kid) => Encoding.UTF8.GetBytes($"{_usage}|{kid}");
}
