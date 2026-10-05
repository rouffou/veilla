using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.Dossiers;

/// <summary>Cycle de conservation du dossier (SAN-44, NF-20, NF-22).</summary>
public enum StatutArchivage
{
    /// <summary>Dossier vivant : le travailleur est suivi.</summary>
    Actif,

    /// <summary>Plus d'activité : conservé jusqu'à la date de purge prévue.</summary>
    Archive,

    /// <summary>Date de purge atteinte : destruction proposée, en attente de la validation humaine du responsable du traitement.</summary>
    PurgeProposee,
}

/// <summary>
/// Dossier de santé (§15.3, SAN-40) : un seul dossier par personne, transversal aux employeurs successifs affiliés
/// au SEPP. Il ne connaît que <c>personne_id</c> (jamais le NISS, DAT-06). Il regroupe les parties prévues aux
/// art. I.4-85 à I.4-87 qui ne sont pas des agrégats propres : données d'exposition, pièces jointes, questionnaires
/// de santé, vaccinations et tests tuberculiniques. Les examens, décisions, déclarations de maladie professionnelle et
/// transferts sont des agrégats qui le référencent.
/// </summary>
/// <remarks>
/// Secret médical (§2.1) : tout le contenu clinique est chiffré au niveau applicatif avec les clés de la zone médicale
/// (ARC-45), et chaque accès est autorisé par la matrice §3.3, la relation de soin et journalisé (NF-04).
/// </remarks>
public sealed class DossierSante : AggregateRoot
{
    private readonly List<Exposition> _expositions = [];
    private readonly List<RattachementGroupeExposition> _groupesExposition = [];
    private readonly List<PieceJointe> _piecesJointes = [];
    private readonly List<QuestionnaireRempli> _questionnaires = [];
    private readonly List<Vaccination> _vaccinations = [];
    private readonly List<TestTuberculinique> _testsTuberculiniques = [];

    private DossierSante()
    {
    }

    private DossierSante(Guid id, Guid personneId, string? gestionnaireCpmtId, DateOnly dateOuverture) : base(id)
    {
        PersonneId = Garde.Identifiant(personneId, "personne");
        GestionnaireCpmtId = Garde.Facultatif(gestionnaireCpmtId, "CPMT gestionnaire", 100);
        DateOuverture = dateOuverture;
        StatutArchivage = StatutArchivage.Actif;
    }

    public Guid PersonneId { get; private set; }

    /// <summary>CPMT gestionnaire du dossier (identifiant OIDC <c>sub</c>) : premier critère de la relation de soin.</summary>
    public string? GestionnaireCpmtId { get; private set; }

    public DateOnly DateOuverture { get; private set; }

    public StatutArchivage StatutArchivage { get; private set; }

    public DateOnly? DateArchivage { get; private set; }

    /// <summary>Date à partir de laquelle la destruction peut être proposée (SAN-44).</summary>
    public DateOnly? DatePurgePrevue { get; private set; }

    public IReadOnlyList<Exposition> Expositions => _expositions.AsReadOnly();

    public IReadOnlyList<RattachementGroupeExposition> GroupesExposition => _groupesExposition.AsReadOnly();

    public IReadOnlyList<PieceJointe> PiecesJointes => _piecesJointes.AsReadOnly();

    public IReadOnlyList<QuestionnaireRempli> Questionnaires => _questionnaires.AsReadOnly();

    public IReadOnlyList<Vaccination> Vaccinations => _vaccinations.AsReadOnly();

    public IReadOnlyList<TestTuberculinique> TestsTuberculiniques => _testsTuberculiniques.AsReadOnly();

    public static DossierSante Ouvrir(Guid personneId, string? gestionnaireCpmtId, DateOnly dateOuverture) =>
        new(NewId(), personneId, gestionnaireCpmtId, dateOuverture);

    /// <summary>Relation de soin (§3.3) : le CPMT gestionnaire, ou un professionnel ayant vacciné la personne.</summary>
    /// <remarks>Les examens planifiés ou réalisés par le professionnel complètent la règle (voir l'application).</remarks>
    public bool EstSuiviPar(string utilisateurId) =>
        string.Equals(GestionnaireCpmtId, utilisateurId, StringComparison.Ordinal)
        || _vaccinations.Exists(v => string.Equals(v.AdministrePar, utilisateurId, StringComparison.Ordinal));

    public void ChangerGestionnaire(string cpmtId)
    {
        GestionnaireCpmtId = Garde.Requis(cpmtId, "CPMT gestionnaire", 100);
        Reactiver();
    }

    /// <summary>
    /// SAN-40 : la personne fait partie d'un groupe d'exposition homogène (service Prévention) pendant une période ;
    /// les mesurages de ce groupe alimentent ses données d'exposition.
    /// </summary>
    public RattachementGroupeExposition RattacherGroupeExposition(Guid groupeExpositionId, Guid affilieId, Validity periode)
    {
        if (_groupesExposition.Exists(g => g.GroupeExpositionId == groupeExpositionId && g.Periode.Overlaps(periode)))
        {
            throw new DomainException("La personne est déjà rattachée à ce groupe d'exposition sur cette période.");
        }

        var rattachement = new RattachementGroupeExposition(NewId(), groupeExpositionId, Garde.Identifiant(affilieId, "affilié"), periode);
        _groupesExposition.Add(rattachement);
        Reactiver();
        return rattachement;
    }

    /// <summary>Groupes d'exposition auxquels la personne appartenait à la date d'un mesurage.</summary>
    public bool EstExposeeViaGroupe(Guid groupeExpositionId, DateOnly date) =>
        _groupesExposition.Exists(g => g.GroupeExpositionId == groupeExpositionId && g.Periode.Contains(date));

    /// <summary>
    /// Donnée d'exposition (art. I.4-87) : saisie par le CPMT ou issue d'un mesurage du service Prévention.
    /// Idempotent pour un mesurage : un même mesurage n'est enregistré qu'une fois dans le dossier.
    /// </summary>
    /// <returns><c>null</c> si le mesurage était déjà enregistré.</returns>
    public Exposition? EnregistrerExposition(string agent, string niveau, DateOnly debut, DateOnly? fin, Guid? mesurageId, Guid? groupeExpositionId)
    {
        if (mesurageId is { } id && _expositions.Exists(e => e.MesurageId == id))
        {
            return null;
        }

        var exposition = new Exposition(NewId(), agent, niveau, debut, fin, mesurageId, groupeExpositionId);
        _expositions.Add(exposition);
        Reactiver();
        return exposition;
    }

    /// <summary>SAN-41 : pièce jointe dont le fichier est conservé par le service Documents (référence <c>document_id</c>).</summary>
    public PieceJointe AjouterPieceJointe(Guid documentId, CategoriePieceJointe categorie, string titre, string? description, DateOnly dateDocument, DateOnly ajouteeLe)
    {
        if (_piecesJointes.Exists(p => p.DocumentId == documentId))
        {
            throw new DomainException("Ce document est déjà joint au dossier.");
        }

        var piece = new PieceJointe(NewId(), documentId, categorie, titre, description, dateDocument, ajouteeLe);
        _piecesJointes.Add(piece);
        Reactiver();
        return piece;
    }

    /// <summary>SAN-22 : questionnaire de santé rempli (portail, tablette en salle d'attente ou consultation).</summary>
    public QuestionnaireRempli EnregistrerQuestionnaire(
        string modeleCode, int modeleVersion, IReadOnlyList<ReponseQuestionnaire> reponses, SourceQuestionnaire source, DateTimeOffset remplile, Guid? examenId)
    {
        var questionnaire = new QuestionnaireRempli(NewId(), modeleCode, modeleVersion, reponses, source, remplile, examenId);
        _questionnaires.Add(questionnaire);
        Reactiver();
        return questionnaire;
    }

    /// <summary>Dernières réponses à un modèle de questionnaire : base du pré-remplissage (SAN-22).</summary>
    public QuestionnaireRempli? DernierQuestionnaire(string modeleCode) =>
        _questionnaires.Where(q => string.Equals(q.ModeleCode, modeleCode, StringComparison.Ordinal)).MaxBy(q => q.RempliLe);

    /// <summary>SAN-50 : vaccination administrée, publiée vers Intégrations (Vaccinnet / e-Vax, SAN-52).</summary>
    public Vaccination Vacciner(string vaccinCode, int dose, DateOnly date, Guid? lotId, Guid? centreId, string administrePar, string? remarque)
    {
        var code = Garde.Code(vaccinCode, "vaccin");
        if (_vaccinations.Exists(v => v.VaccinCode == code && v.Dose == dose && v.Date == date))
        {
            throw new DomainException($"La dose {dose} du vaccin {code} est déjà enregistrée à cette date.");
        }

        var vaccination = new Vaccination(NewId(), code, dose, date, lotId, centreId, administrePar, remarque);
        _vaccinations.Add(vaccination);
        Raise(new VaccinationEnregistree(vaccination.Id, PersonneId, code, dose, date, DateTimeOffset.UtcNow));
        Reactiver();
        return vaccination;
    }

    /// <summary>SAN-50 : test tuberculinique (pose, puis lecture du résultat 48 à 72 heures plus tard).</summary>
    public TestTuberculinique PoserTestTuberculinique(DateOnly datePose, string realisePar)
    {
        var test = new TestTuberculinique(NewId(), datePose, realisePar);
        _testsTuberculiniques.Add(test);
        Reactiver();
        return test;
    }

    public TestTuberculinique LireTestTuberculinique(Guid testId, DateOnly dateLecture, LectureTuberculinique lecture)
    {
        var test = _testsTuberculiniques.Find(t => t.Id == testId) ?? throw new DomainException("Test tuberculinique inconnu.");
        test.Lire(dateLecture, lecture);
        Reactiver();
        return test;
    }

    /// <summary>SAN-44 : plus d'activité ; le dossier est conservé jusqu'à la date de purge calculée par la politique de conservation.</summary>
    public void Archiver(DateOnly date, DateOnly datePurgePrevue)
    {
        if (StatutArchivage != StatutArchivage.Actif)
        {
            throw new DomainException("Seul un dossier actif peut être archivé.");
        }

        if (datePurgePrevue <= date)
        {
            throw new DomainException("La date de purge prévue doit être postérieure à l'archivage.");
        }

        StatutArchivage = StatutArchivage.Archive;
        DateArchivage = date;
        DatePurgePrevue = datePurgePrevue;
    }

    /// <summary>Recalcul de la date de purge (nouvelle exposition, nouveau paramètre légal) : jamais plus tôt que prévu.</summary>
    public void ReporterPurge(DateOnly datePurgePrevue)
    {
        if (DatePurgePrevue is { } prevue && datePurgePrevue < prevue)
        {
            throw new DomainException("La date de purge ne peut pas être avancée.");
        }

        DatePurgePrevue = datePurgePrevue;
        if (StatutArchivage == StatutArchivage.PurgeProposee)
        {
            StatutArchivage = StatutArchivage.Archive;
        }
    }

    /// <summary>NF-22 : la purge est proposée quand la date de conservation est atteinte ; la destruction attend une validation humaine.</summary>
    public bool PeutEtreProposeeALaPurge(DateOnly date) =>
        StatutArchivage == StatutArchivage.Archive && DatePurgePrevue is { } prevue && prevue <= date;

    public void ProposerPurge(DateOnly date)
    {
        if (!PeutEtreProposeeALaPurge(date))
        {
            throw new DomainException("Le dossier n'a pas atteint sa date de purge.");
        }

        StatutArchivage = StatutArchivage.PurgeProposee;
    }

    /// <summary>NF-22 : condition de la destruction physique (après validation humaine, voir le cas d'usage).</summary>
    public void VerifierDestructionPossible()
    {
        if (StatutArchivage != StatutArchivage.PurgeProposee)
        {
            throw new DomainException("La destruction n'est possible que pour un dossier proposé à la purge.");
        }
    }

    /// <summary>Une nouvelle activité (retour du travailleur, nouvelle donnée) rend le dossier actif et annule la purge prévue.</summary>
    private void Reactiver()
    {
        StatutArchivage = StatutArchivage.Actif;
        DateArchivage = null;
        DatePurgePrevue = null;
    }
}

/// <summary>Vaccination enregistrée : identifiants, code du vaccin, dose et date uniquement (ARC-06).</summary>
public sealed record VaccinationEnregistree(Guid VaccinationId, Guid PersonneId, string VaccinCode, int Dose, DateOnly Date, DateTimeOffset OccurredAt) : IDomainEvent;
