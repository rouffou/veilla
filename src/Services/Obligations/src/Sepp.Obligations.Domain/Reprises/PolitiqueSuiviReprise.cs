namespace Sepp.Obligations.Domain.Reprises;

/// <summary>
/// Seuils de suivi du processus de reprise (ARC-33, POR-04). Ce ne sont pas des délais légaux (ceux-ci viennent des
/// politiques légales, ARC-21) mais des réglages d'organisation lus dans la configuration <c>Obligations:Reprise</c>.
/// Toutes les valeurs par défaut sont provisoires et <b>à valider</b> par les départements médical et juridique.
/// </summary>
/// <param name="AlerteAvantEcheanceJoursOuvrables">
/// Alerte « échéance menacée » à N jours ouvrables avant la date limite (SANTE.REPRISE.ALERTE_AVANT_ECHEANCE, à valider) ;
/// 0 ou moins : désactivée.
/// </param>
/// <param name="RendezVousSansClotureJoursOuvrables">Alerte si le rendez-vous est passé de N jours ouvrables sans clôture d'examen (1 par le plan).</param>
/// <param name="DelaiDecisionJoursOuvrables">Alerte « décision en attente » N jours ouvrables après la clôture de l'examen (à valider) ; <c>null</c> : désactivée.</param>
/// <param name="ExpirationJours">Expiration administrative N jours après la date de reprise (à valider) ; <c>null</c> : désactivée.</param>
public sealed record PolitiqueSuiviReprise(
    int AlerteAvantEcheanceJoursOuvrables = 2,
    int RendezVousSansClotureJoursOuvrables = 1,
    int? DelaiDecisionJoursOuvrables = null,
    int? ExpirationJours = null)
{
    public static PolitiqueSuiviReprise ParDefaut { get; } = new();
}
