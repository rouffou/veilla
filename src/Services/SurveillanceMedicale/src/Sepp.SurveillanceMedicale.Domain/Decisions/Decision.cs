using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;

namespace Sepp.SurveillanceMedicale.Domain.Decisions;

/// <summary>SAN-31 : décisions du formulaire d'évaluation de santé (annexe I.4-2).</summary>
public enum CategorieDecision
{
    Apte,
    ApteAvecMesures,
    InaptitudeTemporaire,
    InaptitudeDefinitive,
    Mutation,
    EcartementMaternite,
}

public enum StatutDecision
{
    /// <summary>En rédaction par le CPMT : rien ne sort de la zone médicale.</summary>
    Brouillon,

    /// <summary>Signée (signature qualifiée, SAN-32) et transmise (DecisionEmise, SAN-33).</summary>
    Emise,
}

/// <summary>Codes publiés dans <c>DecisionEmise.Categorie</c> : seule la catégorie sort de la zone médicale (ARC-06).</summary>
public static class CodesDecision
{
    public static string Code(CategorieDecision categorie) => categorie switch
    {
        CategorieDecision.Apte => "APTE",
        CategorieDecision.ApteAvecMesures => "APTE_AVEC_MESURES",
        CategorieDecision.InaptitudeTemporaire => "INAPTITUDE_TEMPORAIRE",
        CategorieDecision.InaptitudeDefinitive => "INAPTITUDE_DEFINITIVE",
        CategorieDecision.Mutation => "MUTATION",
        CategorieDecision.EcartementMaternite => "ECARTEMENT_MATERNITE",
        _ => throw new ArgumentOutOfRangeException(nameof(categorie)),
    };
}

/// <summary>Contenu décidé : catégorie, mesures codées et validité (ce qui sort), justification et recommandations (chiffrées).</summary>
public sealed record ContenuDecision(
    CategorieDecision Categorie,
    IReadOnlyList<string> Mesures,
    DateOnly? ValideJusquAu,
    string? Justification,
    string? Recommandations);

/// <summary>
/// Décision d'évaluation de santé (§15.3 <c>decision</c>, SAN-30 à SAN-34). Seules la catégorie, les mesures codées
/// et la validité sortent de la zone médicale par <c>DecisionEmise</c> (§2.1, ARC-06) ; la justification médicale et les
/// recommandations au travailleur sont chiffrées et réservées aux exemplaires « travailleur » et « dossier ».
/// </summary>
public sealed class Decision : AggregateRoot
{
    private readonly List<string> _mesures = [];
    private readonly List<Recours> _recours = [];

    private Decision()
    {
    }

    private Decision(Guid id, Guid examenId, Guid dossierId, Guid personneId, Guid affilieId, string typeExamen, DateOnly dateExamen, string auteurId)
        : base(id)
    {
        ExamenId = Garde.Identifiant(examenId, "examen");
        DossierId = dossierId;
        PersonneId = personneId;
        AffilieId = affilieId;
        TypeExamen = typeExamen;
        DateExamen = dateExamen;
        AuteurId = Garde.Requis(auteurId, "auteur", 100);
        Statut = StatutDecision.Brouillon;
    }

    public Guid ExamenId { get; private set; }

    public Guid DossierId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public string TypeExamen { get; private set; } = string.Empty;

    public DateOnly DateExamen { get; private set; }

    public CategorieDecision Categorie { get; private set; }

    /// <summary>Mesures codées (restrictions, aménagements) : codes uniquement, jamais de texte libre (ARC-06).</summary>
    public IReadOnlyList<string> Mesures => _mesures.AsReadOnly();

    /// <summary>SAN-31 : fin de validité (inclus) ; <c>null</c> pour une décision sans échéance.</summary>
    public DateOnly? ValideJusquAu { get; private set; }

    /// <summary>Motif médical : chiffré (ARC-45), jamais transmis à l'employeur.</summary>
    public string? Justification { get; private set; }

    /// <summary>Recommandations au travailleur (exemplaire travailleur) : chiffrées (ARC-45).</summary>
    public string? Recommandations { get; private set; }

    public string AuteurId { get; private set; } = string.Empty;

    public StatutDecision Statut { get; private set; }

    public DateTimeOffset? SigneeLe { get; private set; }

    /// <summary>Référence de la signature qualifiée (SAN-32) renvoyée par le prestataire de signature.</summary>
    public string? ReferenceSignature { get; private set; }

    /// <summary>Formulaire PDF/A produit par le service Documents (SAN-30), quand il est connu.</summary>
    public Guid? DocumentId { get; private set; }

    public IReadOnlyList<Recours> Recours => _recours.AsReadOnly();

    public static Decision Rediger(Guid examenId, Guid dossierId, Guid personneId, Guid affilieId, string typeExamen, DateOnly dateExamen, string auteurId, ContenuDecision contenu)
    {
        var decision = new Decision(NewId(), examenId, dossierId, personneId, affilieId, typeExamen, dateExamen, auteurId);
        decision.Appliquer(contenu);
        return decision;
    }

    public void Modifier(ContenuDecision contenu)
    {
        if (Statut != StatutDecision.Brouillon)
        {
            throw new DomainException("Une décision émise ne se modifie plus : seule l'issue d'un recours ou d'une concertation peut la réformer.");
        }

        Appliquer(contenu);
    }

    /// <summary>SAN-32 : signature qualifiée par le CPMT auteur, puis transmission (SAN-33).</summary>
    public void Signer(string signataireId, string referenceSignature, DateTimeOffset signeeLe)
    {
        if (Statut != StatutDecision.Brouillon)
        {
            throw new DomainException("Cette décision est déjà signée.");
        }

        if (!string.Equals(signataireId, AuteurId, StringComparison.Ordinal))
        {
            throw new DomainException("Seul le CPMT auteur de la décision peut la signer.");
        }

        if (ValideJusquAu is { } fin && fin < DateOnly.FromDateTime(signeeLe.UtcDateTime))
        {
            throw new DomainException("La validité de la décision est déjà dépassée.");
        }

        ReferenceSignature = Garde.Requis(referenceSignature, "référence de signature", 200);
        SigneeLe = signeeLe;
        Statut = StatutDecision.Emise;
        Raise(new DecisionTransmise(Id, PersonneId, AffilieId, Categorie, [.. _mesures], ValideJusquAu, signeeLe));
    }

    public void AssocierDocument(Guid documentId) => DocumentId = Garde.Identifiant(documentId, "document");

    /// <summary>Date de remise du formulaire au travailleur : point de départ des délais de concertation et de recours.</summary>
    public DateOnly? DateRemise => SigneeLe is { } s ? DateOnly.FromDateTime(s.UtcDateTime) : null;

    /// <summary>SAN-34 : introduction d'une concertation ou d'un recours, dans le délai légal suivant la remise du formulaire.</summary>
    public Recours IntroduireRecours(TypeRecours type, DateOnly dateIntroduction, PolitiqueRecours politique)
    {
        if (Statut != StatutDecision.Emise || DateRemise is not { } remise)
        {
            throw new DomainException("Une concertation ou un recours ne porte que sur une décision émise.");
        }

        if (_recours.Exists(r => r.Type == type && r.Issue is null))
        {
            throw new DomainException("Une procédure de ce type est déjà en cours pour cette décision.");
        }

        if (dateIntroduction < remise)
        {
            throw new DomainException("La procédure ne peut être introduite avant la remise du formulaire.");
        }

        var recours = new Recours(NewId(), type, dateIntroduction, politique.DateLimiteIntroduction(type, remise), politique.DateLimiteIssue(type, dateIntroduction));
        _recours.Add(recours);
        return recours;
    }

    /// <summary>
    /// SAN-34 : issue de la procédure (décision du médecin-inspecteur social pour un recours). Une décision réformée
    /// prend la catégorie, les mesures et la validité nouvelles et est retransmise (DecisionEmise, état courant).
    /// </summary>
    public void EnregistrerIssue(Guid recoursId, IssueRecours issue, DateOnly dateIssue, ContenuDecision? reformation, string? commentaire)
    {
        var recours = _recours.Find(r => r.Id == recoursId) ?? throw new DomainException("Procédure inconnue.");
        if (issue == IssueRecours.Reformee && reformation is null)
        {
            throw new DomainException("Une décision réformée précise la nouvelle catégorie.");
        }

        if (issue != IssueRecours.Reformee && reformation is not null)
        {
            throw new DomainException("Seule une issue « réformée » modifie la décision.");
        }

        recours.Clore(issue, dateIssue, commentaire);
        if (reformation is not null)
        {
            Appliquer(reformation with { Justification = reformation.Justification ?? Justification, Recommandations = reformation.Recommandations ?? Recommandations });
            Raise(new DecisionTransmise(Id, PersonneId, AffilieId, Categorie, [.. _mesures], ValideJusquAu, DateTimeOffset.UtcNow));
        }
    }

    private void Appliquer(ContenuDecision contenu)
    {
        var mesures = contenu.Mesures.Select(m => Garde.Code(m, "mesure")).Distinct(StringComparer.Ordinal).ToList();
        switch (contenu.Categorie)
        {
            case CategorieDecision.Apte when mesures.Count > 0:
                throw new DomainException("Une décision « apte » ne comporte pas de mesure : choisir « apte avec mesures ».");
            case CategorieDecision.ApteAvecMesures when mesures.Count == 0:
                throw new DomainException("Une décision « apte avec mesures » précise au moins une mesure.");
            case CategorieDecision.InaptitudeTemporaire or CategorieDecision.EcartementMaternite when contenu.ValideJusquAu is null:
                throw new DomainException("Une inaptitude temporaire ou un écartement précise sa durée de validité.");
            case CategorieDecision.InaptitudeDefinitive when contenu.ValideJusquAu is not null:
                throw new DomainException("Une inaptitude définitive n'a pas de fin de validité.");
        }

        if (contenu.ValideJusquAu is { } fin && fin < DateExamen)
        {
            throw new DomainException("La fin de validité précède la date de l'examen.");
        }

        Categorie = contenu.Categorie;
        _mesures.Clear();
        _mesures.AddRange(mesures);
        ValideJusquAu = contenu.ValideJusquAu;
        Justification = Garde.Facultatif(contenu.Justification, "justification", Garde.TexteCliniqueMaximum);
        Recommandations = Garde.Facultatif(contenu.Recommandations, "recommandations", Garde.TexteCliniqueMaximum);
    }
}

public enum TypeRecours
{
    /// <summary>Procédure de concertation avec le CPMT (Code du bien-être au travail, livre I, titre 4).</summary>
    Concertation,

    /// <summary>Recours auprès du médecin-inspecteur social.</summary>
    RecoursMedecinInspecteur,
}

public enum IssueRecours
{
    Confirmee,
    Reformee,
    Irrecevable,
    Desistement,
}

/// <summary>SAN-34 : procédure de concertation ou de recours (§15.3 <c>recours</c>).</summary>
public sealed class Recours : Entity
{
    private Recours()
    {
    }

    internal Recours(Guid id, TypeRecours type, DateOnly dateIntroduction, DateOnly dateLimiteIntroduction, DateOnly dateLimiteIssue) : base(id)
    {
        Type = type;
        DateIntroduction = dateIntroduction;
        DateLimiteIntroduction = dateLimiteIntroduction;
        DateLimiteIssue = dateLimiteIssue;
        IntroduitDansLeDelai = dateIntroduction <= dateLimiteIntroduction;
    }

    public TypeRecours Type { get; private set; }

    public DateOnly DateIntroduction { get; private set; }

    public DateOnly DateLimiteIntroduction { get; private set; }

    /// <summary>Introduction tardive : la procédure est enregistrée mais signalée (l'irrecevabilité est prononcée par l'instance).</summary>
    public bool IntroduitDansLeDelai { get; private set; }

    /// <summary>Échéance de la décision de l'instance (CPMT ou médecin-inspecteur social).</summary>
    public DateOnly DateLimiteIssue { get; private set; }

    public IssueRecours? Issue { get; private set; }

    public DateOnly? DateIssue { get; private set; }

    /// <summary>Chiffré (ARC-45).</summary>
    public string? Commentaire { get; private set; }

    internal void Clore(IssueRecours issue, DateOnly dateIssue, string? commentaire)
    {
        if (Issue is not null)
        {
            throw new DomainException("Cette procédure est déjà close.");
        }

        if (dateIssue < DateIntroduction)
        {
            throw new DomainException("L'issue ne peut précéder l'introduction de la procédure.");
        }

        Issue = issue;
        DateIssue = dateIssue;
        Commentaire = Garde.Facultatif(commentaire, "commentaire", Garde.TexteCliniqueMaximum);
    }
}

/// <summary>
/// Délais des procédures de concertation et de recours (SAN-34), en jours ouvrables (DAT-08). Valeurs par défaut :
/// introduction de la concertation 5 jours, issue 14 jours ; recours auprès du médecin-inspecteur social 7 jours,
/// décision 31 jours — à valider par le département médical, paramétrables par configuration.
/// </summary>
public sealed class PolitiqueRecours(BusinessCalendar calendrier, DelaisRecours delais)
{
    public DateOnly DateLimiteIntroduction(TypeRecours type, DateOnly remise) =>
        calendrier.AddBusinessDays(remise, type == TypeRecours.Concertation ? delais.IntroductionConcertation : delais.IntroductionRecours);

    public DateOnly DateLimiteIssue(TypeRecours type, DateOnly introduction) =>
        calendrier.AddBusinessDays(introduction, type == TypeRecours.Concertation ? delais.IssueConcertation : delais.DecisionMedecinInspecteur);
}

public sealed record DelaisRecours(int IntroductionConcertation = 5, int IssueConcertation = 14, int IntroductionRecours = 7, int DecisionMedecinInspecteur = 31);

/// <summary>Décision transmise (signée ou réformée) : traduite en <c>DecisionEmise</c> (catégorie, mesures, validité).</summary>
public sealed record DecisionTransmise(
    Guid DecisionId, Guid PersonneId, Guid AffilieId, CategorieDecision Categorie, IReadOnlyList<string> Mesures, DateOnly? ValideJusquAu, DateTimeOffset OccurredAt)
    : IDomainEvent;
