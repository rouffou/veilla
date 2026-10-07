using System.Text;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Obligations.Domain.Obligations;

/// <summary>
/// Types d'examens gérés par le moteur d'échéances (§5.1). Le code publié (<c>EVALUATION_PERIODIQUE</c>…) est le
/// vocabulaire commun de <c>ObligationCreee.TypeExamen</c> et de <c>ExamenCloture.TypeExamen</c> : un type d'examen,
/// jamais un contenu clinique (ARC-06).
/// </summary>
public enum TypeObligation
{
    /// <summary>Nouvelle affectation à un poste à risque : avant l'affectation effective.</summary>
    EvaluationPrealable,

    /// <summary>Risques du poste (annexe I.4-5) : fréquence par risque, surchargeable (AFF-13).</summary>
    EvaluationPeriodique,

    /// <summary>Risques du poste : entre ou avant les évaluations.</summary>
    ActesMedicauxSupplementaires,

    /// <summary>Absence d'au moins 4 semaines : du jour de la reprise à J+10 ouvrables (§2.1).</summary>
    ExamenReprise,

    /// <summary>Demande du travailleur en incapacité : 10 jours ouvrables.</summary>
    VisitePreReprise,

    /// <summary>Demande du travailleur : 10 jours ouvrables.</summary>
    ConsultationSpontanee,

    /// <summary>Déclaration de grossesse ou d'allaitement : dès la déclaration (AFF-24).</summary>
    ProtectionMaternite,

    /// <summary>Fin d'exposition à certains agents : selon l'agent (fréquence de la règle).</summary>
    SurveillanceProlongee,

    /// <summary>8 semaines d'incapacité (réglementation 2026).</summary>
    EstimationPotentielTravail,

    /// <summary>Demande de trajet de réintégration : invitation dans les 49 jours.</summary>
    EvaluationReintegration,
}

/// <summary>Origine d'une obligation (§15.3 obligation.origine).</summary>
public enum OrigineObligation
{
    /// <summary>Règle de surveillance d'un risque du poste (AFF-12).</summary>
    Regle,

    /// <summary>Fréquence surchargée par le CPMT (AFF-13).</summary>
    Surcharge,

    /// <summary>Événement : reprise, incapacité, demande du travailleur, protection de la maternité, trajet.</summary>
    Evenement,
}

public static class TypesObligation
{
    private static readonly Dictionary<TypeObligation, LocalizedLabel> Libelles = new()
    {
        [TypeObligation.EvaluationPrealable] = new("Évaluation de santé préalable", "Voorafgaande gezondheidsbeoordeling", "Vorherige Gesundheitsbeurteilung", "Pre-employment health assessment"),
        [TypeObligation.EvaluationPeriodique] = new("Évaluation de santé périodique", "Periodieke gezondheidsbeoordeling", "Regelmäßige Gesundheitsbeurteilung", "Periodic health assessment"),
        [TypeObligation.ActesMedicauxSupplementaires] = new("Actes médicaux supplémentaires", "Bijkomende medische handelingen", "Zusätzliche medizinische Leistungen", "Additional medical procedures"),
        [TypeObligation.ExamenReprise] = new("Examen de reprise du travail", "Onderzoek bij werkhervatting", "Untersuchung bei Wiederaufnahme der Arbeit", "Return-to-work examination"),
        [TypeObligation.VisitePreReprise] = new("Visite de pré-reprise", "Bezoek voorafgaand aan de werkhervatting", "Besuch vor der Wiederaufnahme der Arbeit", "Pre-return-to-work visit"),
        [TypeObligation.ConsultationSpontanee] = new("Consultation spontanée", "Spontane raadpleging", "Spontane Konsultation", "Spontaneous consultation"),
        [TypeObligation.ProtectionMaternite] = new("Protection de la maternité", "Moederschapsbescherming", "Mutterschutz", "Maternity protection"),
        [TypeObligation.SurveillanceProlongee] = new("Surveillance de santé prolongée", "Voortgezet gezondheidstoezicht", "Nachgehende Gesundheitsüberwachung", "Extended health surveillance"),
        [TypeObligation.EstimationPotentielTravail] = new("Estimation du potentiel de travail", "Inschatting van het arbeidspotentieel", "Einschätzung des Arbeitspotenzials", "Work potential assessment"),
        [TypeObligation.EvaluationReintegration] = new("Évaluation de réintégration", "Re-integratiebeoordeling", "Wiedereingliederungsbeurteilung", "Reintegration assessment"),
    };

    /// <summary>Code publié : <c>EvaluationPeriodique</c> → <c>EVALUATION_PERIODIQUE</c>.</summary>
    public static string Code(this TypeObligation type)
    {
        var nom = type.ToString();
        var code = new StringBuilder(nom.Length + 8);
        for (var i = 0; i < nom.Length; i++)
        {
            if (i > 0 && char.IsUpper(nom[i]))
            {
                code.Append('_');
            }

            code.Append(char.ToUpperInvariant(nom[i]));
        }

        return code.ToString();
    }

    /// <summary>Lit un code publié (<c>EVALUATION_PERIODIQUE</c>) ou un nom d'énumération, sans tenir compte de la casse.</summary>
    public static bool TryParse(string? code, out TypeObligation type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalise = code.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return !int.TryParse(normalise, out _) && Enum.TryParse(normalise, ignoreCase: true, out type) && Enum.IsDefined(type);
    }

    public static LocalizedLabel Libelle(this TypeObligation type) => Libelles[type];

    /// <summary>
    /// Types dont la seule existence révèle une information protégée (protection de la maternité, démarche du travailleur) :
    /// masqués aux profils externes (employeur, SIPP), qui n'en ont pas l'usage (minimisation, ARC-06).
    /// </summary>
    public static bool EstConfidentiel(this TypeObligation type) =>
        type is TypeObligation.ProtectionMaternite or TypeObligation.ConsultationSpontanee or TypeObligation.VisitePreReprise;

    /// <summary>Évaluations de santé qui fondent la périodicité : préalable, périodique et prolongée.</summary>
    public static bool EstEvaluationDeSante(this TypeObligation type) =>
        type is TypeObligation.EvaluationPrealable or TypeObligation.EvaluationPeriodique or TypeObligation.SurveillanceProlongee;

    /// <summary>
    /// Types à périodicité, qui peuvent être anticipés pour être regroupés avec un autre examen (SAN-03) ; les examens
    /// déclenchés par un événement (reprise, demande…) ne peuvent pas avoir lieu avant leur déclencheur.
    /// </summary>
    public static bool PeutEtreAnticipe(this TypeObligation type) =>
        type is TypeObligation.EvaluationPrealable or TypeObligation.EvaluationPeriodique or TypeObligation.ActesMedicauxSupplementaires
            or TypeObligation.SurveillanceProlongee;
}
