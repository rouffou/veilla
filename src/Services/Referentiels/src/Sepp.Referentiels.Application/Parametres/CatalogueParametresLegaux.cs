using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Application.Parametres;

/// <summary>
/// Valeurs initiales des règles légales structurantes (§2.1). Elles restent modifiables par
/// l'administrateur fonctionnel ; les durées de conservation sont à valider par le département médical.
/// </summary>
public static class CatalogueParametresLegaux
{
    private static readonly DateOnly Reforme2026 = new(2026, 1, 1);

    public sealed record Definition(string Code, LocalizedLabel Libelle, UniteParametre Unite, decimal Valeur, string BaseLegale);

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new("SANTE.REPRISE.ABSENCE_MINIMUM",
            new("Absence minimale déclenchant l'examen de reprise", "Minimale afwezigheid voor het werkhervattingsonderzoek", "Mindestabwesenheit für die Wiederaufnahmeuntersuchung", "Minimum absence triggering the return-to-work examination"),
            UniteParametre.Semaines, 4, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
        new("SANTE.REPRISE.DELAI",
            new("Délai de l'examen de reprise après la reprise", "Termijn van het werkhervattingsonderzoek na de hervatting", "Frist für die Wiederaufnahmeuntersuchung nach der Wiederaufnahme", "Return-to-work examination deadline"),
            UniteParametre.JoursOuvrables, 10, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
        new("SANTE.CONSULTATION_SPONTANEE.DELAI",
            new("Délai de la consultation spontanée", "Termijn van de spontane raadpleging", "Frist für die spontane Konsultation", "Spontaneous consultation deadline"),
            UniteParametre.JoursOuvrables, 10, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
        new("SANTE.PRE_REPRISE.DELAI",
            new("Délai de la visite de pré-reprise", "Termijn van het bezoek voorafgaand aan de werkhervatting", "Frist für den Besuch vor der Wiederaufnahme", "Pre-return visit deadline"),
            UniteParametre.JoursOuvrables, 10, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
        new("SANTE.DOSSIER.CONSERVATION_MINIMUM",
            new("Durée minimale de conservation du dossier de santé", "Minimale bewaartermijn van het gezondheidsdossier", "Mindestaufbewahrungsdauer der Gesundheitsakte", "Minimum health record retention period"),
            UniteParametre.Annees, 15, "Code du bien-être au travail, livre I, titre 4 (à valider par le département médical)"),
        new("SANTE.LISTES_NOMINATIVES.CONSERVATION",
            new("Conservation de l'historique des listes nominatives", "Bewaring van de historiek van de naamlijsten", "Aufbewahrung des Verlaufs der Namenslisten", "Retention of nominative list history"),
            UniteParametre.Annees, 5, "Cahier des charges AFF-31"),
        new("SANTE.LISTES_NOMINATIVES.REVUE_ALERTE",
            new("Alerte liste nominative non revue", "Waarschuwing niet-herziene naamlijst", "Warnung nicht überprüfte Namensliste", "Alert for unreviewed nominative list"),
            UniteParametre.Mois, 12, "Cahier des charges AFF-32"),
        new("CONVOCATION.RAPPEL_1",
            new("Premier rappel avant le rendez-vous", "Eerste herinnering vóór de afspraak", "Erste Erinnerung vor dem Termin", "First appointment reminder"),
            UniteParametre.JoursCalendrier, 7, "Cahier des charges SAN-13"),
        new("CONVOCATION.RAPPEL_2",
            new("Second rappel avant le rendez-vous", "Tweede herinnering vóór de afspraak", "Zweite Erinnerung vor dem Termin", "Second appointment reminder"),
            UniteParametre.JoursCalendrier, 1, "Cahier des charges SAN-13"),
        new("REINTEGRATION.INFORMATION_TRAVAILLEUR",
            new("Information du travailleur après incapacité", "Informatie aan de werknemer na arbeidsongeschiktheid", "Information des Arbeitnehmers nach Arbeitsunfähigkeit", "Worker information after incapacity"),
            UniteParametre.Semaines, 4, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
        new("REINTEGRATION.ESTIMATION_POTENTIEL",
            new("Estimation du potentiel de travail après incapacité", "Inschatting van het arbeidspotentieel na arbeidsongeschiktheid", "Einschätzung des Arbeitspotenzials nach Arbeitsunfähigkeit", "Work potential assessment after incapacity"),
            UniteParametre.Semaines, 8, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
        new("REINTEGRATION.TRAJET_OBLIGATOIRE",
            new("Démarrage obligatoire du trajet après le début de l'incapacité", "Verplichte start van het traject na aanvang van de ongeschiktheid", "Verpflichtender Beginn des Wiedereingliederungsplans nach Beginn der Unfähigkeit", "Mandatory start of the reintegration pathway"),
            UniteParametre.Mois, 6, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
        new("REINTEGRATION.SEUIL_TRAVAILLEURS",
            new("Seuil de travailleurs pour le trajet obligatoire", "Drempel aantal werknemers voor het verplichte traject", "Schwellenwert der Arbeitnehmer für den Pflichtplan", "Worker threshold for the mandatory pathway"),
            UniteParametre.Nombre, 20, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
        new("REINTEGRATION.ABSENCES_MEDECIN_CONSEIL",
            new("Absences aux rendez-vous avant information du médecin-conseil", "Afwezigheden op afspraken vóór informatie aan de adviserend arts", "Abwesenheiten bei Terminen vor Information des Vertrauensarztes", "Missed appointments before informing the medical adviser"),
            UniteParametre.Nombre, 2, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
        new("REINTEGRATION.INVITATION.DELAI",
            new("Délai d'invitation à l'évaluation de réintégration", "Termijn voor de uitnodiging tot de re-integratiebeoordeling", "Frist für die Einladung zur Wiedereingliederungsbeurteilung", "Invitation deadline for the reintegration assessment"),
            UniteParametre.JoursCalendrier, 49, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
        new("REINTEGRATION.REPONSE_PLAN.DELAI",
            new("Délai de réponse du travailleur au plan de réintégration", "Antwoordtermijn van de werknemer op het re-integratieplan", "Antwortfrist des Arbeitnehmers auf den Wiedereingliederungsplan", "Worker response deadline for the reintegration plan"),
            UniteParametre.JoursCalendrier, 14, "Cahier des charges SAN-62"),
        new("PSY.ENTRETIEN_PREALABLE.DELAI",
            new("Délai de l'entretien personnel préalable", "Termijn van het voorafgaand persoonlijk gesprek", "Frist für das vorherige persönliche Gespräch", "Deadline for the preliminary personal interview"),
            UniteParametre.JoursCalendrier, 10, "Code du bien-être au travail, livre I, titre 3"),
        new("PSY.ANONYMAT.SEUIL_REPONDANTS",
            new("Seuil minimal de répondants avant restitution", "Minimale drempel aan respondenten vóór terugkoppeling", "Mindestanzahl an Befragten vor der Auswertung", "Minimum respondents before reporting"),
            UniteParametre.Nombre, 5, "Cahier des charges PSY-11"),
        new("AUDIT.CONSERVATION",
            new("Durée de conservation du journal d'audit", "Bewaartermijn van het auditlogboek", "Aufbewahrungsdauer des Audit-Protokolls", "Audit log retention period"),
            UniteParametre.Annees, 10, "Cahier des charges NF-04"),
    ];

    public static ParametreLegal Creer(Definition d) =>
        ParametreLegal.Creer(new CodeParametre(d.Code), d.Libelle, d.Unite, d.BaseLegale, d.Valeur, Reforme2026);
}

/// <summary>Crée les paramètres légaux manquants (idempotent) ; exécuté au démarrage du service.</summary>
public sealed class InitialiserParametresLegaux(IParametreLegalRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var existants = (await repository.ListAsync(cancellationToken)).Select(p => p.Code.Value).ToHashSet(StringComparer.Ordinal);
        var manquants = CatalogueParametresLegaux.Definitions.Where(d => !existants.Contains(d.Code)).ToList();
        foreach (var definition in manquants)
        {
            repository.Add(CatalogueParametresLegaux.Creer(definition));
        }

        if (manquants.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return manquants.Count;
    }
}
