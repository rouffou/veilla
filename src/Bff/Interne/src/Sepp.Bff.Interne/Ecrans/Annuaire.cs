using System.Collections.Concurrent;

using Sepp.Bff.Interne.Aval;

namespace Sepp.Bff.Interne.Ecrans;

/// <summary>
/// Libellés d'affichage des identifiants pour les écrans composites (ADR 0013) : personne → « Prénom Nom », affilié →
/// dénomination. Le cache vit le temps d'une requête (service scoped) : un même identifiant n'est demandé qu'une fois par
/// écran, même en appels parallèles, et rien n'est conservé entre deux utilisateurs (les droits sont ceux du jeton). Un
/// libellé introuvable, refusé ou indisponible vaut <c>null</c> : l'écran affiche alors l'identifiant, sans échouer.
/// </summary>
public sealed class Annuaire(IPersonnesApi personnes, IAffiliesApi affilies)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<string?>>> _personnes = new();
    private readonly ConcurrentDictionary<Guid, Lazy<Task<string?>>> _affilies = new();

    /// <example><c>var nom = await annuaire.NomPersonneAsync(rendezVous.PersonneId, ct) ?? rendezVous.PersonneId.ToString();</c></example>
    public Task<string?> NomPersonneAsync(Guid personneId, CancellationToken ct) =>
        Resoudre(_personnes, personneId, async () =>
        {
            var p = await personnes.ObtenirPersonneAsync(personneId, ct);
            return $"{p.Prenom} {p.Nom}".Trim();
        });

    public Task<string?> DenominationAffilieAsync(Guid affilieId, CancellationToken ct) =>
        Resoudre(_affilies, affilieId, async () => (await affilies.ObtenirAffilieAsync(affilieId, ct)).Fiche.Denomination);

    /// <summary>Résout plusieurs personnes en parallèle (identifiants dédoublonnés).</summary>
    public async Task<IReadOnlyDictionary<Guid, string?>> NomsPersonnesAsync(IEnumerable<Guid> personneIds, CancellationToken ct)
    {
        var ids = personneIds.Distinct().ToList();
        var noms = await Task.WhenAll(ids.Select(id => NomPersonneAsync(id, ct)));
        return ids.Zip(noms).ToDictionary(x => x.First, x => x.Second);
    }

    private static Task<string?> Resoudre(ConcurrentDictionary<Guid, Lazy<Task<string?>>> cache, Guid id, Func<Task<string>> lecture) =>
        cache.GetOrAdd(id, _ => new Lazy<Task<string?>>(async () =>
        {
            try
            {
                return await lecture();
            }
            catch (Exception e) when (e is ErreurAvalException or ServiceIndisponibleException)
            {
                return null;
            }
        })).Value;
}
