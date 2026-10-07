using Sepp.BuildingBlocks.Domain;

using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Domain.Calcul;

/// <summary>
/// Proposition d'un rendez-vous unique pour un travailleur chez un affilié (SAN-03) : toutes les obligations listées
/// peuvent être réalisées à <see cref="DateProposee"/>, au plus tard le <see cref="DateAuPlusTard"/>.
/// </summary>
public sealed record PropositionRendezVous(
    Guid PersonneId,
    Guid AffilieId,
    DateOnly DateProposee,
    DateOnly DateAuPlusTard,
    IReadOnlyList<Obligation> Obligations)
{
    public bool EstRegroupement => Obligations.Count > 1;
}

/// <summary>
/// SAN-03 : regroupement intelligent. Chaque obligation à (re)planifier dont l'échéance tombe dans la fenêtre a une
/// période de réalisation possible : depuis sa date due (anticipée de la fenêtre pour les examens périodiques, jamais
/// avant aujourd'hui ni avant un report) jusqu'à sa date limite (à défaut, sa date due plus la fenêtre). Les périodes
/// qui se recoupent sont couvertes par un seul rendez-vous, avec le moins de rendez-vous possible (algorithme glouton
/// par date au plus tard croissante, optimal pour des intervalles).
/// </summary>
public static class Regroupement
{
    public const int FenetreMaximaleJours = 365;

    public static IReadOnlyList<PropositionRendezVous> Proposer(IEnumerable<Obligation> obligations, DateOnly aujourdHui, int fenetreJours)
    {
        if (fenetreJours is < 0 or > FenetreMaximaleJours)
        {
            throw new DomainException($"La fenêtre de regroupement doit être comprise entre 0 et {FenetreMaximaleJours} jours.");
        }

        var horizon = aujourdHui.AddDays(fenetreJours);
        var periodes = obligations
            .Where(o => MachineEtatsObligation.EstAPlanifier(o.Statut))
            .Select(o => Periode(o, aujourdHui, fenetreJours))
            .Where(p => p.Debut <= horizon)
            .ToList();

        var propositions = new List<PropositionRendezVous>();
        foreach (var travailleur in periodes.GroupBy(p => (p.Obligation.PersonneId, p.Obligation.AffilieId)))
        {
            var restantes = travailleur
                .OrderBy(p => p.Fin)
                .ThenBy(p => p.Debut)
                .ThenBy(p => p.Obligation.Cle, StringComparer.Ordinal)
                .ToList();
            while (restantes.Count > 0)
            {
                var auPlusTard = restantes[0].Fin;
                var groupe = restantes.Where(p => p.Debut <= auPlusTard).ToList();
                restantes.RemoveAll(groupe.Contains);
                propositions.Add(new PropositionRendezVous(
                    travailleur.Key.PersonneId,
                    travailleur.Key.AffilieId,
                    groupe.Max(p => p.Debut),
                    auPlusTard,
                    groupe.OrderBy(p => p.Obligation.Echeance).ThenBy(p => p.Obligation.Cle, StringComparer.Ordinal).Select(p => p.Obligation).ToList()));
            }
        }

        return propositions.OrderBy(p => p.DateProposee).ThenBy(p => p.PersonneId).ToList();
    }

    private static PeriodeRealisation Periode(Obligation obligation, DateOnly aujourdHui, int fenetreJours)
    {
        var debut = obligation.Type.PeutEtreAnticipe() ? obligation.DateDue.AddDays(-fenetreJours) : obligation.DateDue;
        if (obligation.Statut == StatutObligation.Reporte && obligation.DateReport is { } report && report > debut)
        {
            debut = report;
        }

        debut = Max(debut, aujourdHui);
        var fin = Max(obligation.DateLimite ?? obligation.DateDue.AddDays(fenetreJours), debut);
        return new PeriodeRealisation(obligation, debut, fin);
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;

    private sealed record PeriodeRealisation(Obligation Obligation, DateOnly Debut, DateOnly Fin);
}
