using Sepp.BuildingBlocks.Domain;

namespace Sepp.PostesRisques.Domain.Risques;

/// <summary>Catégories de risques de l'annexe I.4-5 du code du bien-être au travail (AFF-11).</summary>
public enum CategorieRisque
{
    Chimique,
    Physique,
    Biologique,
    Ergonomique,
    Psychosocial,
    PosteSecurite,
    PosteVigilance,
    TravailNuit,
    Conduite,
    DenreesAlimentaires,
    Autre,
}

/// <summary>Nature de la surveillance de la santé déclenchée par un risque (§5.1, AFF-12).</summary>
public enum TypeSurveillance
{
    /// <summary>Évaluation de santé préalable puis évaluations périodiques à la fréquence de la règle.</summary>
    EvaluationSantePeriodique,

    /// <summary>Actes médicaux supplémentaires entre ou avant les évaluations, à la fréquence de la règle.</summary>
    ActesMedicauxSupplementaires,

    /// <summary>Évaluation de santé préalable uniquement, sans périodicité.</summary>
    EvaluationPrealableUniquement,
}

/// <summary>
/// Risque du référentiel importé de l'annexe I.4-5 (AFF-11). Porte ses règles de surveillance versionnées
/// (AFF-12) : une nouvelle règle clôture la précédente, les règles passées restent consultables (DAT-04).
/// </summary>
public sealed class Risque : AggregateRoot
{
    private readonly List<RegleSurveillance> _regles = [];

    private Risque()
    {
    }

    private Risque(Guid id, string code, CategorieRisque categorie, LocalizedLabel libelle, string referenceLegale) : base(id)
    {
        Code = code;
        Categorie = categorie;
        Libelle = libelle;
        ReferenceLegale = referenceLegale;
    }

    /// <summary>Code stable du risque dans le référentiel.</summary>
    public string Code { get; private set; } = string.Empty;

    public CategorieRisque Categorie { get; private set; }

    /// <summary>Libellés FR, NL, DE (obligatoires) et EN (DAT-07).</summary>
    public LocalizedLabel Libelle { get; private set; } = null!;

    /// <summary>Référence du texte légal (article du code, arrêté).</summary>
    public string ReferenceLegale { get; private set; } = string.Empty;

    public IReadOnlyList<RegleSurveillance> Regles => _regles.AsReadOnly();

    public static Risque Creer(string code, CategorieRisque categorie, LocalizedLabel libelle, string referenceLegale)
    {
        var risque = new Risque(
            NewId(),
            NormaliserCode(code),
            categorie,
            libelle,
            Saisie.Obligatoire(referenceLegale, "La référence légale", 500));
        risque.Raise(new RisqueReferentielModifie(risque.Id, risque.Code, DateTimeOffset.UtcNow));
        return risque;
    }

    public static string NormaliserCode(string code) => Saisie.Code(code, "Le code du risque");

    /// <summary>Met à jour la description du risque ; renvoie <c>false</c> si rien ne change (import idempotent).</summary>
    public bool MettreAJour(CategorieRisque categorie, LocalizedLabel libelle, string referenceLegale)
    {
        var reference = Saisie.Obligatoire(referenceLegale, "La référence légale", 500);
        if (Categorie == categorie && Libelle == libelle && ReferenceLegale == reference)
        {
            return false;
        }

        Categorie = categorie;
        Libelle = libelle;
        ReferenceLegale = reference;
        Raise(new RisqueReferentielModifie(Id, Code, DateTimeOffset.UtcNow));
        return true;
    }

    /// <summary>Règle applicable à une date, ou <c>null</c> si le risque n'a pas encore de règle.</summary>
    public RegleSurveillance? RegleAu(DateOnly date) => _regles.FirstOrDefault(r => r.Validite.Contains(date));

    public RegleSurveillance? RegleEnVigueur => _regles.SingleOrDefault(r => r.Validite.IsOpen);

    /// <summary>
    /// AFF-12 : définit la règle de surveillance à partir d'une date. La règle en vigueur est clôturée à cette date
    /// et la version incrémentée. Renvoie <c>null</c> si la règle en vigueur est identique (import idempotent).
    /// </summary>
    public RegleSurveillance? DefinirRegle(
        TypeSurveillance type,
        int? frequenceMois,
        IEnumerable<string> actesSupplementaires,
        IEnumerable<string> vaccins,
        bool surveillanceProlongee,
        DateOnly valideDu)
    {
        var candidate = new RegleSurveillance(
            NewId(), 0, type, frequenceMois, actesSupplementaires, vaccins, surveillanceProlongee, new Validity(valideDu));

        var courante = RegleEnVigueur;
        if (courante is not null)
        {
            if (courante.MemeContenu(candidate))
            {
                return null;
            }

            if (valideDu <= courante.Validite.ValidFrom)
            {
                throw new DomainException(
                    $"La nouvelle règle du risque {Code} doit prendre effet après le {courante.Validite.ValidFrom:yyyy-MM-dd}.");
            }

            courante.Cloturer(valideDu);
        }

        candidate.Numeroter(_regles.Count == 0 ? 1 : _regles.Max(r => r.Version) + 1);
        _regles.Add(candidate);
        Raise(new RegleSurveillanceVersionnee(Id, Code, candidate.Version, valideDu, DateTimeOffset.UtcNow));
        return candidate;
    }
}

/// <summary>Règle de surveillance portée par un risque, versionnée (AFF-12, §15.3 regle_surveillance).</summary>
public sealed class RegleSurveillance : Entity
{
    public const int FrequenceMaximaleMois = 120;

    private RegleSurveillance()
    {
    }

    internal RegleSurveillance(
        Guid id,
        int version,
        TypeSurveillance type,
        int? frequenceMois,
        IEnumerable<string> actesSupplementaires,
        IEnumerable<string> vaccins,
        bool surveillanceProlongee,
        Validity validite) : base(id)
    {
        if (type is TypeSurveillance.EvaluationSantePeriodique or TypeSurveillance.ActesMedicauxSupplementaires && frequenceMois is null)
        {
            throw new DomainException($"Une fréquence en mois est obligatoire pour le type de surveillance {type}.");
        }

        if (type == TypeSurveillance.EvaluationPrealableUniquement && frequenceMois is not null)
        {
            throw new DomainException("Une évaluation préalable uniquement n'a pas de fréquence.");
        }

        if (frequenceMois is < 1 or > FrequenceMaximaleMois)
        {
            throw new DomainException($"La fréquence doit être comprise entre 1 et {FrequenceMaximaleMois} mois.");
        }

        Version = version;
        TypeSurveillance = type;
        FrequenceMois = frequenceMois;
        ActesSupplementaires = Codes(actesSupplementaires, "Un code d'acte médical");
        Vaccins = Codes(vaccins, "Un code de vaccin");
        SurveillanceProlongee = surveillanceProlongee;
        Validite = validite;
    }

    /// <summary>Numéro de version de la règle pour ce risque (1, 2, …).</summary>
    public int Version { get; private set; }

    public TypeSurveillance TypeSurveillance { get; private set; }

    /// <summary>Périodicité en mois ; <c>null</c> pour une évaluation préalable uniquement.</summary>
    public int? FrequenceMois { get; private set; }

    /// <summary>Codes des actes médicaux supplémentaires (biométrie, audiométrie, spirométrie…).</summary>
    public IReadOnlyList<string> ActesSupplementaires { get; private set; } = [];

    /// <summary>Codes des vaccinations requises.</summary>
    public IReadOnlyList<string> Vaccins { get; private set; } = [];

    /// <summary>Surveillance prolongée après la fin de l'exposition (§5.1).</summary>
    public bool SurveillanceProlongee { get; private set; }

    public Validity Validite { get; private set; }

    internal void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);

    internal void Numeroter(int version) => Version = version;

    internal bool MemeContenu(RegleSurveillance autre) =>
        TypeSurveillance == autre.TypeSurveillance
        && FrequenceMois == autre.FrequenceMois
        && SurveillanceProlongee == autre.SurveillanceProlongee
        && ActesSupplementaires.SequenceEqual(autre.ActesSupplementaires)
        && Vaccins.SequenceEqual(autre.Vaccins);

    private static List<string> Codes(IEnumerable<string> codes, string nom) =>
        codes.Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Saisie.Code(c, nom))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}

public sealed record RisqueReferentielModifie(Guid RisqueId, string Code, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RegleSurveillanceVersionnee(Guid RisqueId, string Code, int Version, DateOnly ValideDu, DateTimeOffset OccurredAt) : IDomainEvent;
