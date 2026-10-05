namespace Sepp.SurveillanceMedicale.Domain.Projections;

// Modèles de lecture locaux alimentés par les événements des autres services (ARC-31, ARC-35) : identifiants, dates,
// codes et statuts uniquement (ARC-06, DAT-06). Écriture par clé : rejouer un événement ne duplique rien.

/// <summary>SAN-20 : examen dû, projeté depuis <c>obligations.obligation-creee</c> ; satisfait à la clôture d'un examen qui le couvre.</summary>
public sealed class ObligationDue
{
    private ObligationDue()
    {
    }

    public ObligationDue(Guid obligationId, Guid personneId, Guid affilieId, string typeExamen, DateOnly dateDue, DateOnly? dateLimite)
    {
        ObligationId = obligationId;
        Appliquer(personneId, affilieId, typeExamen, dateDue, dateLimite);
    }

    public Guid ObligationId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public string TypeExamen { get; private set; } = string.Empty;

    public DateOnly DateDue { get; private set; }

    public DateOnly? DateLimite { get; private set; }

    /// <summary>Examen clôturé qui a satisfait l'obligation.</summary>
    public Guid? SatisfaiteParExamenId { get; private set; }

    public void Appliquer(Guid personneId, Guid affilieId, string typeExamen, DateOnly dateDue, DateOnly? dateLimite)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        DateDue = dateDue;
        DateLimite = dateLimite;
    }

    public void Satisfaire(Guid examenId) => SatisfaiteParExamenId ??= examenId;
}

/// <summary>Rendez-vous planifié (<c>planification.rendez-vous-planifie</c>) : convocation de la personne, base de la relation de soin.</summary>
public sealed class RendezVousPrevu
{
    private readonly List<Guid> _obligationIds = [];

    private RendezVousPrevu()
    {
    }

    public RendezVousPrevu(Guid rendezVousId, Guid personneId, Guid affilieId, DateTimeOffset debut, IEnumerable<Guid> obligationIds)
    {
        RendezVousId = rendezVousId;
        Appliquer(personneId, affilieId, debut, obligationIds);
    }

    public Guid RendezVousId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateTimeOffset Debut { get; private set; }

    public IReadOnlyList<Guid> ObligationIds => _obligationIds.AsReadOnly();

    public void Appliquer(Guid personneId, Guid affilieId, DateTimeOffset debut, IEnumerable<Guid> obligationIds)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        Debut = debut;
        _obligationIds.Clear();
        _obligationIds.AddRange(obligationIds.Distinct());
    }
}

/// <summary>
/// SAN-20 : affectation de la personne à un poste (<c>personnes.affectation-modifiee</c>). <see cref="DateFin"/> est la
/// borne exclusive (DAT-04). Un événement plus ancien que l'état connu est ignoré.
/// </summary>
public sealed class AffectationPersonne
{
    private AffectationPersonne()
    {
    }

    public AffectationPersonne(Guid affectationId, Guid personneId, Guid posteId, DateOnly dateDebut, DateOnly? dateFin, DateTimeOffset evenementDu)
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

    public DateOnly? DateFin { get; private set; }

    public DateTimeOffset EvenementDu { get; private set; }

    public bool EstActiveAu(DateOnly date) => DateDebut <= date && (DateFin is null || date < DateFin);

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

/// <summary>SAN-20 : risques d'un poste à partir d'une date (<c>postes-risques.profil-risque-poste-modifie</c>), clé (poste, date d'effet).</summary>
public sealed class ProfilRisquePoste
{
    private readonly List<string> _codesRisques = [];

    private ProfilRisquePoste()
    {
    }

    public ProfilRisquePoste(Guid posteId, Guid affilieId, DateOnly valideDu, IEnumerable<string> codesRisques)
    {
        PosteId = posteId;
        ValideDu = valideDu;
        Appliquer(affilieId, codesRisques);
    }

    public Guid PosteId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly ValideDu { get; private set; }

    public IReadOnlyList<string> CodesRisques => _codesRisques.AsReadOnly();

    public void Appliquer(Guid affilieId, IEnumerable<string> codesRisques)
    {
        AffilieId = affilieId;
        _codesRisques.Clear();
        _codesRisques.AddRange(codesRisques.Distinct(StringComparer.Ordinal));
    }
}

/// <summary>SAN-40 : mesurage d'exposition d'un groupe (<c>prevention.mesurage-enregistre</c>), versé aux dossiers des personnes rattachées.</summary>
public sealed class MesurageExposition
{
    private MesurageExposition()
    {
    }

    public MesurageExposition(Guid mesurageId, Guid groupeExpositionId, Guid affilieId, string agent, string niveau, DateOnly date)
    {
        MesurageId = mesurageId;
        GroupeExpositionId = groupeExpositionId;
        AffilieId = affilieId;
        Agent = agent;
        Niveau = niveau;
        Date = date;
    }

    public Guid MesurageId { get; private set; }

    public Guid GroupeExpositionId { get; private set; }

    public Guid AffilieId { get; private set; }

    public string Agent { get; private set; } = string.Empty;

    public string Niveau { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }
}

/// <summary>Copie locale d'un paramètre légal (<c>referentiels.parametre-legal-modifie</c>), clé (code, date d'effet), ARC-21.</summary>
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
