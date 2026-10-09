using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Domain.Calcul;

/// <summary>
/// SAN-04 : applique un calcul aux obligations existantes d'un travailleur. Une échéance attendue crée l'obligation de
/// même clé ou la met à jour ; une obligation ouverte qui n'est plus attendue est annulée (motif « recalcul ») ou
/// passe au statut « sorti de l'entreprise » si le travailleur a quitté l'affilié. Les rendez-vous de la Planification
/// sont ensuite reportés sur les statuts (planifié / à planifier). Le résultat ne dépend que de l'état des projections.
/// </summary>
public static class ReconciliationObligations
{
    /// <returns>Les obligations créées, à ajouter au dépôt.</returns>
    public static IReadOnlyList<Obligation> Appliquer(
        Guid personneId,
        IReadOnlyList<Obligation> existantes,
        ResultatCalcul resultat,
        IReadOnlyList<RendezVousLocal> rendezVous,
        DateTimeOffset maintenant)
    {
        var parCle = existantes.ToDictionary(o => o.Cle, StringComparer.Ordinal);
        var attendues = new HashSet<string>(StringComparer.Ordinal);
        var nouvelles = new List<Obligation>();

        foreach (var echeance in resultat.Echeances)
        {
            if (!attendues.Add(echeance.Cle))
            {
                continue;
            }

            if (parCle.TryGetValue(echeance.Cle, out var obligation))
            {
                obligation.Actualiser(echeance, maintenant);
            }
            else
            {
                nouvelles.Add(Obligation.Creer(personneId, echeance, maintenant));
            }
        }

        foreach (var obligation in existantes.Where(o => o.EstOuverte && !attendues.Contains(o.Cle)))
        {
            if (resultat.EstSortiDe(obligation.AffilieId))
            {
                obligation.SortirDeLEntreprise(maintenant);
            }
            else
            {
                obligation.Annuler(MotifAnnulation.Recalcul, maintenant);
            }
        }

        foreach (var obligation in existantes.Concat(nouvelles).Where(o => o.EstOuverte))
        {
            SynchroniserRendezVous(obligation, rendezVous);
        }

        return nouvelles;
    }

    /// <summary>
    /// Un rendez-vous actif (non annulé) qui couvre l'obligation la planifie ; si son rendez-vous a été annulé sans
    /// remplacement, une obligation planifiée ou convoquée redevient à planifier. Une absence ou une excuse notée pour
    /// un rendez-vous n'est pas effacée par ce même rendez-vous.
    /// </summary>
    public static void SynchroniserRendezVous(Obligation obligation, IReadOnlyList<RendezVousLocal> rendezVous)
    {
        var actif = rendezVous
            .Where(r => r.EstActif && r.ObligationIds.Contains(obligation.Id))
            .OrderByDescending(r => r.PlanifieDu)
            .ThenByDescending(r => r.RendezVousId)
            .FirstOrDefault();

        if (actif is { Absent: true })
        {
            // SAN-13 : absence constatée à ce rendez-vous ; l'obligation attend un nouveau rendez-vous.
            // Absence reçue avant le rendez-vous planifié : l'obligation passe par « planifié » puis « absent ».
            if (obligation.Statut == StatutObligation.APlanifier && obligation.RendezVousId is null)
            {
                obligation.Planifier(actif.RendezVousId, actif.Debut!.Value);
            }

            if (MachineEtatsObligation.EstPlanifie(obligation.Statut) && obligation.RendezVousId == actif.RendezVousId)
            {
                obligation.MarquerAbsent();
            }
        }
        else if (actif is not null)
        {
            if (actif.RendezVousId != obligation.RendezVousId
                || (MachineEtatsObligation.EstPlanifie(obligation.Statut) && obligation.DateRendezVous != actif.Debut))
            {
                obligation.Planifier(actif.RendezVousId, actif.Debut!.Value);
            }

            // SAN-10 : la convocation du rendez-vous a été remise au canal d'envoi (quel que soit l'ordre de réception).
            if (obligation.Statut == StatutObligation.Planifie && obligation.RendezVousId == actif.RendezVousId && actif.ConvocationAJour)
            {
                obligation.Convoquer();
            }
        }
        else if (MachineEtatsObligation.EstPlanifie(obligation.Statut))
        {
            obligation.LibererRendezVous();
        }
    }
}
