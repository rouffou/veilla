using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Referentiels;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Application.Parametres;

/// <summary>
/// Valeurs initiales des règles légales structurantes (§2.1). Elles restent modifiables par
/// l'administrateur fonctionnel ; les durées de conservation sont à valider par le département médical.
/// </summary>
public static class CatalogueParametresLegaux
{
    private static readonly DateOnly Reforme2026 = new(2026, 1, 1);

    public sealed record Definition(string Code, LocalizedLabel Libelle)
    {
        private ValeurParDefaut Defaut => ParametresLegauxParDefaut.Obtenir(Code);

        public UniteParametre Unite => Enum.Parse<UniteParametre>(Defaut.Unite);

        public decimal Valeur => Defaut.Valeur;

        public string BaseLegale => Defaut.BaseLegale;
    }

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new("SANTE.REPRISE.ABSENCE_MINIMUM",
            new("Absence minimale déclenchant l'examen de reprise", "Minimale afwezigheid voor het werkhervattingsonderzoek", "Mindestabwesenheit für die Wiederaufnahmeuntersuchung", "Minimum absence triggering the return-to-work examination")),
        new("SANTE.REPRISE.DELAI",
            new("Délai de l'examen de reprise après la reprise", "Termijn van het werkhervattingsonderzoek na de hervatting", "Frist für die Wiederaufnahmeuntersuchung nach der Wiederaufnahme", "Return-to-work examination deadline")),
        new("SANTE.CONSULTATION_SPONTANEE.DELAI",
            new("Délai de la consultation spontanée", "Termijn van de spontane raadpleging", "Frist für die spontane Konsultation", "Spontaneous consultation deadline")),
        new("SANTE.PRE_REPRISE.DELAI",
            new("Délai de la visite de pré-reprise", "Termijn van het bezoek voorafgaand aan de werkhervatting", "Frist für den Besuch vor der Wiederaufnahme", "Pre-return visit deadline")),
        new("SANTE.DOSSIER.CONSERVATION_MINIMUM",
            new("Durée minimale de conservation du dossier de santé", "Minimale bewaartermijn van het gezondheidsdossier", "Mindestaufbewahrungsdauer der Gesundheitsakte", "Minimum health record retention period")),
        new("SANTE.CONCERTATION.INTRODUCTION_DELAI",
            new("Délai d'introduction de la concertation", "Termijn voor het inleiden van het overleg", "Frist für die Einleitung der Konzertierung", "Deadline to request a concertation")),
        new("SANTE.CONCERTATION.ISSUE_DELAI",
            new("Délai d'issue de la concertation", "Termijn voor de afronding van het overleg", "Frist für den Abschluss der Konzertierung", "Deadline to conclude a concertation")),
        new("SANTE.RECOURS.INTRODUCTION_DELAI",
            new("Délai d'introduction du recours auprès du médecin-inspecteur social", "Termijn voor het indienen van het beroep bij de sociaal inspecteur-arts", "Frist für die Einlegung des Einspruchs beim Sozialinspektionsarzt", "Deadline to lodge an appeal with the social medical inspector")),
        new("SANTE.RECOURS.DECISION_DELAI",
            new("Délai de décision du médecin-inspecteur social", "Beslissingstermijn van de sociaal inspecteur-arts", "Entscheidungsfrist des Sozialinspektionsarztes", "Decision deadline of the social medical inspector")),
        new("SANTE.LISTES_NOMINATIVES.CONSERVATION",
            new("Conservation de l'historique des listes nominatives", "Bewaring van de historiek van de naamlijsten", "Aufbewahrung des Verlaufs der Namenslisten", "Retention of nominative list history")),
        new("SANTE.LISTES_NOMINATIVES.REVUE_ALERTE",
            new("Alerte liste nominative non revue", "Waarschuwing niet-herziene naamlijst", "Warnung nicht überprüfte Namensliste", "Alert for unreviewed nominative list")),
        new("CONVOCATION.RAPPEL_1",
            new("Premier rappel avant le rendez-vous", "Eerste herinnering vóór de afspraak", "Erste Erinnerung vor dem Termin", "First appointment reminder")),
        new("CONVOCATION.RAPPEL_2",
            new("Second rappel avant le rendez-vous", "Tweede herinnering vóór de afspraak", "Zweite Erinnerung vor dem Termin", "Second appointment reminder")),
        new("REINTEGRATION.INFORMATION_TRAVAILLEUR",
            new("Information du travailleur après incapacité", "Informatie aan de werknemer na arbeidsongeschiktheid", "Information des Arbeitnehmers nach Arbeitsunfähigkeit", "Worker information after incapacity")),
        new("REINTEGRATION.ESTIMATION_POTENTIEL",
            new("Estimation du potentiel de travail après incapacité", "Inschatting van het arbeidspotentieel na arbeidsongeschiktheid", "Einschätzung des Arbeitspotenzials nach Arbeitsunfähigkeit", "Work potential assessment after incapacity")),
        new("REINTEGRATION.TRAJET_OBLIGATOIRE",
            new("Démarrage obligatoire du trajet après le début de l'incapacité", "Verplichte start van het traject na aanvang van de ongeschiktheid", "Verpflichtender Beginn des Wiedereingliederungsplans nach Beginn der Unfähigkeit", "Mandatory start of the reintegration pathway")),
        new("REINTEGRATION.SEUIL_TRAVAILLEURS",
            new("Seuil de travailleurs pour le trajet obligatoire", "Drempel aantal werknemers voor het verplichte traject", "Schwellenwert der Arbeitnehmer für den Pflichtplan", "Worker threshold for the mandatory pathway")),
        new("REINTEGRATION.ABSENCES_MEDECIN_CONSEIL",
            new("Absences aux rendez-vous avant information du médecin-conseil", "Afwezigheden op afspraken vóór informatie aan de adviserend arts", "Abwesenheiten bei Terminen vor Information des Vertrauensarztes", "Missed appointments before informing the medical adviser")),
        new("REINTEGRATION.INVITATION.DELAI",
            new("Délai d'invitation à l'évaluation de réintégration", "Termijn voor de uitnodiging tot de re-integratiebeoordeling", "Frist für die Einladung zur Wiedereingliederungsbeurteilung", "Invitation deadline for the reintegration assessment")),
        new("REINTEGRATION.REPONSE_PLAN.DELAI",
            new("Délai de réponse du travailleur au plan de réintégration", "Antwoordtermijn van de werknemer op het re-integratieplan", "Antwortfrist des Arbeitnehmers auf den Wiedereingliederungsplan", "Worker response deadline for the reintegration plan")),
        new("PSY.ENTRETIEN_PREALABLE.DELAI",
            new("Délai de l'entretien personnel préalable", "Termijn van het voorafgaand persoonlijk gesprek", "Frist für das vorherige persönliche Gespräch", "Deadline for the preliminary personal interview")),
        new("PSY.ANONYMAT.SEUIL_REPONDANTS",
            new("Seuil minimal de répondants avant restitution", "Minimale drempel aan respondenten vóór terugkoppeling", "Mindestanzahl an Befragten vor der Auswertung", "Minimum respondents before reporting")),
        new("AUDIT.CONSERVATION",
            new("Durée de conservation du journal d'audit", "Bewaartermijn van het auditlogboek", "Aufbewahrungsdauer des Audit-Protokolls", "Audit log retention period")),
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
