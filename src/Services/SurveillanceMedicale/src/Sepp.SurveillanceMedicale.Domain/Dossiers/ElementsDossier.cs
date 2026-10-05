using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.Dossiers;

/// <summary>
/// Donnée d'exposition (art. I.4-87, §15.3 <c>exposition</c>) : agent, niveau et période. Alimentée par les mesurages
/// du service Prévention (<c>MesurageEnregistre</c>) via les groupes d'exposition, ou saisie par le CPMT.
/// </summary>
public sealed class Exposition : Entity
{
    private Exposition()
    {
    }

    internal Exposition(Guid id, string agent, string niveau, DateOnly debut, DateOnly? fin, Guid? mesurageId, Guid? groupeExpositionId) : base(id)
    {
        if (fin is { } f && f < debut)
        {
            throw new DomainException("La fin de l'exposition précède son début.");
        }

        Agent = Garde.Code(agent, "agent");
        Niveau = Garde.Code(niveau, "niveau d'exposition");
        PeriodeDebut = debut;
        PeriodeFin = fin;
        MesurageId = mesurageId;
        GroupeExpositionId = groupeExpositionId;
    }

    public string Agent { get; private set; } = string.Empty;

    public string Niveau { get; private set; } = string.Empty;

    public DateOnly PeriodeDebut { get; private set; }

    /// <summary>Dernier jour d'exposition (inclus) ; <c>null</c> si l'exposition se poursuit.</summary>
    public DateOnly? PeriodeFin { get; private set; }

    public Guid? MesurageId { get; private set; }

    public Guid? GroupeExpositionId { get; private set; }

    /// <summary>Date de référence pour la conservation : fin d'exposition, ou début si elle est ponctuelle.</summary>
    public DateOnly DerniereDate => PeriodeFin ?? PeriodeDebut;
}

/// <summary>Appartenance de la personne à un groupe d'exposition homogène du service Prévention, sur une période (DAT-04).</summary>
public sealed class RattachementGroupeExposition : Entity
{
    private RattachementGroupeExposition()
    {
    }

    internal RattachementGroupeExposition(Guid id, Guid groupeExpositionId, Guid affilieId, Validity periode) : base(id)
    {
        GroupeExpositionId = Garde.Identifiant(groupeExpositionId, "groupe d'exposition");
        AffilieId = affilieId;
        Periode = periode;
    }

    public Guid GroupeExpositionId { get; private set; }

    public Guid AffilieId { get; private set; }

    /// <summary>Période [début, fin[ (DAT-04).</summary>
    public Validity Periode { get; private set; }
}

public enum CategoriePieceJointe
{
    RapportSpecialiste,
    Imagerie,
    ResultatExterne,
    Correspondance,
    DossierRecu,
    Autre,
}

/// <summary>
/// SAN-41 : pièce jointe du dossier. Le fichier est stocké par le service Documents (<see cref="DocumentId"/>) ;
/// le titre et la description, qui peuvent révéler une pathologie, sont chiffrés (ARC-45).
/// </summary>
public sealed class PieceJointe : Entity
{
    private PieceJointe()
    {
    }

    internal PieceJointe(Guid id, Guid documentId, CategoriePieceJointe categorie, string titre, string? description, DateOnly dateDocument, DateOnly ajouteeLe)
        : base(id)
    {
        DocumentId = Garde.Identifiant(documentId, "document");
        Categorie = categorie;
        Titre = Garde.Requis(titre, "titre", 300);
        Description = Garde.Facultatif(description, "description", 4_000);
        DateDocument = dateDocument;
        AjouteeLe = ajouteeLe;
    }

    public Guid DocumentId { get; private set; }

    public CategoriePieceJointe Categorie { get; private set; }

    /// <summary>Chiffré (ARC-45).</summary>
    public string Titre { get; private set; } = string.Empty;

    /// <summary>Chiffré (ARC-45).</summary>
    public string? Description { get; private set; }

    public DateOnly DateDocument { get; private set; }

    public DateOnly AjouteeLe { get; private set; }
}

public enum SourceQuestionnaire
{
    /// <summary>Rempli à l'avance sur le portail travailleur.</summary>
    Portail,

    /// <summary>Rempli sur tablette en salle d'attente.</summary>
    Tablette,

    /// <summary>Complété pendant la consultation.</summary>
    Consultation,
}

/// <summary>Réponse à une question d'un modèle de questionnaire (contenu clinique, chiffré).</summary>
public sealed record ReponseQuestionnaire(string CodeQuestion, string Valeur);

/// <summary>SAN-22 : questionnaire de santé rempli ; les réponses sont chiffrées (ARC-45).</summary>
public sealed class QuestionnaireRempli : Entity
{
    private QuestionnaireRempli()
    {
    }

    internal QuestionnaireRempli(
        Guid id, string modeleCode, int modeleVersion, IReadOnlyList<ReponseQuestionnaire> reponses, SourceQuestionnaire source, DateTimeOffset rempliLe, Guid? examenId)
        : base(id)
    {
        if (reponses.Count == 0)
        {
            throw new DomainException("Un questionnaire rempli contient au moins une réponse.");
        }

        ModeleCode = Garde.Code(modeleCode, "modèle de questionnaire");
        ModeleVersion = modeleVersion;
        Reponses = [.. reponses.Select(r => new ReponseQuestionnaire(Garde.Code(r.CodeQuestion, "question"), Garde.Requis(r.Valeur, "réponse", 4_000)))];
        Source = source;
        RempliLe = rempliLe;
        ExamenId = examenId;
    }

    public string ModeleCode { get; private set; } = string.Empty;

    public int ModeleVersion { get; private set; }

    /// <summary>Chiffrées (ARC-45).</summary>
    public IReadOnlyList<ReponseQuestionnaire> Reponses { get; private set; } = [];

    public SourceQuestionnaire Source { get; private set; }

    public DateTimeOffset RempliLe { get; private set; }

    public Guid? ExamenId { get; private set; }
}

/// <summary>SAN-50 : vaccination administrée (§15.3 <c>vaccination</c>).</summary>
public sealed class Vaccination : Entity
{
    private Vaccination()
    {
    }

    internal Vaccination(Guid id, string vaccinCode, int dose, DateOnly date, Guid? lotId, Guid? centreId, string administrePar, string? remarque) : base(id)
    {
        if (dose is < 1 or > 20)
        {
            throw new DomainException("Le numéro de dose est compris entre 1 et 20.");
        }

        VaccinCode = vaccinCode;
        Dose = dose;
        Date = date;
        LotId = lotId;
        CentreId = centreId;
        AdministrePar = Garde.Requis(administrePar, "administré par", 100);
        Remarque = Garde.Facultatif(remarque, "remarque", 2_000);
    }

    public string VaccinCode { get; private set; } = string.Empty;

    /// <summary>Numéro de la dose dans le schéma ; une dose au-delà du schéma de base est un rappel.</summary>
    public int Dose { get; private set; }

    public DateOnly Date { get; private set; }

    public Guid? LotId { get; private set; }

    public Guid? CentreId { get; private set; }

    public string AdministrePar { get; private set; } = string.Empty;

    /// <summary>Réaction, contre-indication… : chiffrée (ARC-45).</summary>
    public string? Remarque { get; private set; }

    /// <summary>SAN-52 : enregistrement dans Vaccinnet ou e-Vax, effectué par le service Intégrations.</summary>
    public bool EnregistreRegistre { get; private set; }
}

public enum ResultatTuberculinique
{
    Negatif,
    Positif,
    Douteux,
}

/// <summary>Lecture d'un test tuberculinique : contenu clinique, chiffré (ARC-45).</summary>
public sealed record LectureTuberculinique(ResultatTuberculinique Resultat, int? IndurationMm)
{
    public static LectureTuberculinique Creer(ResultatTuberculinique resultat, int? indurationMm) =>
        indurationMm is < 0 or > 100
            ? throw new DomainException("L'induration est exprimée en millimètres (0 à 100).")
            : new(resultat, indurationMm);
}

/// <summary>SAN-50 : test tuberculinique (intradermoréaction).</summary>
public sealed class TestTuberculinique : Entity
{
    private TestTuberculinique()
    {
    }

    internal TestTuberculinique(Guid id, DateOnly datePose, string realisePar) : base(id)
    {
        DatePose = datePose;
        RealisePar = Garde.Requis(realisePar, "réalisé par", 100);
    }

    public DateOnly DatePose { get; private set; }

    public DateOnly? DateLecture { get; private set; }

    /// <summary>Chiffrée (ARC-45).</summary>
    public LectureTuberculinique? Lecture { get; private set; }

    public string RealisePar { get; private set; } = string.Empty;

    internal void Lire(DateOnly dateLecture, LectureTuberculinique lecture)
    {
        if (Lecture is not null)
        {
            throw new DomainException("Ce test a déjà été lu.");
        }

        if (dateLecture < DatePose || dateLecture > DatePose.AddDays(7))
        {
            throw new DomainException("La lecture d'un test tuberculinique a lieu dans les jours qui suivent la pose (7 jours au plus).");
        }

        DateLecture = dateLecture;
        Lecture = lecture;
    }
}
