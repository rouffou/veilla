namespace Sepp.PostesRisques.Domain.Projections;

/// <summary>
/// Modèle de lecture local des affectations travailleur ↔ poste, alimenté par l'événement AffectationModifiee
/// du service Personnes (AFF-30). Aucune donnée d'identité : identifiants et dates uniquement (ARC-06, DAT-06).
/// Idempotent : clé = identifiant d'affectation, un événement plus ancien que l'état connu est ignoré.
/// </summary>
public sealed class AffectationPoste
{
    private AffectationPoste()
    {
    }

    public AffectationPoste(Guid affectationId, Guid personneId, Guid posteId, DateOnly dateDebut, DateOnly? dateFin, DateTimeOffset evenementDu)
    {
        AffectationId = affectationId;
        PersonneId = personneId;
        PosteId = posteId;
        DateDebut = dateDebut;
        DateFin = dateFin;
        EvenementDu = evenementDu;
    }

    public Guid AffectationId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid PosteId { get; private set; }

    public DateOnly DateDebut { get; private set; }

    /// <summary>Dernier jour d'affectation (inclus), ou <c>null</c> si l'affectation est en cours.</summary>
    public DateOnly? DateFin { get; private set; }

    /// <summary>Horodatage de l'événement source le plus récent appliqué.</summary>
    public DateTimeOffset EvenementDu { get; private set; }

    public bool EstActiveAu(DateOnly date) => DateDebut <= date && (DateFin is null || date <= DateFin);

    /// <returns><c>false</c> si l'événement est plus ancien que l'état connu (livraison désordonnée).</returns>
    public bool Appliquer(Guid personneId, Guid posteId, DateOnly dateDebut, DateOnly? dateFin, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        PersonneId = personneId;
        PosteId = posteId;
        DateDebut = dateDebut;
        DateFin = dateFin;
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>
/// Modèle de lecture des examens clôturés (événement ExamenCloture de la Surveillance médicale) : type et date
/// uniquement, jamais de contenu clinique (ARC-06). Sert à la « date de la dernière évaluation » des listes (AFF-30).
/// </summary>
public sealed class ExamenRealise
{
    private ExamenRealise()
    {
    }

    public ExamenRealise(Guid examenId, Guid personneId, Guid affilieId, string typeExamen, DateOnly date)
    {
        ExamenId = examenId;
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        Date = date;
    }

    public Guid ExamenId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public string TypeExamen { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public void Appliquer(Guid personneId, Guid affilieId, string typeExamen, DateOnly date)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        Date = date;
    }
}

/// <summary>
/// Copie locale d'un paramètre légal du service Référentiels (événement ParametreLegalModifie), pour les
/// paramètres utilisés par ce service (par ex. SANTE.LISTES_NOMINATIVES.CONSERVATION, ARC-21).
/// Clé : (code, date d'effet) ; rejouer l'événement réécrit la même ligne.
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
