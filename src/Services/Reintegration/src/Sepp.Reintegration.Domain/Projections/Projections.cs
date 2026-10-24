namespace Sepp.Reintegration.Domain.Projections;

/// <summary>
/// Valeur d'un paramètre légal <c>REINTEGRATION.*</c> pour une période (<c>referentiels.parametre-legal-modifie</c>, ARC-21).
/// Clé : code et début de validité ; écriture par clé (idempotente).
/// </summary>
public sealed class ParametreLegalLocal
{
    private ParametreLegalLocal()
    {
    }

    public ParametreLegalLocal(string code, DateOnly valideDu, DateOnly? valideJusquAu, decimal valeur, string unite)
    {
        Code = code;
        ValideDu = valideDu;
        Appliquer(valideJusquAu, valeur, unite);
    }

    public string Code { get; private set; } = string.Empty;

    public DateOnly ValideDu { get; private set; }

    public DateOnly? ValideJusquAu { get; private set; }

    public decimal Valeur { get; private set; }

    public string Unite { get; private set; } = string.Empty;

    public bool EstApplicableAu(DateOnly date) => ValideDu <= date && (ValideJusquAu is null || date < ValideJusquAu);

    public void Appliquer(DateOnly? valideJusquAu, decimal valeur, string unite)
    {
        ValideJusquAu = valideJusquAu;
        Valeur = valeur;
        Unite = unite;
    }
}

/// <summary>
/// Jours fériés supplémentaires d'une année (<c>referentiels.jours-feries-modifies</c>, DAT-08), en plus des dix jours fériés
/// légaux calculés. Clé : année ; l'état le plus récent l'emporte.
/// </summary>
public sealed class CalendrierLocal
{
    private CalendrierLocal()
    {
    }

    public CalendrierLocal(int annee, IEnumerable<DateOnly> joursSupplementaires, DateTimeOffset evenementDu)
    {
        Annee = annee;
        JoursSupplementaires = [.. joursSupplementaires.Distinct().Order()];
        EvenementDu = evenementDu;
    }

    public int Annee { get; private set; }

    public IReadOnlyList<DateOnly> JoursSupplementaires { get; private set; } = [];

    public DateTimeOffset EvenementDu { get; private set; }

    public bool Appliquer(IEnumerable<DateOnly> joursSupplementaires, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        JoursSupplementaires = [.. joursSupplementaires.Distinct().Order()];
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>
/// SAN-63 : obligation d'Obligations liée à la réintégration (estimation du potentiel de travail, évaluation de réintégration),
/// pour reconnaître qu'un rendez-vous manqué (<c>planification.absence-rendez-vous-constatee</c>) concerne le trajet.
/// </summary>
public sealed class ObligationReintegration
{
    private ObligationReintegration()
    {
    }

    public ObligationReintegration(Guid obligationId, Guid personneId, Guid affilieId, string typeExamen)
    {
        ObligationId = obligationId;
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
    }

    public Guid ObligationId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public string TypeExamen { get; private set; } = string.Empty;
}
