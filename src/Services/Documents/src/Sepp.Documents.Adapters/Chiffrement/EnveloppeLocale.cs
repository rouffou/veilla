using System.Security.Cryptography;

using Microsoft.Extensions.Configuration;

using Sepp.BuildingBlocks.Infrastructure.Security;
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
/// Une <see cref="CleRacineLocale"/> (socle commun, ADR 0012) par zone. Kid : <c>local:&lt;zone&gt;:&lt;version&gt;</c>.
/// Clé enveloppée : nonce (12 octets) | étiquette (16 octets) | clé chiffrée ; données associées :
/// <c>documents-kek|&lt;kid&gt;</c> (format inchangé par la factorisation).
/// </remarks>
public sealed class EnveloppeLocale : IEnveloppeCles
{
    private const int TailleCle = 32;

    private readonly Dictionary<ZoneDocument, CleRacineLocale> _cles;

    public EnveloppeLocale(IReadOnlyDictionary<ZoneDocument, (string Courante, Dictionary<string, byte[]> Versions)> cles)
    {
        var empreintes = new Dictionary<string, ZoneDocument>(StringComparer.Ordinal);
        foreach (var (zone, (courante, versions)) in cles)
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

        _cles = cles.ToDictionary(c => c.Key, c => new CleRacineLocale($"local:{c.Key.Code()}:", "documents-kek", c.Value.Courante, c.Value.Versions));
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

    public async Task<CleEnveloppee> EnvelopperAsync(ZoneDocument zone, ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken)
    {
        var resultat = await Cle(zone).EnvelopperAsync(cleDonnees, cancellationToken);
        return new CleEnveloppee(resultat.Kid, resultat.Octets);
    }

    public async Task<byte[]> DesenvelopperAsync(ZoneDocument zone, string kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
    {
        var cle = Cle(zone);
        try
        {
            return await cle.DesenvelopperAsync(kid, cleEnveloppee, cancellationToken);
        }
        catch (EnveloppeAltereeException ex)
        {
            throw ex.InnerException is null ? new ContenuAltereException(ex.Message) : new ContenuAltereException(ex.Message, ex.InnerException);
        }
    }

    private CleRacineLocale Cle(ZoneDocument zone) =>
        _cles.TryGetValue(zone, out var cle)
            ? cle
            : throw new InvalidOperationException($"La zone {zone.Code()} n'est pas servie par cette instance : aucune clé racine.");
}
