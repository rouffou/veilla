using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.Examens;

public enum StatutExamen
{
    EnCours,
    Cloture,
    Annule,
}

/// <summary>SAN-21 : rubriques de saisie structurée des actes.</summary>
public enum TypeActe
{
    Biometrie,
    Vision,
    Audiometrie,
    Spirometrie,
    Ecg,
    Biologie,
}

public enum SourceResultat
{
    Saisie,

    /// <summary>Import direct d'un appareil (HL7, fichier, pilote — SAN-21).</summary>
    ImportAppareil,
}

/// <summary>Mesure brute d'un acte : valeur numérique et/ou texte (contenu clinique, chiffré).</summary>
public sealed record Mesure(string Code, decimal? Valeur, string? Unite, string? Texte);

/// <summary>Mesure comparée aux valeurs de référence (SAN-23) : contenu clinique, chiffré.</summary>
public sealed record MesureEvaluee(string Code, decimal? Valeur, string? Unite, string? Texte, decimal? ReferenceMin, decimal? ReferenceMax, bool Inhabituelle);

/// <summary>
/// Examen de surveillance de la santé (§15.3 <c>examen</c>, §14.5) : consultation d'un travailleur par un CPMT ou un
/// infirmier, avec observation clinique, résultats d'actes et, à la clôture, publication de <c>ExamenCloture</c>
/// (type et date uniquement, ARC-06). La décision d'évaluation de santé est un agrégat distinct.
/// </summary>
public sealed class Examen : AggregateRoot
{
    private readonly List<ResultatActe> _resultats = [];
    private readonly List<PropositionFrequence> _propositionsFrequence = [];
    private readonly List<Guid> _obligationIds = [];

    private Examen()
    {
    }

    private Examen(Guid id, Guid dossierId, Guid personneId, Guid affilieId, string typeExamen, DateOnly date, string professionnelId,
        Guid? rendezVousId, IEnumerable<Guid> obligationIds)
        : base(id)
    {
        DossierId = Garde.Identifiant(dossierId, "dossier");
        PersonneId = Garde.Identifiant(personneId, "personne");
        AffilieId = Garde.Identifiant(affilieId, "affilié");
        TypeExamen = Garde.Code(typeExamen, "type d'examen");
        Date = date;
        ProfessionnelId = Garde.Requis(professionnelId, "professionnel", 100);
        RendezVousId = rendezVousId;
        _obligationIds.AddRange(obligationIds.Distinct());
        Statut = StatutExamen.EnCours;
    }

    public Guid DossierId { get; private set; }

    public Guid PersonneId { get; private set; }

    /// <summary>Employeur pour lequel l'examen est réalisé (destinataire de la décision).</summary>
    public Guid AffilieId { get; private set; }

    public string TypeExamen { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    /// <summary>CPMT ou infirmier qui réalise l'examen (relation de soin).</summary>
    public string ProfessionnelId { get; private set; } = string.Empty;

    public Guid? RendezVousId { get; private set; }

    /// <summary>Obligations couvertes (projection de <c>ObligationCreee</c> et <c>RendezVousPlanifie</c>).</summary>
    public IReadOnlyList<Guid> ObligationIds => _obligationIds.AsReadOnly();

    public StatutExamen Statut { get; private set; }

    public DateOnly? DateCloture { get; private set; }

    /// <summary>
    /// Examen de reprise réalisé hors du délai légal (date hors de [date due, date limite] de l'obligation reçue du service
    /// Obligations) ; <c>null</c> si sans objet ou si la date limite est inconnue.
    /// </summary>
    public bool? HorsDelaiLegal { get; private set; }

    /// <summary>Anamnèse et examen clinique, chiffrés (ARC-45).</summary>
    public ObservationClinique? Observation { get; private set; }

    public IReadOnlyList<ResultatActe> Resultats => _resultats.AsReadOnly();

    public IReadOnlyList<PropositionFrequence> PropositionsFrequence => _propositionsFrequence.AsReadOnly();

    public static Examen Ouvrir(Guid dossierId, Guid personneId, Guid affilieId, string typeExamen, DateOnly date, string professionnelId,
        Guid? rendezVousId, IEnumerable<Guid> obligationIds) =>
        new(NewId(), dossierId, personneId, affilieId, typeExamen, date, professionnelId, rendezVousId, obligationIds);

    /// <summary>SAN-21 : anamnèse et examen clinique (texte structuré par le modèle de texte du CPMT, SAN-24).</summary>
    public void SaisirObservation(string? anamnese, string? examenClinique, DateTimeOffset le)
    {
        VerifierEnCours();
        var a = Garde.Facultatif(anamnese, "anamnèse", Garde.TexteCliniqueMaximum);
        var e = Garde.Facultatif(examenClinique, "examen clinique", Garde.TexteCliniqueMaximum);
        if (a is null && e is null)
        {
            throw new DomainException("L'observation clinique est vide.");
        }

        if (Observation is null)
        {
            Observation = new ObservationClinique(NewId(), a, e, le);
        }
        else
        {
            Observation.Modifier(a, e, le);
        }
    }

    /// <summary>
    /// SAN-21, SAN-23 : résultat d'un acte, comparé aux valeurs de référence. Un résultat inhabituel déclenche une
    /// proposition d'augmenter la fréquence de surveillance (art. I.4-32), à accepter ou refuser par le CPMT.
    /// </summary>
    public ResultatActe EnregistrerActe(TypeActe typeActe, IReadOnlyList<MesureEvaluee> mesures, string? commentaire, SourceResultat source, DateOnly date)
    {
        VerifierEnCours();
        var resultat = new ResultatActe(NewId(), typeActe, mesures, commentaire, source, date);
        _resultats.Add(resultat);
        if (resultat.Inhabituel && !_propositionsFrequence.Exists(p => p.Statut == StatutProposition.Proposee))
        {
            _propositionsFrequence.Add(new PropositionFrequence(NewId(), resultat.Id, date));
        }

        return resultat;
    }

    public void DeciderProposition(Guid propositionId, bool acceptee, string decidePar, DateOnly date)
    {
        var proposition = _propositionsFrequence.Find(p => p.Id == propositionId) ?? throw new DomainException("Proposition inconnue.");
        proposition.Decider(acceptee, decidePar, date);
        Raise(new PropositionFrequenceDecidee(Id, propositionId, acceptee, DateTimeOffset.UtcNow));
    }

    /// <summary>Clôture de l'examen : publie <c>ExamenCloture</c> (type et date, jamais le contenu clinique).</summary>
    public void Cloturer(DateOnly date, bool? horsDelaiLegal)
    {
        VerifierEnCours();
        if (date < Date)
        {
            throw new DomainException("La clôture ne peut précéder la date de l'examen.");
        }

        Statut = StatutExamen.Cloture;
        DateCloture = date;
        HorsDelaiLegal = horsDelaiLegal;
        Raise(new ExamenClotureLocal(Id, PersonneId, AffilieId, TypeExamen, Date, DateTimeOffset.UtcNow));
    }

    public void Annuler()
    {
        VerifierEnCours();
        Statut = StatutExamen.Annule;
    }

    private void VerifierEnCours()
    {
        if (Statut != StatutExamen.EnCours)
        {
            throw new DomainException("L'examen est clôturé ou annulé : il ne peut plus être modifié.");
        }
    }
}

/// <summary>Anamnèse et examen clinique (§15.3 <c>observation_clinique</c>), chiffrés (ARC-45).</summary>
public sealed class ObservationClinique : Entity
{
    private ObservationClinique()
    {
    }

    internal ObservationClinique(Guid id, string? anamnese, string? examenClinique, DateTimeOffset le) : base(id)
    {
        Anamnese = anamnese;
        ExamenClinique = examenClinique;
        SaisieLe = le;
    }

    public string? Anamnese { get; private set; }

    public string? ExamenClinique { get; private set; }

    public DateTimeOffset SaisieLe { get; private set; }

    internal void Modifier(string? anamnese, string? examenClinique, DateTimeOffset le)
    {
        Anamnese = anamnese ?? Anamnese;
        ExamenClinique = examenClinique ?? ExamenClinique;
        SaisieLe = le;
    }
}

/// <summary>
/// Résultat d'acte (§15.3 <c>resultat_acte</c>) : audiométrie, spirométrie, biologie… Valeurs et commentaire chiffrés
/// (ARC-45) ; seul l'indicateur « inhabituel » reste en clair pour les alertes (SAN-23).
/// </summary>
public sealed class ResultatActe : Entity
{
    private ResultatActe()
    {
    }

    internal ResultatActe(Guid id, TypeActe typeActe, IReadOnlyList<MesureEvaluee> mesures, string? commentaire, SourceResultat source, DateOnly date) : base(id)
    {
        if (mesures.Count == 0)
        {
            throw new DomainException("Un résultat d'acte comporte au moins une mesure.");
        }

        TypeActe = typeActe;
        Mesures = [.. mesures];
        Commentaire = Garde.Facultatif(commentaire, "commentaire", Garde.TexteCliniqueMaximum);
        Source = source;
        Date = date;
        Inhabituel = mesures.Any(m => m.Inhabituelle);
    }

    public TypeActe TypeActe { get; private set; }

    /// <summary>Valeurs mesurées et valeurs de référence, chiffrées (ARC-45).</summary>
    public IReadOnlyList<MesureEvaluee> Mesures { get; private set; } = [];

    public string? Commentaire { get; private set; }

    public SourceResultat Source { get; private set; }

    public DateOnly Date { get; private set; }

    public bool Inhabituel { get; private set; }
}

public enum StatutProposition
{
    Proposee,
    Acceptee,
    Refusee,
}

/// <summary>
/// SAN-23 (art. I.4-32) : proposition d'augmenter la fréquence de surveillance après un résultat inhabituel.
/// Acceptée, elle se traduit par une surcharge de fréquence que le CPMT définit dans Postes et risques (AFF-13).
/// </summary>
public sealed class PropositionFrequence : Entity
{
    private PropositionFrequence()
    {
    }

    internal PropositionFrequence(Guid id, Guid resultatActeId, DateOnly date) : base(id)
    {
        ResultatActeId = resultatActeId;
        DateProposition = date;
        Statut = StatutProposition.Proposee;
    }

    public Guid ResultatActeId { get; private set; }

    public DateOnly DateProposition { get; private set; }

    public StatutProposition Statut { get; private set; }

    public string? DecidePar { get; private set; }

    public DateOnly? DateDecision { get; private set; }

    internal void Decider(bool acceptee, string decidePar, DateOnly date)
    {
        if (Statut != StatutProposition.Proposee)
        {
            throw new DomainException("Cette proposition a déjà été traitée.");
        }

        Statut = acceptee ? StatutProposition.Acceptee : StatutProposition.Refusee;
        DecidePar = Garde.Requis(decidePar, "décidé par", 100);
        DateDecision = date;
    }
}

/// <summary>Clôture d'un examen, traduite en <c>ExamenCloture</c> (ARC-06 : type et date uniquement).</summary>
public sealed record ExamenClotureLocal(Guid ExamenId, Guid PersonneId, Guid AffilieId, string TypeExamen, DateOnly Date, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record PropositionFrequenceDecidee(Guid ExamenId, Guid PropositionId, bool Acceptee, DateTimeOffset OccurredAt) : IDomainEvent;
