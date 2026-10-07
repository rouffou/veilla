namespace Sepp.Planification.Domain.Projections;

/// <summary>
/// Modèle de lecture local des obligations à planifier, alimenté par <c>obligations.obligation-creee</c> et
/// <c>obligations.obligation-echue</c> (PLA-04, ARC-31). Identifiants, type d'examen et échéances uniquement (ARC-06).
/// Idempotent : clé = identifiant d'obligation, un événement plus ancien que l'état connu est ignoré.
/// </summary>
public sealed class ObligationAPlanifier
{
    private ObligationAPlanifier()
    {
    }

    public ObligationAPlanifier(Guid obligationId, Guid personneId, Guid affilieId, string typeExamen, DateOnly dateDue, DateOnly? dateLimite,
        DateTimeOffset evenementDu)
    {
        ObligationId = obligationId;
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        DateDue = dateDue;
        DateLimite = dateLimite;
        EvenementDu = evenementDu;
    }

    public Guid ObligationId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    /// <summary>Type d'examen communiqué par le service Obligations ; même espace de codes que les types d'acte des créneaux.</summary>
    public string TypeExamen { get; private set; } = string.Empty;

    public DateOnly DateDue { get; private set; }

    public DateOnly? DateLimite { get; private set; }

    /// <summary>La date limite est dépassée (obligations.obligation-echue).</summary>
    public bool Echue { get; private set; }

    /// <summary>Rendez-vous actif couvrant l'obligation, ou <c>null</c> si elle reste à planifier.</summary>
    public Guid? RendezVousId { get; private set; }

    /// <summary>PLA-06 : aucun créneau n'a pu être trouvé dans le délai légal.</summary>
    public bool UrgenceNonCouverte { get; private set; }

    public DateTimeOffset EvenementDu { get; private set; }

    public bool EstAPlanifier => RendezVousId is null;

    /// <returns><c>false</c> si l'événement est plus ancien que l'état connu (livraison désordonnée).</returns>
    public bool Appliquer(Guid personneId, Guid affilieId, string typeExamen, DateOnly dateDue, DateOnly? dateLimite, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        DateDue = dateDue;
        DateLimite = dateLimite;
        EvenementDu = evenementDu;
        return true;
    }

    public void MarquerEchue(DateOnly dateLimite)
    {
        Echue = true;
        DateLimite = dateLimite;
    }

    public void Couvrir(Guid rendezVousId)
    {
        RendezVousId = rendezVousId;
        UrgenceNonCouverte = false;
    }

    /// <summary>Le rendez-vous a été annulé ou manqué : l'obligation redevient à planifier.</summary>
    public void Decouvrir(Guid rendezVousId)
    {
        if (RendezVousId == rendezVousId)
        {
            RendezVousId = null;
        }
    }

    public void SignalerUrgenceNonCouverte() => UrgenceNonCouverte = true;
}

/// <summary>
/// Copie locale d'un paramètre légal du service Référentiels (<c>referentiels.parametre-legal-modifie</c>, ARC-21) :
/// délais des urgences (SANTE.REPRISE.DELAI…) et des rappels (CONVOCATION.RAPPEL_1/2). Clé : (code, date d'effet).
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
        ValideJusquAu = valideJusquAu;
        Valeur = valeur;
        Unite = unite;
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
