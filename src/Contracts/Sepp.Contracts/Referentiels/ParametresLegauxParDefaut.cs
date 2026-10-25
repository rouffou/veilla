namespace Sepp.Contracts.Referentiels;

/// <summary>
/// Unités d'un paramètre légal, telles que publiées par <see cref="ParametreLegalModifie.Unite"/> (mêmes noms que
/// l'énumération du service Référentiels).
/// </summary>
public static class UnitesParametreLegal
{
    public const string JoursOuvrables = "JoursOuvrables";
    public const string JoursCalendrier = "JoursCalendrier";
    public const string Semaines = "Semaines";
    public const string Mois = "Mois";
    public const string Annees = "Annees";
    public const string Nombre = "Nombre";
}

/// <summary>Codes stables des paramètres légaux versionnés du service Référentiels (ARC-21).</summary>
public static class CodesParametresLegaux
{
    public const string RepriseAbsenceMinimum = "SANTE.REPRISE.ABSENCE_MINIMUM";
    public const string RepriseDelai = "SANTE.REPRISE.DELAI";
    public const string ConsultationSpontaneeDelai = "SANTE.CONSULTATION_SPONTANEE.DELAI";
    public const string PreRepriseDelai = "SANTE.PRE_REPRISE.DELAI";
    public const string DossierConservationMinimum = "SANTE.DOSSIER.CONSERVATION_MINIMUM";
    public const string ListesNominativesConservation = "SANTE.LISTES_NOMINATIVES.CONSERVATION";
    public const string ListesNominativesRevueAlerte = "SANTE.LISTES_NOMINATIVES.REVUE_ALERTE";
    public const string ConcertationIntroductionDelai = "SANTE.CONCERTATION.INTRODUCTION_DELAI";
    public const string ConcertationIssueDelai = "SANTE.CONCERTATION.ISSUE_DELAI";
    public const string RecoursIntroductionDelai = "SANTE.RECOURS.INTRODUCTION_DELAI";
    public const string RecoursDecisionDelai = "SANTE.RECOURS.DECISION_DELAI";
    public const string ConvocationRappel1 = "CONVOCATION.RAPPEL_1";
    public const string ConvocationRappel2 = "CONVOCATION.RAPPEL_2";
    public const string ReintegrationInformationTravailleur = "REINTEGRATION.INFORMATION_TRAVAILLEUR";
    public const string ReintegrationEstimationPotentiel = "REINTEGRATION.ESTIMATION_POTENTIEL";
    public const string ReintegrationTrajetObligatoire = "REINTEGRATION.TRAJET_OBLIGATOIRE";
    public const string ReintegrationSeuilTravailleurs = "REINTEGRATION.SEUIL_TRAVAILLEURS";
    public const string ReintegrationAbsencesMedecinConseil = "REINTEGRATION.ABSENCES_MEDECIN_CONSEIL";
    public const string ReintegrationInvitationDelai = "REINTEGRATION.INVITATION.DELAI";
    public const string ReintegrationReponsePlanDelai = "REINTEGRATION.REPONSE_PLAN.DELAI";
    public const string PsyEntretienPrealableDelai = "PSY.ENTRETIEN_PREALABLE.DELAI";
    public const string PsyAnonymatSeuilRepondants = "PSY.ANONYMAT.SEUIL_REPONDANTS";
    public const string AuditConservation = "AUDIT.CONSERVATION";
}

/// <summary>Valeur initiale d'un paramètre légal : valeur, unité et base légale.</summary>
public sealed record ValeurParDefaut(string Code, int Valeur, string Unite, string BaseLegale);

/// <summary>
/// Source unique des valeurs initiales des paramètres légaux (ARC-21). Le service Référentiels s'en sert pour amorcer son
/// catalogue ; les services consommateurs s'en servent comme valeur de repli tant qu'aucune valeur n'a été reçue par
/// <see cref="ParametreLegalModifie"/> (Référentiels ne publie qu'à la modification d'un paramètre). Aucun service ne
/// redéclare ces valeurs : toute durée légale vient d'ici ou d'un paramètre reçu. Les durées de conservation et les délais
/// de concertation et de recours sont <b>à valider</b> par le département médical.
/// </summary>
public static class ParametresLegauxParDefaut
{
    private const string CodeBienEtre = "Code du bien-être au travail, livre I, titre 4 (§2.1)";
    private const string Reintegration2026 = "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)";
    private const string ValiderMedical = "Code du bien-être au travail, livre I, titre 4 (à valider par le département médical)";
    private const string Recours = "Cahier des charges SAN-34 (à valider par le département médical)";

    public static IReadOnlyList<ValeurParDefaut> Toutes { get; } =
    [
        new(CodesParametresLegaux.RepriseAbsenceMinimum, 4, UnitesParametreLegal.Semaines, CodeBienEtre),
        new(CodesParametresLegaux.RepriseDelai, 10, UnitesParametreLegal.JoursOuvrables, CodeBienEtre),
        new(CodesParametresLegaux.ConsultationSpontaneeDelai, 10, UnitesParametreLegal.JoursOuvrables, CodeBienEtre),
        new(CodesParametresLegaux.PreRepriseDelai, 10, UnitesParametreLegal.JoursOuvrables, CodeBienEtre),
        new(CodesParametresLegaux.DossierConservationMinimum, 15, UnitesParametreLegal.Annees, ValiderMedical),
        new(CodesParametresLegaux.ListesNominativesConservation, 5, UnitesParametreLegal.Annees, "Cahier des charges AFF-31"),
        new(CodesParametresLegaux.ListesNominativesRevueAlerte, 12, UnitesParametreLegal.Mois, "Cahier des charges AFF-32"),
        new(CodesParametresLegaux.ConcertationIntroductionDelai, 5, UnitesParametreLegal.JoursOuvrables, Recours),
        new(CodesParametresLegaux.ConcertationIssueDelai, 14, UnitesParametreLegal.JoursOuvrables, Recours),
        new(CodesParametresLegaux.RecoursIntroductionDelai, 7, UnitesParametreLegal.JoursOuvrables, Recours),
        new(CodesParametresLegaux.RecoursDecisionDelai, 31, UnitesParametreLegal.JoursOuvrables, Recours),
        new(CodesParametresLegaux.ConvocationRappel1, 7, UnitesParametreLegal.JoursCalendrier, "Cahier des charges SAN-13"),
        new(CodesParametresLegaux.ConvocationRappel2, 1, UnitesParametreLegal.JoursCalendrier, "Cahier des charges SAN-13"),
        new(CodesParametresLegaux.ReintegrationInformationTravailleur, 4, UnitesParametreLegal.Semaines, Reintegration2026),
        new(CodesParametresLegaux.ReintegrationEstimationPotentiel, 8, UnitesParametreLegal.Semaines, Reintegration2026),
        new(CodesParametresLegaux.ReintegrationTrajetObligatoire, 6, UnitesParametreLegal.Mois, Reintegration2026),
        new(CodesParametresLegaux.ReintegrationSeuilTravailleurs, 20, UnitesParametreLegal.Nombre, Reintegration2026),
        new(CodesParametresLegaux.ReintegrationAbsencesMedecinConseil, 2, UnitesParametreLegal.Nombre, Reintegration2026),
        new(CodesParametresLegaux.ReintegrationInvitationDelai, 49, UnitesParametreLegal.JoursCalendrier, Reintegration2026),
        new(CodesParametresLegaux.ReintegrationReponsePlanDelai, 14, UnitesParametreLegal.JoursCalendrier, "Cahier des charges SAN-62"),
        new(CodesParametresLegaux.PsyEntretienPrealableDelai, 10, UnitesParametreLegal.JoursCalendrier, "Code du bien-être au travail, livre I, titre 3"),
        new(CodesParametresLegaux.PsyAnonymatSeuilRepondants, 5, UnitesParametreLegal.Nombre, "Cahier des charges PSY-11"),
        new(CodesParametresLegaux.AuditConservation, 10, UnitesParametreLegal.Annees, "Cahier des charges NF-04"),
    ];

    private static readonly Dictionary<string, ValeurParDefaut> ParCode = Toutes.ToDictionary(v => v.Code, StringComparer.Ordinal);

    public static bool Existe(string code) => ParCode.ContainsKey(code);

    /// <summary>Valeur initiale du paramètre ; lève une exception pour un code inconnu (erreur de programmation).</summary>
    public static ValeurParDefaut Obtenir(string code) =>
        ParCode.TryGetValue(code, out var valeur) ? valeur : throw new InvalidOperationException($"Paramètre légal sans valeur par défaut : {code}.");
}
