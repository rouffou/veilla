namespace Sepp.Obligations.Domain.Projections;

// Modèles de lecture locaux alimentés par les événements des autres services (ARC-31) : le moteur d'échéances ne fait
// aucun appel synchrone (SAN-01). Chaque projection est une écriture par clé ; quand un même objet peut être republié,
// l'horodatage de l'événement (OccurredAt) départage les livraisons désordonnées : rejouer ou réordonner les événements
// donne le même état. Identifiants, dates, statuts et catégories uniquement (ARC-06).

/// <summary>Affectation travailleur ↔ poste (personnes.affectation-modifiee). <see cref="DateFin"/> est exclusive (DAT-04).</summary>
public sealed class AffectationLocale
{
    private AffectationLocale()
    {
    }

    public AffectationLocale(Guid affectationId, Guid personneId, Guid posteId, DateOnly dateDebut, DateOnly? dateFin, DateTimeOffset evenementDu)
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

    /// <summary>Borne exclusive de la période, <c>null</c> si l'affectation est en cours.</summary>
    public DateOnly? DateFin { get; private set; }

    public DateTimeOffset EvenementDu { get; private set; }

    public bool EstActiveAu(DateOnly date) => DateDebut <= date && (DateFin is null || date < DateFin);

    /// <returns><c>false</c> si l'événement est plus ancien que l'état connu.</returns>
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
/// Profil de risques d'un poste à partir d'une date (postes-risques.profil-risque-poste-modifie) : codes des risques
/// validés par le CPMT, en vigueur jusqu'au profil suivant. Clé : (poste, date d'effet).
/// </summary>
public sealed class ProfilRisquePosteLocal
{
    private ProfilRisquePosteLocal()
    {
    }

    public ProfilRisquePosteLocal(Guid posteId, DateOnly valideDu, Guid affilieId, IEnumerable<string> codesRisques, DateTimeOffset evenementDu)
    {
        PosteId = posteId;
        ValideDu = valideDu;
        AffilieId = affilieId;
        CodesRisques = Codes.Normaliser(codesRisques);
        EvenementDu = evenementDu;
    }

    public Guid PosteId { get; private set; }

    public DateOnly ValideDu { get; private set; }

    public Guid AffilieId { get; private set; }

    public IReadOnlyList<string> CodesRisques { get; private set; } = [];

    public DateTimeOffset EvenementDu { get; private set; }

    public bool Appliquer(Guid affilieId, IEnumerable<string> codesRisques, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        AffilieId = affilieId;
        CodesRisques = Codes.Normaliser(codesRisques);
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>
/// Version d'une règle de surveillance d'un risque (postes-risques.regle-surveillance-modifiee, AFF-12) : type de
/// surveillance, fréquence, surveillance prolongée. Clé : (code du risque, version) ; une version ne change pas.
/// </summary>
public sealed class RegleSurveillanceLocale
{
    private RegleSurveillanceLocale()
    {
    }

    public RegleSurveillanceLocale(
        string codeRisque, int version, Guid risqueId, string categorie, string typeSurveillance, int? frequenceMois, bool surveillanceProlongee, DateOnly valideDu)
    {
        CodeRisque = Codes.Normaliser(codeRisque);
        Version = version;
        RisqueId = risqueId;
        Categorie = categorie;
        TypeSurveillance = typeSurveillance;
        FrequenceMois = frequenceMois;
        SurveillanceProlongee = surveillanceProlongee;
        ValideDu = valideDu;
    }

    public string CodeRisque { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public Guid RisqueId { get; private set; }

    public string Categorie { get; private set; } = string.Empty;

    /// <summary><c>EvaluationSantePeriodique</c>, <c>ActesMedicauxSupplementaires</c> ou <c>EvaluationPrealableUniquement</c>.</summary>
    public string TypeSurveillance { get; private set; } = string.Empty;

    public int? FrequenceMois { get; private set; }

    public bool SurveillanceProlongee { get; private set; }

    public DateOnly ValideDu { get; private set; }

    public void Appliquer(Guid risqueId, string categorie, string typeSurveillance, int? frequenceMois, bool surveillanceProlongee, DateOnly valideDu)
    {
        RisqueId = risqueId;
        Categorie = categorie;
        TypeSurveillance = typeSurveillance;
        FrequenceMois = frequenceMois;
        SurveillanceProlongee = surveillanceProlongee;
        ValideDu = valideDu;
    }
}

/// <summary>
/// Surcharge de fréquence du CPMT (postes-risques.surcharge-frequence-definie, AFF-13), republiée à sa clôture.
/// <see cref="ValideJusquAu"/> est exclusive (DAT-04). Clé : identifiant de la surcharge.
/// </summary>
public sealed class SurchargeFrequenceLocale
{
    public const string CiblePoste = "Poste";
    public const string CibleGroupe = "Groupe";
    public const string CiblePersonne = "Personne";

    private SurchargeFrequenceLocale()
    {
    }

    public SurchargeFrequenceLocale(
        Guid surchargeId, Guid affilieId, string cibleType, Guid cibleId, string codeRisque, int frequenceMois, DateOnly valideDu, DateOnly? valideJusquAu,
        DateTimeOffset evenementDu)
    {
        SurchargeId = surchargeId;
        AffilieId = affilieId;
        CibleType = cibleType;
        CibleId = cibleId;
        CodeRisque = Codes.Normaliser(codeRisque);
        FrequenceMois = frequenceMois;
        ValideDu = valideDu;
        ValideJusquAu = valideJusquAu;
        EvenementDu = evenementDu;
    }

    public Guid SurchargeId { get; private set; }

    public Guid AffilieId { get; private set; }

    /// <summary><c>Poste</c>, <c>Groupe</c> ou <c>Personne</c>.</summary>
    public string CibleType { get; private set; } = string.Empty;

    public Guid CibleId { get; private set; }

    public string CodeRisque { get; private set; } = string.Empty;

    public int FrequenceMois { get; private set; }

    public DateOnly ValideDu { get; private set; }

    public DateOnly? ValideJusquAu { get; private set; }

    public DateTimeOffset EvenementDu { get; private set; }

    public bool EstApplicableAu(DateOnly date) => ValideDu <= date && (ValideJusquAu is null || date < ValideJusquAu);

    public bool Cible(string cibleType) => string.Equals(CibleType, cibleType, StringComparison.OrdinalIgnoreCase);

    public bool Appliquer(Guid affilieId, string cibleType, Guid cibleId, string codeRisque, int frequenceMois, DateOnly valideDu, DateOnly? valideJusquAu, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        AffilieId = affilieId;
        CibleType = cibleType;
        CibleId = cibleId;
        CodeRisque = Codes.Normaliser(codeRisque);
        FrequenceMois = frequenceMois;
        ValideDu = valideDu;
        ValideJusquAu = valideJusquAu;
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>
/// Occupation d'un travailleur chez un employeur (personnes.occupation-debutee / -terminee). Les deux événements
/// complètent la même ligne, quel que soit leur ordre d'arrivée. <see cref="DateFin"/> est le dernier jour (inclus).
/// </summary>
public sealed class OccupationLocale
{
    private OccupationLocale()
    {
    }

    public OccupationLocale(Guid occupationId, Guid personneId, Guid affilieId)
    {
        OccupationId = occupationId;
        PersonneId = personneId;
        AffilieId = affilieId;
    }

    public Guid OccupationId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly? DateDebut { get; private set; }

    /// <summary>Dernier jour d'occupation (inclus, comme DIMONA), <c>null</c> tant que la fin n'est pas connue.</summary>
    public DateOnly? DateFin { get; private set; }

    public DateTimeOffset? FinRecueDu { get; private set; }

    /// <summary>L'occupation n'est pas terminée à la date (en cours ou à venir).</summary>
    public bool EstEnCoursOuAVenirAu(DateOnly date) => DateFin is null || DateFin >= date;

    public void Debuter(DateOnly dateDebut) => DateDebut = dateDebut;

    public bool Terminer(DateOnly dateFin, DateTimeOffset evenementDu)
    {
        if (FinRecueDu is { } connue && evenementDu < connue)
        {
            return false;
        }

        DateFin = dateFin;
        FinRecueDu = evenementDu;
        return true;
    }
}

/// <summary>
/// État particulier (personnes.etat-particulier-declare, AFF-23, AFF-24) : catégorie générique uniquement
/// (<c>PROTECTION_MATERNITE</c>, <c>TRAVAIL_DE_NUIT</c>, <c>JEUNE_TRAVAILLEUR</c>) et période (fin incluse).
/// </summary>
public sealed class EtatParticulierLocal
{
    public const string ProtectionMaternite = "PROTECTION_MATERNITE";

    private EtatParticulierLocal()
    {
    }

    public EtatParticulierLocal(Guid etatParticulierId, Guid personneId, string categorie, DateOnly dateDebut, DateOnly? dateFin, DateTimeOffset evenementDu)
    {
        EtatParticulierId = etatParticulierId;
        PersonneId = personneId;
        Categorie = categorie;
        DateDebut = dateDebut;
        DateFin = dateFin;
        EvenementDu = evenementDu;
    }

    public Guid EtatParticulierId { get; private set; }

    public Guid PersonneId { get; private set; }

    public string Categorie { get; private set; } = string.Empty;

    public DateOnly DateDebut { get; private set; }

    /// <summary>Dernier jour de la période (inclus), <c>null</c> si elle est en cours.</summary>
    public DateOnly? DateFin { get; private set; }

    public DateTimeOffset EvenementDu { get; private set; }

    public bool EstProtectionMaternite => string.Equals(Categorie, ProtectionMaternite, StringComparison.OrdinalIgnoreCase);

    public bool Appliquer(Guid personneId, string categorie, DateOnly dateDebut, DateOnly? dateFin, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        PersonneId = personneId;
        Categorie = categorie;
        DateDebut = dateDebut;
        DateFin = dateFin;
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>Examen clôturé (surveillance-medicale.examen-cloture) : type et date uniquement, jamais de contenu clinique.</summary>
public sealed class ExamenLocal
{
    private ExamenLocal()
    {
    }

    public ExamenLocal(Guid examenId, Guid personneId, Guid affilieId, string typeExamen, DateOnly date)
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

    /// <summary>Code du type d'examen (<c>EVALUATION_PERIODIQUE</c>…), voir <see cref="Obligations.TypesObligation"/>.</summary>
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
/// Valeur d'un paramètre légal du service Référentiels (referentiels.parametre-legal-modifie, ARC-21).
/// Clé : (code, date d'effet).
/// </summary>
public sealed class ParametreLegalLocal
{
    private ParametreLegalLocal()
    {
    }

    public ParametreLegalLocal(string code, DateOnly valideDu, DateOnly? valideJusquAu, decimal valeur, string unite, DateTimeOffset evenementDu)
    {
        Code = code;
        ValideDu = valideDu;
        ValideJusquAu = valideJusquAu;
        Valeur = valeur;
        Unite = unite;
        EvenementDu = evenementDu;
    }

    public string Code { get; private set; } = string.Empty;

    public DateOnly ValideDu { get; private set; }

    public DateOnly? ValideJusquAu { get; private set; }

    public decimal Valeur { get; private set; }

    public string Unite { get; private set; } = string.Empty;

    public DateTimeOffset EvenementDu { get; private set; }

    public bool Appliquer(DateOnly? valideJusquAu, decimal valeur, string unite, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        ValideJusquAu = valideJusquAu;
        Valeur = valeur;
        Unite = unite;
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>
/// Jours fériés supplémentaires d'une année (referentiels.jours-feries-modifies, DAT-08), en plus des dix jours fériés
/// légaux calculés : jours de remplacement, fêtes des Communautés. Clé : année ; l'état le plus récent l'emporte.
/// </summary>
public sealed class CalendrierLocal
{
    private CalendrierLocal()
    {
    }

    public CalendrierLocal(int annee, IEnumerable<DateOnly> joursSupplementaires, DateTimeOffset evenementDu)
    {
        Annee = annee;
        JoursSupplementaires = joursSupplementaires.Distinct().Order().ToList();
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

        JoursSupplementaires = joursSupplementaires.Distinct().Order().ToList();
        EvenementDu = evenementDu;
        return true;
    }
}

/// <summary>
/// Rendez-vous de la Planification (planification.rendez-vous-planifie / -annule) et obligations qu'il couvre.
/// L'annulation est définitive : un « planifié » reçu après l'annulation ne la défait pas.
/// </summary>
public sealed class RendezVousLocal
{
    private RendezVousLocal()
    {
    }

    public RendezVousLocal(Guid rendezVousId, Guid personneId)
    {
        RendezVousId = rendezVousId;
        PersonneId = personneId;
    }

    public Guid RendezVousId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid? AffilieId { get; private set; }

    public DateTimeOffset? Debut { get; private set; }

    public IReadOnlyList<Guid> ObligationIds { get; private set; } = [];

    public bool Annule { get; private set; }

    /// <summary>Motif d'annulation reçu (<c>RendezVousAnnule.Motif</c>) : <c>ObligationLevee</c> = compensation, pas de replanification.</summary>
    public string? MotifAnnulation { get; private set; }

    public DateTimeOffset? PlanifieDu { get; private set; }

    /// <summary>SAN-13 : la personne ne s'est pas présentée (planification.absence-rendez-vous-constatee), définitif.</summary>
    public bool Absent { get; private set; }

    /// <summary>
    /// PLA-07 : date de la dernière replanification (<c>RendezVousReplanifie.OccurredAt</c>) : un « planifié » plus ancien ne
    /// ramène pas le rendez-vous à son ancienne date.
    /// </summary>
    public DateTimeOffset? ReplanifieDu { get; private set; }

    /// <summary>Dernière convocation remise au canal d'envoi (planification.convocation-envoyee, SAN-10).</summary>
    public DateTimeOffset? ConvocationEnvoyeeLe { get; private set; }

    /// <summary>Dernière convocation abandonnée (planification.convocation-non-remise, SAN-10).</summary>
    public DateTimeOffset? ConvocationNonRemiseLe { get; private set; }

    public bool EstActif => !Annule && Debut is not null;

    /// <summary>
    /// La convocation envoyée est postérieure à la dernière (re)planification : elle concerne bien la date actuelle. Une
    /// replanification sans nouvelle convocation remet l'obligation à « planifié ».
    /// </summary>
    public bool ConvocationAJour
    {
        get
        {
            if (ConvocationEnvoyeeLe is not { } envoyee)
            {
                return false;
            }

            var reference = PlanifieDu ?? DateTimeOffset.MinValue;
            if (ReplanifieDu is { } replanifie && replanifie > reference)
            {
                reference = replanifie;
            }

            return envoyee >= reference;
        }
    }

    /// <summary>La dernière convocation a été abandonnée et n'a pas été remplacée par un envoi plus récent.</summary>
    public bool ConvocationNonRemise =>
        ConvocationNonRemiseLe is { } abandonnee && (ConvocationEnvoyeeLe is not { } envoyee || abandonnee > envoyee);

    public bool Planifier(Guid affilieId, DateTimeOffset debut, IEnumerable<Guid> obligationIds, DateTimeOffset evenementDu)
    {
        if (PlanifieDu is { } connu && evenementDu < connu)
        {
            return false;
        }

        AffilieId = affilieId;
        ObligationIds = obligationIds.Distinct().ToList();
        PlanifieDu = evenementDu;
        if (ReplanifieDu is not { } replanifie || evenementDu >= replanifie)
        {
            Debut = debut;
        }

        return true;
    }

    /// <summary>PLA-07 : le rendez-vous est déplacé ; l'événement le plus récent l'emporte.</summary>
    /// <returns><c>false</c> si un état plus récent est déjà connu.</returns>
    public bool Replanifier(DateTimeOffset nouveauDebut, DateTimeOffset evenementDu)
    {
        if (ReplanifieDu is { } connu && evenementDu <= connu)
        {
            return false;
        }

        Debut = nouveauDebut;
        ReplanifieDu = evenementDu;
        return true;
    }

    /// <summary>SAN-13 : absence constatée, définitive quel que soit l'ordre de réception.</summary>
    /// <returns><c>false</c> si l'absence était déjà notée.</returns>
    public bool MarquerAbsent()
    {
        if (Absent)
        {
            return false;
        }

        Absent = true;
        return true;
    }

    /// <returns><c>false</c> si un envoi plus récent est déjà connu.</returns>
    public bool EnregistrerConvocation(DateTimeOffset envoyeeLe)
    {
        if (ConvocationEnvoyeeLe is { } connu && envoyeeLe <= connu)
        {
            return false;
        }

        ConvocationEnvoyeeLe = envoyeeLe;
        return true;
    }

    /// <returns><c>false</c> si un abandon plus récent est déjà connu.</returns>
    public bool EnregistrerConvocationNonRemise(DateTimeOffset le)
    {
        if (ConvocationNonRemiseLe is { } connu && le <= connu)
        {
            return false;
        }

        ConvocationNonRemiseLe = le;
        return true;
    }

    public void Annuler(string? motif = null)
    {
        Annule = true;
        MotifAnnulation = motif;
    }
}

/// <summary>Reprise annoncée par l'employeur (bff-employeur.reprise-annoncee, §14.6). Clé : (travailleur, affilié, date de reprise).</summary>
public sealed class RepriseLocale
{
    private RepriseLocale()
    {
    }

    public RepriseLocale(Guid personneId, Guid affilieId, DateOnly dateReprise, DateOnly debutAbsence, DateTimeOffset evenementDu, Guid? repriseId = null)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        DateReprise = dateReprise;
        DebutAbsence = debutAbsence;
        EvenementDu = evenementDu;
        RepriseId = repriseId;
    }

    /// <summary>Processus de reprise (ProcessusReprise) qui a enregistré cette reprise ; <c>null</c> pour une annonce antérieure au processus.</summary>
    public Guid? RepriseId { get; private set; }

    /// <summary>Reprise annulée (ARC-33) : le moteur d'échéances l'ignore, l'obligation d'examen de reprise est annulée par le recalcul.</summary>
    public bool Annulee { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly DateReprise { get; private set; }

    public DateOnly DebutAbsence { get; private set; }

    public DateTimeOffset EvenementDu { get; private set; }

    public bool Appliquer(DateOnly debutAbsence, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        DebutAbsence = debutAbsence;
        EvenementDu = evenementDu;
        return true;
    }

    /// <summary>Rattache la reprise à son processus (annonce antérieure au processus, ou nouvelle annonce après annulation).</summary>
    public void Lier(Guid repriseId) => RepriseId = repriseId;

    /// <summary>Annulation par le processus : définitive pour ce processus, l'événement le plus récent l'emporte.</summary>
    public bool MarquerAnnulee(DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        Annulee = true;
        EvenementDu = evenementDu;
        return true;
    }

    /// <summary>Nouvelle annonce de la même reprise après son annulation : un nouveau processus la remplace.</summary>
    public void Reactiver(Guid repriseId, DateOnly debutAbsence, DateTimeOffset evenementDu)
    {
        RepriseId = repriseId;
        Annulee = false;
        DebutAbsence = debutAbsence;
        EvenementDu = evenementDu > EvenementDu ? evenementDu : EvenementDu;
    }
}

/// <summary>Incapacité notifiée (integrations.incapacite-notifiee, SAN-60) : début et source uniquement.</summary>
public sealed class IncapaciteLocale
{
    private IncapaciteLocale()
    {
    }

    public IncapaciteLocale(Guid incapaciteId, Guid personneId, Guid affilieId, DateOnly dateDebut, string source)
    {
        IncapaciteId = incapaciteId;
        PersonneId = personneId;
        AffilieId = affilieId;
        DateDebut = dateDebut;
        Source = source;
    }

    public Guid IncapaciteId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly DateDebut { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public void Appliquer(Guid personneId, Guid affilieId, DateOnly dateDebut, string source)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        DateDebut = dateDebut;
        Source = source;
    }
}

/// <summary>
/// Trajet de réintégration (reintegration.trajet-demarre / -termine) : les deux événements complètent la même ligne,
/// quel que soit leur ordre d'arrivée.
/// </summary>
public sealed class TrajetLocal
{
    private TrajetLocal()
    {
    }

    public TrajetLocal(Guid trajetId, Guid personneId, Guid affilieId)
    {
        TrajetId = trajetId;
        PersonneId = personneId;
        AffilieId = affilieId;
    }

    public Guid TrajetId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly? DateDemande { get; private set; }

    public DateOnly? DateFin { get; private set; }

    public string? Statut { get; private set; }

    public void Demarrer(DateOnly dateDemande) => DateDemande = dateDemande;

    public void Terminer(DateOnly dateFin, string statut)
    {
        DateFin = dateFin;
        Statut = statut;
    }
}

/// <summary>
/// Version d'une liste nominative générée (postes-risques.liste-nominative-generee, AFF-30) : sert à l'alerte
/// « liste non revue depuis 12 mois » (AFF-32). Clé : identifiant de la liste.
/// </summary>
public sealed class ListeNominativeLocale
{
    private ListeNominativeLocale()
    {
    }

    public ListeNominativeLocale(Guid listeNominativeId, Guid affilieId, string typeListe, int version, DateOnly dateReference, DateOnly dateGeneration)
    {
        ListeNominativeId = listeNominativeId;
        AffilieId = affilieId;
        TypeListe = typeListe;
        Version = version;
        DateReference = dateReference;
        DateGeneration = dateGeneration;
    }

    public Guid ListeNominativeId { get; private set; }

    public Guid AffilieId { get; private set; }

    public string TypeListe { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public DateOnly DateReference { get; private set; }

    public DateOnly DateGeneration { get; private set; }

    public void Appliquer(Guid affilieId, string typeListe, int version, DateOnly dateReference, DateOnly dateGeneration)
    {
        AffilieId = affilieId;
        TypeListe = typeListe;
        Version = version;
        DateReference = dateReference;
        DateGeneration = dateGeneration;
    }
}

/// <summary>Normalisation des codes de risques reçus (majuscules, sans doublon, triés).</summary>
public static class Codes
{
    public static string Normaliser(string code) => code.Trim().ToUpperInvariant();

    public static IReadOnlyList<string> Normaliser(IEnumerable<string> codes) =>
        codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(Normaliser).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}
