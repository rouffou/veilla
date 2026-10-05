using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application.Protocoles;

/// <summary>
/// Protocoles initiaux, chargés au démarrage quand la table correspondante est vide. Valeurs indicatives
/// À VALIDER PAR LE DÉPARTEMENT MÉDICAL (§2.1) ; elles restent modifiables par le CPMT dirigeant. Les codes de risque
/// des schémas vaccinaux reprennent le jeu d'exemple du service Postes et risques (préfixe EX.), à remplacer.
/// </summary>
public sealed class InitialiserProtocoles(IProtocolesRepository protocoles, IUnitOfWork unitOfWork)
{
    private static readonly DateOnly Debut = new(2026, 1, 1);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if ((await protocoles.ListerValeursReferenceAsync(cancellationToken)).Count == 0)
        {
            foreach (var valeur in ValeursReference())
            {
                protocoles.Add(valeur);
            }
        }

        if ((await protocoles.ListerSchemasVaccinauxAsync(cancellationToken)).Count == 0)
        {
            foreach (var schema in SchemasVaccinaux())
            {
                protocoles.Add(schema);
            }
        }

        if ((await protocoles.ListerDureesConservationAsync(cancellationToken)).Count == 0)
        {
            foreach (var duree in DureesConservation())
            {
                protocoles.Add(duree);
            }
        }

        if ((await protocoles.ListerModelesQuestionnaireAsync(cancellationToken)).Count == 0)
        {
            protocoles.Add(QuestionnaireGeneral());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public static IEnumerable<ValeurReference> ValeursReference() =>
    [
        ValeurReference.Definir(TypeActe.Biometrie, "BIOM.IMC", new("Indice de masse corporelle", "Body-mass-index", "Body-Mass-Index", "Body mass index"), "kg/m2", 18.5m, 30m, Debut),
        ValeurReference.Definir(TypeActe.Biometrie, "BIOM.TA_SYS", new("Tension artérielle systolique", "Systolische bloeddruk", "Systolischer Blutdruck", "Systolic blood pressure"), "mmHg", 90m, 140m, Debut),
        ValeurReference.Definir(TypeActe.Biometrie, "BIOM.TA_DIA", new("Tension artérielle diastolique", "Diastolische bloeddruk", "Diastolischer Blutdruck", "Diastolic blood pressure"), "mmHg", 60m, 90m, Debut),
        ValeurReference.Definir(TypeActe.Vision, "VISION.ACUITE_OD", new("Acuité visuelle œil droit", "Gezichtsscherpte rechteroog", "Sehschärfe rechtes Auge", "Visual acuity right eye"), "/10", 8m, null, Debut),
        ValeurReference.Definir(TypeActe.Vision, "VISION.ACUITE_OG", new("Acuité visuelle œil gauche", "Gezichtsscherpte linkeroog", "Sehschärfe linkes Auge", "Visual acuity left eye"), "/10", 8m, null, Debut),
        ValeurReference.Definir(TypeActe.Audiometrie, "AUDIO.PERTE_4000_OD", new("Perte auditive 4 kHz oreille droite", "Gehoorverlies 4 kHz rechteroor", "Hörverlust 4 kHz rechtes Ohr", "Hearing loss 4 kHz right ear"), "dB", null, 25m, Debut),
        ValeurReference.Definir(TypeActe.Audiometrie, "AUDIO.PERTE_4000_OG", new("Perte auditive 4 kHz oreille gauche", "Gehoorverlies 4 kHz linkeroor", "Hörverlust 4 kHz linkes Ohr", "Hearing loss 4 kHz left ear"), "dB", null, 25m, Debut),
        ValeurReference.Definir(TypeActe.Spirometrie, "SPIRO.VEMS_PCT", new("VEMS (% de la valeur prédite)", "FEV1 (% van voorspeld)", "FEV1 (% des Sollwerts)", "FEV1 (% predicted)"), "%", 80m, null, Debut),
        ValeurReference.Definir(TypeActe.Spirometrie, "SPIRO.TIFFENEAU", new("Rapport VEMS/CVF", "FEV1/FVC-verhouding", "FEV1/FVC-Verhältnis", "FEV1/FVC ratio"), "%", 70m, null, Debut),
        ValeurReference.Definir(TypeActe.Ecg, "ECG.FREQUENCE", new("Fréquence cardiaque", "Hartfrequentie", "Herzfrequenz", "Heart rate"), "bpm", 50m, 100m, Debut),
        ValeurReference.Definir(TypeActe.Biologie, "BIO.GLYCEMIE", new("Glycémie à jeun", "Nuchtere glycemie", "Nüchternblutzucker", "Fasting blood glucose"), "mg/dL", 70m, 110m, Debut),
        ValeurReference.Definir(TypeActe.Biologie, "BIO.PLOMBEMIE", new("Plombémie", "Loodgehalte in het bloed", "Blutbleispiegel", "Blood lead level"), "µg/dL", null, 30m, Debut),
        ValeurReference.Definir(TypeActe.Biologie, "BIO.ANTI_HBS", new("Anticorps anti-HBs", "Anti-HBs-antistoffen", "Anti-HBs-Antikörper", "Anti-HBs antibodies"), "UI/L", 10m, null, Debut),
    ];

    public static IEnumerable<SchemaVaccinal> SchemasVaccinaux() =>
    [
        SchemaVaccinal.Definir("HEPATITE_B", new("Hépatite B", "Hepatitis B", "Hepatitis B", "Hepatitis B"), ["EX.BIO.HEPATITE_B"], 3, [1, 5], null),
        SchemaVaccinal.Definir("TETANOS", new("Tétanos", "Tetanus", "Tetanus", "Tetanus"), ["EX.BIO.TETANOS"], 3, [1, 6], 120),
        SchemaVaccinal.Definir("GRIPPE", new("Grippe saisonnière", "Seizoensgriep", "Saisonale Grippe", "Seasonal influenza"), ["EX.BIO.GRIPPE"], 1, [], 12),
        SchemaVaccinal.Definir(CodesVaccination.TestTuberculinique, new("Test tuberculinique", "Tuberculinetest", "Tuberkulintest", "Tuberculin skin test"), ["EX.BIO.TUBERCULOSE"], 1, [], 12),
    ];

    public static IEnumerable<DureeConservationExposition> DureesConservation() =>
    [
        DureeConservationExposition.Definir("AMIANTE", 40, "Code du bien-être au travail, livre VI (amiante) — à valider par le département médical"),
        DureeConservationExposition.Definir("CANCERIGENE", 40, "Code du bien-être au travail, livre VI, titre 2 — à valider par le département médical"),
        DureeConservationExposition.Definir("BIO.GROUPE_3", 40, "Code du bien-être au travail, livre VII (agents biologiques) — à valider par le département médical"),
        DureeConservationExposition.Definir("BIO.GROUPE_4", 40, "Code du bien-être au travail, livre VII (agents biologiques) — à valider par le département médical"),
        DureeConservationExposition.Definir("RAYONNEMENTS_IONISANTS", 50, "Réglementation de la radioprotection (AFCN) — à valider par le département médical"),
    ];

    public static ModeleQuestionnaire QuestionnaireGeneral() => ModeleQuestionnaire.Creer(
        "SANTE_GENERAL",
        1,
        new LocalizedLabel("Questionnaire de santé général", "Algemene gezondheidsvragenlijst", "Allgemeiner Gesundheitsfragebogen", "General health questionnaire"),
        [
            new QuestionModele("TRAITEMENT_EN_COURS", new LocalizedLabel("Suivez-vous un traitement médical ?", "Volgt u een medische behandeling?", "Befinden Sie sich in ärztlicher Behandlung?", "Are you under medical treatment?"), TypeReponse.OuiNon, true, []),
            new QuestionModele("PLAINTES", new LocalizedLabel("Plaintes de santé liées au travail", "Gezondheidsklachten in verband met het werk", "Arbeitsbedingte Beschwerden", "Work-related health complaints"), TypeReponse.Texte, false, []),
            new QuestionModele("TABAC", new LocalizedLabel("Consommation de tabac", "Tabaksgebruik", "Tabakkonsum", "Tobacco use"), TypeReponse.Choix, true, ["JAMAIS", "ANCIEN", "ACTUEL"]),
            new QuestionModele("HEURES_SOMMEIL", new LocalizedLabel("Heures de sommeil par nuit", "Uren slaap per nacht", "Schlafstunden pro Nacht", "Hours of sleep per night"), TypeReponse.Nombre, false, []),
        ]);
}
