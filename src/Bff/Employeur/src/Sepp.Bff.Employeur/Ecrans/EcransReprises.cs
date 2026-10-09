using System.Net;

using Sepp.Bff.Employeur.Aval;

namespace Sepp.Bff.Employeur.Ecrans;

/// <summary>
/// POR-04 : annonce et suivi des reprises du travail (ARC-33), relayés en synchrone vers le service Obligations (ARC-30,
/// ADR 0008). Aucune règle métier : le délai, le statut et l'idempotence sont décidés par le service. Le BFF ne publie
/// pas <c>RepriseAnnoncee</c> et ne relaie aucun identifiant d'examen ni de décision.
/// </summary>
public sealed class EcransReprises(IObligationsApi obligations)
{
    public async Task<RepriseAnnoncee> AnnoncerAsync(Guid affilieId, RepriseAnnonceCorps corps, CancellationToken ct)
    {
        var resultat = await obligations.AnnoncerRepriseAsync(new RepriseCorpsAval(corps.PersonneId, affilieId, corps.DateReprise, corps.DebutAbsence), ct);
        return new RepriseAnnoncee(resultat.RepriseId, resultat.Cree, resultat.Statut);
    }

    public async Task<IReadOnlyList<RepriseEcran>> ListerAsync(Guid affilieId, CancellationToken ct) =>
        (await obligations.ListerReprisesAsync(affilieId, ct))
        .Where(r => r.AffilieId == affilieId)
        .OrderByDescending(r => r.DateReprise)
        .Select(ToEcran)
        .ToList();

    public async Task<RepriseEcran> ObtenirAsync(Guid affilieId, Guid repriseId, CancellationToken ct)
    {
        var reprise = await obligations.ObtenirRepriseAsync(repriseId, ct);

        // Une reprise d'un autre affilié est traitée comme inconnue, sans la confirmer.
        return reprise.AffilieId == affilieId
            ? ToEcran(reprise)
            : throw new ErreurAvalException(NomsServices.Obligations, HttpStatusCode.NotFound, "reprise.inconnue", $"La reprise {repriseId} n'existe pas pour cet affilié.");
    }

    private static RepriseEcran ToEcran(RepriseAval r) =>
        new(r.Id, r.PersonneId, r.DateReprise, r.DebutAbsence, r.Statut, r.DateLimite, r.EnRetard, r.HorsDelai);
}
