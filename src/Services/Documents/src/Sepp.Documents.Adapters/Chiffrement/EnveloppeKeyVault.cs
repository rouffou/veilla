using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Chiffrement;

/// <summary>
/// Enveloppe des clés de données par la clé racine <c>documents-kek</c> de la zone (ARC-44, ADR 0009) : clé RSA-HSM 3072
/// non exportable dans le Key Vault Premium ou le Managed HSM des zones sensibles, clé RSA dans le coffre standard.
/// <c>Documents:Enveloppe:CleKek</c> donne l'URI de la clé <b>sans version</b> : l'enveloppe utilise la version courante
/// (rotation automatique) et l'archive conserve le kid versionné, qui reste utilisable pour déballer après rotation.
/// </summary>
/// <remarks>
/// Une <see cref="CleRacineKeyVault"/> (socle commun, ADR 0012) par zone : le kid lu dans une archive est confronté à la
/// clé racine configurée pour la zone (même coffre, même nom, version alphanumérique) avant tout appel, pour qu'une archive
/// altérée ne puisse pas diriger l'identité managée vers un autre coffre, une autre clé ou une autre zone. Les erreurs du
/// socle sont traduites dans celles du port (<see cref="ContenuAltereException"/>, <see cref="CleRacineIndisponibleException"/>).
/// </remarks>
public sealed class EnveloppeKeyVault : IEnveloppeCles
{
    private readonly Dictionary<ZoneDocument, CleRacineKeyVault> _cles;

    public EnveloppeKeyVault(IReadOnlyDictionary<ZoneDocument, Uri> cles, IOperationsCleRacine operations)
    {
        _cles = cles.ToDictionary(c => c.Key, c => new CleRacineKeyVault(LireUriCle(c.Value.AbsoluteUri, c.Key), operations, Designation(c.Key)));
        var doublon = _cles.GroupBy(c => c.Value.Cle.AbsoluteUri.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null)
        {
            throw new InvalidOperationException($"La même clé racine est configurée pour les zones {string.Join(", ", doublon.Select(c => c.Key.Code()))} : une clé par zone (§14.3).");
        }
    }

    /// <summary>Contrôle l'URI de la clé racine : HTTPS, forme <c>https://&lt;coffre&gt;/keys/&lt;nom&gt;</c>, sans version.</summary>
    public static Uri LireUriCle(string? valeur, ZoneDocument zone) => CleRacineKeyVault.LireUriCle(valeur, Designation(zone));

    public async Task<CleEnveloppee> EnvelopperAsync(ZoneDocument zone, ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken)
    {
        var resultat = await Cle(zone).EnvelopperAsync(cleDonnees, cancellationToken);
        return new CleEnveloppee(resultat.Kid, resultat.Octets);
    }

    /// <remarks>
    /// Classement des refus du coffre (#377), fait par <see cref="CleRacineKeyVault"/> : 400 (clé enveloppée invalide)
    /// signale une archive altérée ; 404 (version supprimée ou purgée) et 401/403 (droits manquants, version désactivée)
    /// sont des erreurs d'exploitation, l'archive n'est pas en cause. Les autres statuts remontent tels quels.
    /// </remarks>
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
        catch (CleRacineInaccessibleException ex)
        {
            throw new CleRacineIndisponibleException(ex.Kid, ex.Cause == CauseCleRacineInaccessible.Introuvable
                ? CauseCleRacineIndisponible.Introuvable
                : CauseCleRacineIndisponible.AccesRefuse, ex.InnerException);
        }
    }

    private static string Designation(ZoneDocument zone) => $"Documents:Enveloppe:CleKek de la zone {zone.Code()}";

    private CleRacineKeyVault Cle(ZoneDocument zone) =>
        _cles.TryGetValue(zone, out var cle)
            ? cle
            : throw new InvalidOperationException($"La zone {zone.Code()} n'est pas servie par cette instance : aucune clé racine.");
}
