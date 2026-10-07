namespace Sepp.Planification.Domain.Sessions;

/// <summary>Personne à voir, avec les obligations qu'un seul rendez-vous couvrira (SAN-03).</summary>
public sealed record BesoinPersonne(Guid PersonneId, Guid AffilieId, IReadOnlyList<Guid> ObligationIds, DateOnly? DateLimite);

/// <summary>Personnes à voir sur un site (lieu) donné.</summary>
public sealed record BesoinSite(Guid LieuId, Coordonnees? Position, IReadOnlyList<BesoinPersonne> Personnes);

/// <summary>Session proposée par la planification automatique ; le planificateur la confirme ou l'ajuste.</summary>
public sealed record SessionProposee(
    int Ordre,
    DateOnly Date,
    Guid LieuId,
    IReadOnlyList<BesoinPersonne> Personnes,
    double? DistanceDepuisPrecedentKm,
    bool HorsDelai);

/// <summary>
/// PLA-04 : heuristique de proposition de sessions, volontairement simple et explicable (aucune optimisation opaque).
/// <list type="number">
/// <item><b>Regroupement par site</b> : les obligations dues sont regroupées par personne (un rendez-vous couvre toutes
/// ses obligations, SAN-03), puis les personnes par site ; un site est découpé en sessions de <c>capacité</c> personnes,
/// les échéances les plus proches d'abord.</item>
/// <item><b>Ordre de visite (plus proche voisin)</b> : depuis le point de départ (centre ou dépôt de l'unité mobile),
/// on visite toujours le site non encore visité le plus proche (distance orthodromique) ; les sites sans position sont
/// placés à la fin, par échéance. Toutes les sessions d'un site se suivent : l'équipe ne revient pas sur ses pas.</item>
/// <item><b>Calendrier</b> : une session par jour ouvrable (hors week-ends et jours fériés) à partir de la date de début,
/// pour une équipe ou une unité mobile.</item>
/// <item><b>Contrôle des délais</b> : une session est signalée « hors délai » si elle tombe après la date limite d'une
/// des obligations qu'elle couvre ; le planificateur peut alors l'avancer ou utiliser un créneau d'urgence.</item>
/// </list>
/// </summary>
public static class PlanificateurSessions
{
    public static IReadOnlyList<SessionProposee> Proposer(IEnumerable<BesoinSite> sites, Coordonnees? depart, DateOnly premierJour, int capacite,
        Func<DateOnly, bool> estOuvrable)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacite, 1);

        var restants = sites.Where(s => s.Personnes.Count > 0).ToList();
        var ordreSites = new List<(BesoinSite Site, double? Distance)>();
        var position = depart;
        while (restants.Count > 0)
        {
            var avecPosition = restants.Where(s => s.Position is not null).ToList();
            BesoinSite suivant;
            double? distance = null;
            if (avecPosition.Count > 0 && position is { } courante)
            {
                suivant = avecPosition.MinBy(s => (s.Position!.Value.DistanceKm(courante), PlusProcheEcheance(s), s.LieuId))!;
                distance = suivant.Position!.Value.DistanceKm(courante);
            }
            else if (avecPosition.Count > 0)
            {
                // Pas de point de départ : on commence par le site dont l'échéance est la plus proche.
                suivant = avecPosition.MinBy(s => (PlusProcheEcheance(s), s.LieuId))!;
            }
            else
            {
                suivant = restants.MinBy(s => (PlusProcheEcheance(s), s.LieuId))!;
            }

            ordreSites.Add((suivant, distance));
            position = suivant.Position ?? position;
            restants.Remove(suivant);
        }

        var propositions = new List<SessionProposee>();
        var jour = premierJour;
        foreach (var (site, distance) in ordreSites)
        {
            var personnes = site.Personnes
                .OrderBy(p => p.DateLimite ?? DateOnly.MaxValue)
                .ThenBy(p => p.PersonneId)
                .ToList();
            for (var i = 0; i < personnes.Count; i += capacite)
            {
                while (!estOuvrable(jour))
                {
                    jour = jour.AddDays(1);
                }

                var lot = personnes.Skip(i).Take(capacite).ToList();
                propositions.Add(new SessionProposee(
                    propositions.Count + 1,
                    jour,
                    site.LieuId,
                    lot,
                    i == 0 ? distance : 0,
                    lot.Any(p => p.DateLimite is { } limite && limite < jour)));
                jour = jour.AddDays(1);
            }
        }

        return propositions;
    }

    /// <summary>Distance totale estimée de la tournée proposée (kilomètres, hors retour au départ).</summary>
    public static double DistanceTotaleKm(IEnumerable<SessionProposee> sessions) => sessions.Sum(s => s.DistanceDepuisPrecedentKm ?? 0);

    private static DateOnly PlusProcheEcheance(BesoinSite site) =>
        site.Personnes.Select(p => p.DateLimite ?? DateOnly.MaxValue).DefaultIfEmpty(DateOnly.MaxValue).Min();
}
