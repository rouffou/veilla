using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Application.EvaluationSante;

/// <summary>
/// Modèles de départ du formulaire d'évaluation de santé (annexe I.4-2 du code du bien-être au travail), en français,
/// néerlandais et allemand, pour les trois exemplaires (employeur, travailleur, dossier). Ils sont créés en
/// <b>brouillon</b> : ce sont des points de départ à relire, compléter et valider par le département médical (CPMT dirigeant)
/// avant publication. Ils ne reproduisent pas le formulaire officiel à l'identique.
/// </summary>
public static class ModelesParDefaut
{
    public const string CodeEmployeur = "SANTE.EVALUATION.EMPLOYEUR";
    public const string CodeTravailleur = "SANTE.EVALUATION.TRAVAILLEUR";
    public const string CodeDossier = "SANTE.EVALUATION.DOSSIER";

    public const string Avertissement =
        "Modèle de départ fourni avec le logiciel : à relire, compléter et valider par le département médical (CPMT dirigeant) " +
        "avant publication. Il ne reproduit pas le formulaire officiel de l'annexe I.4-2 du code du bien-être au travail.";

    public static IReadOnlyList<ChampDeclare> Champs { get; } =
    [
        new("reference_decision", TypeChamp.Texte, true, "Référence de la décision"),
        new("date_decision", TypeChamp.Date, true, "Date de la décision"),
        new("categorie", TypeChamp.Texte, true, "Décision (catégorie, libellé)"),
        new("mesures", TypeChamp.Liste, false, "Mesures (aménagements, restrictions)"),
        new("valide_jusqu_au", TypeChamp.Date, false, "Fin de validité de la décision"),
    ];

    public sealed record Definition(string Code, ZoneDocument Zone, Language Langue, string Libelle, string Contenu);

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new(CodeEmployeur, ZoneDocument.Standard, Language.Fr, "Formulaire d'évaluation de santé — exemplaire de l'employeur", Corps(Language.Fr, Exemplaire.Employeur)),
        new(CodeEmployeur, ZoneDocument.Standard, Language.Nl, "Formulier voor de gezondheidsbeoordeling — exemplaar voor de werkgever", Corps(Language.Nl, Exemplaire.Employeur)),
        new(CodeEmployeur, ZoneDocument.Standard, Language.De, "Formular für die Gesundheitsbeurteilung — Exemplar für den Arbeitgeber", Corps(Language.De, Exemplaire.Employeur)),
        new(CodeTravailleur, ZoneDocument.Medicale, Language.Fr, "Formulaire d'évaluation de santé — exemplaire du travailleur", Corps(Language.Fr, Exemplaire.Travailleur)),
        new(CodeTravailleur, ZoneDocument.Medicale, Language.Nl, "Formulier voor de gezondheidsbeoordeling — exemplaar voor de werknemer", Corps(Language.Nl, Exemplaire.Travailleur)),
        new(CodeTravailleur, ZoneDocument.Medicale, Language.De, "Formular für die Gesundheitsbeurteilung — Exemplar für den Arbeitnehmer", Corps(Language.De, Exemplaire.Travailleur)),
        new(CodeDossier, ZoneDocument.Medicale, Language.Fr, "Formulaire d'évaluation de santé — exemplaire du dossier de santé", Corps(Language.Fr, Exemplaire.Dossier)),
        new(CodeDossier, ZoneDocument.Medicale, Language.Nl, "Formulier voor de gezondheidsbeoordeling — exemplaar voor het gezondheidsdossier", Corps(Language.Nl, Exemplaire.Dossier)),
        new(CodeDossier, ZoneDocument.Medicale, Language.De, "Formular für die Gesundheitsbeurteilung — Exemplar für die Gesundheitsakte", Corps(Language.De, Exemplaire.Dossier)),
    ];

    private enum Exemplaire
    {
        Employeur,
        Travailleur,
        Dossier,
    }

    private static string Corps(Language langue, Exemplaire exemplaire)
    {
        var (titre, sousTitre, reference, date, decision, mesures, validite, piedEmployeur, recours, dossier) = langue switch
        {
            Language.Nl => (
                "Formulier voor de gezondheidsbeoordeling",
                exemplaire switch { Exemplaire.Employeur => "Exemplaar voor de werkgever", Exemplaire.Travailleur => "Exemplaar voor de werknemer", _ => "Exemplaar voor het gezondheidsdossier" },
                "Referentie van de beslissing: {{reference_decision}}",
                "Datum van de beslissing: {{date_decision}}",
                "Beslissing van de preventieadviseur-arbeidsarts",
                "Maatregelen",
                "Deze beslissing is geldig tot {{valide_jusqu_au}}.",
                "Dit document bevat enkel de beslissing van de gezondheidsbeoordeling. De medische gegevens blijven onder het medisch beroepsgeheim.",
                "Bent u het niet eens met deze beslissing, dan kunt u een beroep instellen volgens de procedure en binnen de termijnen bepaald door de codex over het welzijn op het werk. De dienst informeert u over de nadere regels.",
                "Exemplaar opgenomen in het gezondheidsdossier. Uitgereikte exemplaren: werkgever, werknemer."),
            Language.De => (
                "Formular für die Gesundheitsbeurteilung",
                exemplaire switch { Exemplaire.Employeur => "Exemplar für den Arbeitgeber", Exemplaire.Travailleur => "Exemplar für den Arbeitnehmer", _ => "Exemplar für die Gesundheitsakte" },
                "Referenz der Entscheidung: {{reference_decision}}",
                "Datum der Entscheidung: {{date_decision}}",
                "Entscheidung des Gefahrenverhütungsberaters-Arbeitsarztes",
                "Maßnahmen",
                "Diese Entscheidung gilt bis zum {{valide_jusqu_au}}.",
                "Dieses Dokument enthält ausschließlich die Entscheidung der Gesundheitsbeurteilung. Die medizinischen Daten unterliegen weiterhin dem ärztlichen Berufsgeheimnis.",
                "Wenn Sie mit dieser Entscheidung nicht einverstanden sind, können Sie nach dem Verfahren und innerhalb der Fristen des Gesetzbuches über das Wohlbefinden bei der Arbeit Beschwerde einlegen. Der Dienst informiert Sie über die Modalitäten.",
                "In die Gesundheitsakte aufgenommenes Exemplar. Ausgehändigte Exemplare: Arbeitgeber, Arbeitnehmer."),
            _ => (
                "Formulaire d'évaluation de santé",
                exemplaire switch { Exemplaire.Employeur => "Exemplaire de l'employeur", Exemplaire.Travailleur => "Exemplaire du travailleur", _ => "Exemplaire du dossier de santé" },
                "Référence de la décision : {{reference_decision}}",
                "Date de la décision : {{date_decision}}",
                "Décision du conseiller en prévention-médecin du travail",
                "Mesures",
                "Cette décision est valable jusqu'au {{valide_jusqu_au}}.",
                "Ce document ne contient que la décision d'évaluation de santé. Les données médicales restent couvertes par le secret médical.",
                "Si vous n'êtes pas d'accord avec cette décision, vous pouvez introduire un recours selon la procédure et dans les délais prévus par le code du bien-être au travail. Le service vous en communique les modalités.",
                "Exemplaire versé au dossier de santé. Exemplaires remis : employeur, travailleur."),
        };

        var pied = exemplaire switch
        {
            Exemplaire.Employeur => piedEmployeur,
            Exemplaire.Travailleur => recours,
            _ => dossier,
        };

        return $"""
            # {titre}
            ## {sousTitre}
            {reference}

            {date}

            ## {decision}
            {"{{categorie}}"}

            {"{{#si mesures}}"}
            ## {mesures}
            {"{{#chaque mesures}}"}
            - {"{{.}}"}
            {"{{/chaque}}"}
            {"{{/si}}"}

            {"{{#si valide_jusqu_au}}"}
            {validite}
            {"{{/si}}"}
            ---
            {pied}
            """;
    }
}

/// <summary>Crée les modèles de départ absents (au démarrage, idempotent) ; ils restent en brouillon jusqu'à validation.</summary>
public sealed class InitialiserModelesParDefaut(IModeleRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var existants = await repository.ListAsync(null, cancellationToken);
        var crees = 0;
        foreach (var definition in ModelesParDefaut.Definitions)
        {
            if (existants.Any(m => m.Code == definition.Code && m.Langue == definition.Langue))
            {
                continue;
            }

            repository.Add(Modele.Creer(definition.Code, definition.Langue, TypeModele.Formulaire, definition.Zone, definition.Libelle,
                ModelesParDefaut.Avertissement, definition.Contenu, ModelesParDefaut.Champs));
            crees++;
        }

        if (crees > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return crees;
    }
}
