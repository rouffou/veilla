namespace Sepp.SurveillanceMedicale.Domain.Projections;

// Modèles de lecture locaux alimentés par les événements des autres services (ARC-31, ARC-35) : identifiants, dates,
// codes et statuts uniquement (ARC-06, DAT-06). Écriture par clé : rejouer un événement ne duplique rien.

/// <summary>
/// SAN-20 : examen dû, projeté depuis <c>obligations.obligation-creee</c> ; satisfait à la clôture d'un examen qui le
/// couvre ; retiré par <c>obligations.obligation-cloturee</c> (annulation, sortie de l'entreprise). Le retrait est
/// logique : la ligne reste pour que l'ordre d'arrivée des messages soit sans effet (un <c>ObligationCreee</c> plus
/// ancien que le retrait ne rouvre pas l'examen dû ; un plus récent, publié quand l'obligation redevient due, le rouvre).
/// </summary>
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

    /// <summary>Date limite calculée par le service Obligations (délai légal, calendrier DAT-08) ; jamais recalculée ici.</summary>
    public DateOnly? DateLimite { get; private set; }

    /// <summary>Examen clôturé qui a satisfait l'obligation.</summary>
    public Guid? SatisfaiteParExamenId { get; private set; }

    /// <summary>Statut de clôture reçu d'Obligations (<c>Annule</c>, <c>SortiEntreprise</c>) ; <c>null</c> tant que l'examen est dû.</summary>
    public string? StatutRetrait { get; private set; }

    /// <summary>Horodatage (<c>OccurredAt</c>) de l'événement de clôture qui a retiré l'examen dû.</summary>
    public DateTimeOffset? RetireeLe { get; private set; }

    public bool EstRetiree => RetireeLe is not null;

    /// <summary>Obligation connue seulement par sa clôture (reçue avant sa création) : déjà retirée.</summary>
    public static ObligationDue Retiree(Guid obligationId, Guid personneId, Guid affilieId, string typeExamen, DateOnly date, string statut, DateTimeOffset evenementDu)
    {
        var obligation = new ObligationDue(obligationId, personneId, affilieId, typeExamen, date, null);
        obligation.Retirer(statut, evenementDu);
        return obligation;
    }

    /// <summary>
    /// État courant de l'obligation (<c>ObligationCreee</c>). Si l'examen dû a été retiré par une clôture plus ancienne
    /// que cet événement, l'obligation est redevenue due : le retrait est levé.
    /// </summary>
    public void Appliquer(Guid personneId, Guid affilieId, string typeExamen, DateOnly dateDue, DateOnly? dateLimite, DateTimeOffset? evenementDu = null)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        DateDue = dateDue;
        DateLimite = dateLimite;
        if (RetireeLe is { } retrait && evenementDu is { } du && du > retrait)
        {
            RetireeLe = null;
            StatutRetrait = null;
        }
    }

    /// <summary>
    /// Retrait de l'examen dû (<c>ObligationCloturee</c> : annulation ou sortie de l'entreprise). Idempotent : une
    /// clôture rejouée, ou plus ancienne que le retrait déjà connu, ne change rien (renvoie <c>false</c>).
    /// </summary>
    public bool Retirer(string statut, DateTimeOffset evenementDu)
    {
        if (RetireeLe is { } retrait && evenementDu <= retrait)
        {
            return false;
        }

        StatutRetrait = statut;
        RetireeLe = evenementDu;
        return true;
    }

    public void Satisfaire(Guid examenId) => SatisfaiteParExamenId ??= examenId;

    /// <summary>
    /// Respect du délai (§14.5, examen de reprise) : l'examen est hors délai si sa date sort de [<see cref="DateDue"/>,
    /// <see cref="DateLimite"/>], bornes incluses. Sans date limite connue, le respect est indéterminé (<c>null</c>).
    /// </summary>
    public bool? EstHorsDelai(DateOnly dateExamen) =>
        DateLimite is { } limite ? dateExamen < DateDue || dateExamen > limite : null;
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

/// <summary>
/// Jours fériés supplémentaires d'une année (<c>referentiels.jours-feries-modifies</c>, DAT-08), en plus des dix jours
/// fériés légaux calculés : jours de remplacement, fêtes des Communautés. Ils entrent dans les délais de concertation et
/// de recours en jours ouvrables (SAN-34). Clé : année ; l'état le plus récent l'emporte (un message plus ancien est ignoré).
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
