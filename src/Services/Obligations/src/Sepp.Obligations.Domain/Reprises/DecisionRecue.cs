namespace Sepp.Obligations.Domain.Reprises;

/// <summary>
/// Décision reçue avant que l'examen qu'elle clôt ne soit connu (DecisionEmise avant ExamenCloture) : parquée par examen
/// puis appliquée au processus de reprise quand l'examen réalise l'obligation. La valeur la plus récente (OccurredAt) gagne.
/// </summary>
public sealed class DecisionRecue
{
    private DecisionRecue()
    {
    }

    public DecisionRecue(Guid examenId, Guid decisionId, Guid personneId, Guid affilieId, DateTimeOffset recueLe)
    {
        ExamenId = examenId;
        DecisionId = decisionId;
        PersonneId = personneId;
        AffilieId = affilieId;
        RecueLe = recueLe;
    }

    public Guid ExamenId { get; private set; }

    public Guid DecisionId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateTimeOffset RecueLe { get; private set; }

    /// <returns><c>false</c> si une décision plus récente est déjà connue.</returns>
    public bool Appliquer(Guid decisionId, DateTimeOffset recueLe)
    {
        if (recueLe < RecueLe)
        {
            return false;
        }

        var change = decisionId != DecisionId || recueLe != RecueLe;
        DecisionId = decisionId;
        RecueLe = recueLe;
        return change;
    }
}
