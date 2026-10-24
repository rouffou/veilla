using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Application.EvaluationSante;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Application.DossierSante;

// ---- Contenu lu auprès de la Surveillance médicale (contrat JSON recopié : aucune référence au code du service) ----

public sealed record RubriqueCopie(string Code, string Valeur);

public sealed record ElementCopie(DateOnly? Date, string Intitule, IReadOnlyList<RubriqueCopie> Rubriques);

/// <summary>Contenu d'une copie validée par le CPMT (<c>GET /api/v1/demandes-copie/{id}/contenu</c>).</summary>
public sealed record CopieDossierMedicale(
    Guid DemandeId,
    Guid PersonneId,
    DateOnly DateOuverture,
    DateTimeOffset ValideeLe,
    IReadOnlyList<ElementCopie> Expositions,
    IReadOnlyList<ElementCopie> EvaluationsSante,
    IReadOnlyList<ElementCopie> Decisions,
    IReadOnlyList<ElementCopie> Questionnaires,
    IReadOnlyList<ElementCopie> Vaccinations,
    IReadOnlyList<ElementCopie> TestsTuberculiniques,
    IReadOnlyList<ElementCopie> PiecesJointes,
    IReadOnlyList<ElementCopie> MaladiesProfessionnelles);

public sealed record LibelleVaccin(string Fr, string Nl, string De, string? En);

public sealed record DoseCarnet(DateOnly Date, string VaccinCode, LibelleVaccin? Libelle, int Dose, string? NumeroLot);

public sealed record TestCarnet(DateOnly DatePose, DateOnly? DateLecture, string? Resultat, int? IndurationMm);

public sealed record RappelCarnet(string VaccinCode, LibelleVaccin? Libelle, int DoseSuivante, DateOnly DateDue, bool EnRetard);

/// <summary>Carnet de vaccination (<c>GET /api/v1/vaccinations/{id}/carnet</c>).</summary>
public sealed record CarnetVaccination(
    Guid VaccinationId,
    Guid PersonneId,
    DateOnly Date,
    IReadOnlyList<DoseCarnet> Doses,
    IReadOnlyList<TestCarnet> TestsTuberculiniques,
    IReadOnlyList<RappelCarnet> Rappels);

/// <summary>
/// Lecture du contenu médical à publier au travailleur, auprès du service Surveillance médicale, avec le compte technique
/// du service Documents (permission <c>dossier-sante:transmettre-documents</c>). C'est le seul chemin par lequel un contenu
/// médical entre dans Documents pour le travailleur : jamais par un événement (ARC-06), jamais depuis le portail.
/// </summary>
public interface ISourceDossierSante
{
    /// <exception cref="InvalidOperationException">Copie non validée, inconnue, ou service injoignable (le message est rejoué).</exception>
    Task<CopieDossierMedicale> ObtenirCopieAsync(Guid demandeId, CancellationToken cancellationToken);

    /// <returns>Le carnet, ou <c>null</c> si la vaccination n'existe plus (dossier purgé).</returns>
    Task<CarnetVaccination?> ObtenirCarnetAsync(Guid vaccinationId, CancellationToken cancellationToken);
}

/// <summary>
/// Modèles médicaux du portail travailleur (POR-13) : copie du dossier de santé (SAN-43) et carnet de vaccination (SAN-50),
/// en FR, NL et DE, zone médicale. Créés en <b>brouillon</b> au démarrage : à relire et valider par le CPMT dirigeant
/// (<c>documents:modele-valider-medical</c>), puis publier ; tant qu'ils ne le sont pas, la génération échoue et le message
/// est rejoué après publication.
/// </summary>
public static class ModelesDossierSante
{
    public const string CodeCopie = "SANTE.COPIE_DOSSIER";
    public const string CodeCarnet = "SANTE.CARNET_VACCINATION";

    /// <summary>Objets documentés repris dans <c>DocumentPublie.ObjetType</c> (contrat avec la Surveillance médicale).</summary>
    public const string ObjetCopie = "demande-copie-dossier";
    public const string ObjetCarnet = "carnet-vaccination";

    public const string Avertissement =
        "Modèle de départ fourni avec le logiciel : à relire, compléter et valider par le département médical (CPMT dirigeant) avant publication.";

    public static IReadOnlyList<string> SectionsCopie { get; } =
        ["expositions", "evaluations", "decisions", "questionnaires", "vaccinations", "tests_tuberculiniques", "pieces_jointes", "maladies_professionnelles"];

    public static IReadOnlyList<ChampDeclare> ChampsCopie { get; } =
    [
        new("reference_demande", TypeChamp.Texte, true, "Référence de la demande"),
        new("date_copie", TypeChamp.Date, true, "Date de la copie (validation par le CPMT)"),
        new("date_ouverture", TypeChamp.Date, true, "Ouverture du dossier"),
        .. SectionsCopie.Select(s => new ChampDeclare(s, TypeChamp.Liste, false, $"Partie du dossier : {s}")),
        new("copie_partielle", TypeChamp.Booleen, false, "Une partie dépasse la taille d'un document"),
    ];

    public static IReadOnlyList<ChampDeclare> ChampsCarnet { get; } =
    [
        new("date_carnet", TypeChamp.Date, true, "Date du carnet"),
        new("vaccinations", TypeChamp.Liste, false, "Doses administrées"),
        new("tests_tuberculiniques", TypeChamp.Liste, false, "Tests tuberculiniques"),
        new("rappels", TypeChamp.Liste, false, "Prochaines doses et rappels"),
    ];

    public sealed record Definition(string Code, Language Langue, string Libelle, string Contenu, IReadOnlyList<ChampDeclare> Champs);

    /// <summary>
    /// Libellé imprimé d'une rubrique de la copie (codes <c>RubriquesCopie</c> de la Surveillance médicale), ou <c>null</c>
    /// si le code est inconnu (il serait alors imprimé tel quel).
    /// </summary>
    public static string? LibelleRubrique(string code, Language langue) => Textes.Pour(langue).Rubriques.GetValueOrDefault(code);

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        .. new[] { Language.Fr, Language.Nl, Language.De }.Select(l => new Definition(CodeCopie, l, Textes.Pour(l).TitreCopie, CorpsCopie(l), ChampsCopie)),
        .. new[] { Language.Fr, Language.Nl, Language.De }.Select(l => new Definition(CodeCarnet, l, Textes.Pour(l).TitreCarnet, CorpsCarnet(l), ChampsCarnet)),
    ];

    private static string Section(string titre, string champ, string aucun) => $$$"""
        ## {{{titre}}}
        {{#si {{{champ}}}}}
        {{#chaque {{{champ}}}}}
        - {{.}}
        {{/chaque}}
        {{#sinon}}
        {{{aucun}}}
        {{/si}}

        """;

    private static string CorpsCopie(Language langue)
    {
        var t = Textes.Pour(langue);
        var sections = string.Concat(SectionsCopie.Select((s, i) => Section(t.Sections[i], s, t.Aucun)));
        return $$$"""
            # {{{t.TitreCopie}}}
            {{{t.Reference}}} {{reference_demande}}

            {{{t.DateCopie}}} {{date_copie}}

            {{{t.Ouverture}}} {{date_ouverture}}

            {{{t.IntroCopie}}}

            {{{sections}}}{{#si copie_partielle}}
            {{{t.Partielle}}}
            {{/si}}
            ---
            {{{t.PiedCopie}}}
            """;
    }

    private static string CorpsCarnet(Language langue)
    {
        var t = Textes.Pour(langue);
        return $$$"""
            # {{{t.TitreCarnet}}}
            {{{t.DateCarnet}}} {{date_carnet}}

            {{{Section(t.Doses, "vaccinations", t.Aucun)}}}{{{Section(t.Tests, "tests_tuberculiniques", t.Aucun)}}}{{{Section(t.Rappels, "rappels", t.Aucun)}}}---
            {{{t.PiedCarnet}}}
            """;
    }
}

/// <summary>
/// Textes des modèles et libellés des rubriques, FR, NL et DE (NF-41 : l'anglais est réservé aux portails). Formulations
/// neutres, <b>à valider</b> par le département médical.
/// </summary>
internal sealed record Textes(
    string TitreCopie, string Reference, string DateCopie, string Ouverture, string IntroCopie, IReadOnlyList<string> Sections, string Aucun,
    string Partielle, string PiedCopie, string TitreCarnet, string DateCarnet, string Doses, string Tests, string Rappels, string PiedCarnet,
    string Dose, string Lot, string Lecture, string Induration, string AuPlusTard, string EnRetard, IReadOnlyDictionary<string, string> Rubriques)
{
    private static readonly Textes Fr = new(
        "Copie de votre dossier de santé", "Référence de la demande :", "Copie arrêtée le", "Dossier ouvert le",
        "Copie établie à votre demande (droit d'accès du travailleur, loi du 22 août 2002 relative aux droits du patient), validée par le conseiller en prévention-médecin du travail.",
        ["Données d'exposition", "Évaluations de santé", "Décisions d'évaluation de santé", "Questionnaires de santé", "Vaccinations", "Tests tuberculiniques", "Pièces jointes", "Maladies professionnelles"],
        "Aucun élément.", "Certaines parties dépassent la taille d'un document : le médecin du travail vous en remet le complément sur demande.",
        "Document confidentiel couvert par le secret médical, destiné à vous seul. Les pièces jointes (rapports, imageries) vous sont remises sur demande.",
        "Carnet de vaccination", "Carnet arrêté le", "Doses administrées", "Tests tuberculiniques", "Prochaines doses et rappels",
        "Document confidentiel couvert par le secret médical. Il est mis à jour après chaque vaccination.",
        "dose", "lot", "lecture le", "induration", "à partir du", "en retard",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NIVEAU"] = "Niveau",
            ["FIN"] = "Fin",
            ["STATUT"] = "Statut",
            ["ANAMNESE"] = "Anamnèse",
            ["EXAMEN_CLINIQUE"] = "Examen clinique",
            ["RESULTAT"] = "Résultat",
            ["COMMENTAIRE"] = "Commentaire",
            ["MESURES"] = "Mesures",
            ["VALIDITE"] = "Valable jusqu'au",
            ["JUSTIFICATION"] = "Justification",
            ["RECOMMANDATIONS"] = "Recommandations",
            ["REPONSE"] = "Réponse",
            ["DOSE"] = "Dose",
            ["REMARQUE"] = "Remarque",
            ["LECTURE"] = "Lecture",
            ["INDURATION"] = "Induration",
            ["TITRE"] = "Titre",
            ["DESCRIPTION"] = "Description",
            ["AGENT"] = "Agent causal",
            ["LOT"] = "Lot",
        });

    private static readonly Textes Nl = new(
        "Kopie van uw gezondheidsdossier", "Referentie van de aanvraag:", "Kopie opgemaakt op", "Dossier geopend op",
        "Kopie opgemaakt op uw verzoek (inzagerecht van de werknemer, wet van 22 augustus 2002 betreffende de rechten van de patiënt), gevalideerd door de preventieadviseur-arbeidsarts.",
        ["Blootstellingsgegevens", "Gezondheidsbeoordelingen", "Beslissingen van de gezondheidsbeoordeling", "Gezondheidsvragenlijsten", "Vaccinaties", "Tuberculinetests", "Bijlagen", "Beroepsziekten"],
        "Geen gegevens.", "Sommige delen zijn te groot voor één document: de arbeidsarts bezorgt u de aanvulling op verzoek.",
        "Vertrouwelijk document onder het medisch beroepsgeheim, uitsluitend voor u bestemd. De bijlagen (verslagen, beeldvorming) worden u op verzoek bezorgd.",
        "Vaccinatieboekje", "Boekje opgemaakt op", "Toegediende dosissen", "Tuberculinetests", "Volgende dosissen en herhalingen",
        "Vertrouwelijk document onder het medisch beroepsgeheim. Het wordt na elke vaccinatie bijgewerkt.",
        "dosis", "lot", "afgelezen op", "induratie", "vanaf", "achterstallig",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NIVEAU"] = "Niveau",
            ["FIN"] = "Einde",
            ["STATUT"] = "Status",
            ["ANAMNESE"] = "Anamnese",
            ["EXAMEN_CLINIQUE"] = "Klinisch onderzoek",
            ["RESULTAT"] = "Resultaat",
            ["COMMENTAIRE"] = "Opmerking",
            ["MESURES"] = "Maatregelen",
            ["VALIDITE"] = "Geldig tot",
            ["JUSTIFICATION"] = "Motivering",
            ["RECOMMANDATIONS"] = "Aanbevelingen",
            ["REPONSE"] = "Antwoord",
            ["DOSE"] = "Dosis",
            ["REMARQUE"] = "Opmerking",
            ["LECTURE"] = "Aflezing",
            ["INDURATION"] = "Induratie",
            ["TITRE"] = "Titel",
            ["DESCRIPTION"] = "Beschrijving",
            ["AGENT"] = "Oorzakelijk agens",
            ["LOT"] = "Lot",
        });

    private static readonly Textes De = new(
        "Kopie Ihrer Gesundheitsakte", "Referenz des Antrags:", "Kopie erstellt am", "Akte eröffnet am",
        "Auf Ihren Antrag erstellte Kopie (Einsichtsrecht des Arbeitnehmers, Gesetz vom 22. August 2002 über die Rechte des Patienten), vom Gefahrenverhütungsberater-Arbeitsarzt freigegeben.",
        ["Expositionsdaten", "Gesundheitsbeurteilungen", "Entscheidungen der Gesundheitsbeurteilung", "Gesundheitsfragebögen", "Impfungen", "Tuberkulintests", "Anlagen", "Berufskrankheiten"],
        "Keine Angaben.", "Einige Teile übersteigen den Umfang eines Dokuments: der Arbeitsarzt übergibt Ihnen die Ergänzung auf Anfrage.",
        "Vertrauliches Dokument unter ärztlicher Schweigepflicht, ausschließlich für Sie bestimmt. Die Anlagen (Berichte, Bildgebung) werden Ihnen auf Anfrage übergeben.",
        "Impfpass", "Impfpass erstellt am", "Verabreichte Dosen", "Tuberkulintests", "Nächste Dosen und Auffrischungen",
        "Vertrauliches Dokument unter ärztlicher Schweigepflicht. Es wird nach jeder Impfung aktualisiert.",
        "Dosis", "Charge", "abgelesen am", "Induration", "ab dem", "überfällig",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NIVEAU"] = "Niveau",
            ["FIN"] = "Ende",
            ["STATUT"] = "Status",
            ["ANAMNESE"] = "Anamnese",
            ["EXAMEN_CLINIQUE"] = "Klinische Untersuchung",
            ["RESULTAT"] = "Ergebnis",
            ["COMMENTAIRE"] = "Kommentar",
            ["MESURES"] = "Maßnahmen",
            ["VALIDITE"] = "Gültig bis",
            ["JUSTIFICATION"] = "Begründung",
            ["RECOMMANDATIONS"] = "Empfehlungen",
            ["REPONSE"] = "Antwort",
            ["DOSE"] = "Dosis",
            ["REMARQUE"] = "Bemerkung",
            ["LECTURE"] = "Ablesung",
            ["INDURATION"] = "Induration",
            ["TITRE"] = "Titel",
            ["DESCRIPTION"] = "Beschreibung",
            ["AGENT"] = "Ursächlicher Stoff",
            ["LOT"] = "Charge",
        });

    public static Textes Pour(Language langue) => langue switch
    {
        Language.Nl => Nl,
        Language.De => De,
        _ => Fr,
    };
}

/// <summary>Mise en lignes du contenu médical pour le moteur de fusion (listes de 100 éléments, valeurs de 2 000 caractères au plus).</summary>
internal static class MiseEnPage
{
    private const int LongueurLigne = 1_900;

    public static string Date(DateOnly date, Language langue) =>
        date.ToString(langue == Language.De ? "d. MMMM yyyy" : "d MMMM yyyy", Culture(langue));

    public static CultureInfo Culture(Language langue) => CultureInfo.GetCultureInfo(langue switch
    {
        Language.Nl => "nl-BE",
        Language.De => "de-BE",
        _ => "fr-BE",
    });

    /// <summary>Une ligne d'en-tête par élément, puis une ligne par rubrique (coupée si elle est trop longue).</summary>
    public static (List<string> Lignes, bool Partielle) Section(IEnumerable<ElementCopie> elements, Language langue, Func<string, string> intitule)
    {
        var t = Textes.Pour(langue);
        var lignes = new List<string>();
        foreach (var e in elements)
        {
            lignes.Add(e.Date is { } d ? $"{Date(d, langue)} — {intitule(e.Intitule)}" : intitule(e.Intitule));
            foreach (var r in e.Rubriques)
            {
                lignes.AddRange(Couper($"· {t.Rubriques.GetValueOrDefault(r.Code, r.Code)} : {r.Valeur}"));
            }
        }

        return Borner(lignes);
    }

    public static (List<string> Lignes, bool Partielle) Borner(List<string> lignes) =>
        lignes.Count <= MoteurFusion.ElementsMaximaux ? (lignes, false) : (lignes[..MoteurFusion.ElementsMaximaux], true);

    public static string Vaccin(string code, LibelleVaccin? libelle, Language langue) =>
        libelle is null ? code : langue switch { Language.Nl => libelle.Nl, Language.De => libelle.De, _ => libelle.Fr };

    private static IEnumerable<string> Couper(string ligne)
    {
        for (var i = 0; i < ligne.Length; i += LongueurLigne)
        {
            yield return ligne.Substring(i, Math.Min(LongueurLigne, ligne.Length - i));
        }
    }
}

/// <summary>
/// SAN-43, POR-13 : à la validation d'une copie par le CPMT (<see cref="CopieDossierValidee"/>, identifiants seulement),
/// lit le contenu auprès de la Surveillance médicale, génère la copie en PDF/A dans la <b>zone médicale</b> (clé et
/// stockage de la zone) et la publie au travailleur (<c>DocumentPublie</c>, objet <c>demande-copie-dossier</c>), qui la
/// télécharge par le portail. Génération et publication sont journalisées (NF-04). Idempotent (inbox et clé
/// <c>copie-dossier:&lt;demande&gt;</c>) ; un modèle non publié fait échouer l'événement, rejoué après publication.
/// </summary>
public sealed class CopieDossierValideeHandler(
    ISourceDossierSante source, GenerateurDocuments generateur, IJournalAcces journal, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : IIntegrationEventHandler<CopieDossierValidee>
{
    public async Task HandleAsync(CopieDossierValidee integrationEvent, CancellationToken cancellationToken)
    {
        var copie = await source.ObtenirCopieAsync(integrationEvent.DemandeId, cancellationToken);
        if (copie.PersonneId != integrationEvent.PersonneId)
        {
            throw new InvalidOperationException($"Copie {integrationEvent.DemandeId} : la personne du contenu ne correspond pas à l'événement.");
        }

        var resultat = await generateur.GenererAsync(new DemandeGeneration(
            ModelesDossierSante.CodeCopie, TypeDestinataire.Personne, copie.PersonneId, DecisionEmiseHandler.ServiceProprietaire,
            ModelesDossierSante.ObjetCopie, copie.DemandeId, langue => Valeurs(copie, langue),
            new ContexteLinguistique(null, null, null, null), null, copie.PersonneId, "travailleur", $"copie-dossier:{copie.DemandeId}"), cancellationToken);
        await PublicationTravailleur.TerminerAsync(resultat, $"copie du dossier {copie.DemandeId}", generateur, journal, outbox, unitOfWork, cancellationToken);
    }

    internal static Dictionary<string, ValeurChamp> Valeurs(CopieDossierMedicale c, Language langue)
    {
        var sections = new[] { c.Expositions, c.EvaluationsSante, c.Decisions, c.Questionnaires, c.Vaccinations, c.TestsTuberculiniques, c.PiecesJointes, c.MaladiesProfessionnelles };
        var valeurs = new Dictionary<string, ValeurChamp>(StringComparer.Ordinal)
        {
            ["reference_demande"] = ValeurChamp.Simple(c.DemandeId.ToString()),
            ["date_copie"] = ValeurChamp.Date(DateOnly.FromDateTime(c.ValideeLe.UtcDateTime)),
            ["date_ouverture"] = ValeurChamp.Date(c.DateOuverture),
        };
        var partielle = false;
        for (var i = 0; i < sections.Length; i++)
        {
            // Les décisions sont imprimées sous leur libellé (SAN-31), jamais sous le code brut.
            Func<string, string> intitule = i == 2 ? code => LibellesDecision.Libelle(code)?.In(langue) ?? code : code => code;
            var (lignes, tronquee) = MiseEnPage.Section(sections[i], langue, intitule);
            valeurs[ModelesDossierSante.SectionsCopie[i]] = ValeurChamp.Liste(lignes);
            partielle |= tronquee;
        }

        valeurs["copie_partielle"] = ValeurChamp.Simple(partielle ? "true" : "false");
        return valeurs;
    }
}

/// <summary>
/// SAN-50, POR-13 : après chaque vaccination (<see cref="VaccinationAdministree"/>), le carnet de vaccination à jour est lu
/// auprès de la Surveillance médicale, généré en zone médicale et publié au travailleur (objet <c>carnet-vaccination</c>).
/// Choix : publication après chaque vaccination plutôt qu'à la demande — l'événement existe déjà, aucune nouvelle demande
/// ni route d'écriture n'est nécessaire, et le travailleur dispose toujours de la dernière version dans « Mes documents ».
/// Idempotent (inbox et clé <c>carnet-vaccination:&lt;vaccination&gt;</c>).
/// </summary>
public sealed class VaccinationAdministreeHandler(
    ISourceDossierSante source, GenerateurDocuments generateur, IJournalAcces journal, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : IIntegrationEventHandler<VaccinationAdministree>
{
    public async Task HandleAsync(VaccinationAdministree integrationEvent, CancellationToken cancellationToken)
    {
        var carnet = await source.ObtenirCarnetAsync(integrationEvent.VaccinationId, cancellationToken);
        if (carnet is null || carnet.PersonneId != integrationEvent.PersonneId)
        {
            // Vaccination disparue (dossier purgé) : plus rien à publier.
            return;
        }

        var resultat = await generateur.GenererAsync(new DemandeGeneration(
            ModelesDossierSante.CodeCarnet, TypeDestinataire.Personne, carnet.PersonneId, DecisionEmiseHandler.ServiceProprietaire,
            ModelesDossierSante.ObjetCarnet, carnet.VaccinationId, langue => Valeurs(carnet, langue),
            new ContexteLinguistique(null, null, null, null), null, carnet.PersonneId, "travailleur", $"carnet-vaccination:{carnet.VaccinationId}"), cancellationToken);
        await PublicationTravailleur.TerminerAsync(resultat, $"carnet de vaccination {carnet.VaccinationId}", generateur, journal, outbox, unitOfWork, cancellationToken);
    }

    internal static Dictionary<string, ValeurChamp> Valeurs(CarnetVaccination c, Language langue)
    {
        var t = Textes.Pour(langue);
        var doses = c.Doses.Select(d =>
            $"{MiseEnPage.Date(d.Date, langue)} — {MiseEnPage.Vaccin(d.VaccinCode, d.Libelle, langue)} — {t.Dose} {d.Dose}" + (d.NumeroLot is { } lot ? $" — {t.Lot} {lot}" : string.Empty));
        var tests = c.TestsTuberculiniques.Select(x =>
            MiseEnPage.Date(x.DatePose, langue)
            + (x.DateLecture is { } l ? $" — {t.Lecture} {MiseEnPage.Date(l, langue)}" : string.Empty)
            + (x.Resultat is { } r ? $" — {r}" : string.Empty)
            + (x.IndurationMm is { } mm ? $" — {t.Induration} {mm} mm" : string.Empty));
        var rappels = c.Rappels.Select(r =>
            $"{MiseEnPage.Vaccin(r.VaccinCode, r.Libelle, langue)} — {t.Dose} {r.DoseSuivante} — {t.AuPlusTard} {MiseEnPage.Date(r.DateDue, langue)}" + (r.EnRetard ? $" ({t.EnRetard})" : string.Empty));
        return new Dictionary<string, ValeurChamp>(StringComparer.Ordinal)
        {
            ["date_carnet"] = ValeurChamp.Date(c.Date),
            ["vaccinations"] = ValeurChamp.Liste(MiseEnPage.Borner([.. doses]).Lignes),
            ["tests_tuberculiniques"] = ValeurChamp.Liste(MiseEnPage.Borner([.. tests]).Lignes),
            ["rappels"] = ValeurChamp.Liste(MiseEnPage.Borner([.. rappels]).Lignes),
        };
    }
}

internal static class PublicationTravailleur
{
    /// <summary>Publie au travailleur le document généré (sauf s'il l'était déjà), trace la publication et valide la transaction.</summary>
    public static async Task TerminerAsync(
        Result<DocumentGenere> resultat, string objet, GenerateurDocuments generateur, IJournalAcces journal, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (!resultat.IsSuccess)
        {
            throw new InvalidOperationException($"Document non généré ({objet}) : {resultat.Error!.Code} — {resultat.Error.Message}");
        }

        if (resultat.Value.Existant)
        {
            return;
        }

        var document = resultat.Value.Document;
        if (document.Zone != ZoneDocument.Medicale)
        {
            throw new InvalidOperationException($"Document {objet} : le modèle {document.CodeModele} doit être de la zone médicale.");
        }

        generateur.Publier(document, outbox);
        journal.Enregistrer(ActionAudit.Modification, document.Zone, document.Id, "Publication au travailleur (POR-13)");
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Crée en brouillon les modèles médicaux du portail travailleur absents (au démarrage, idempotent).</summary>
public static class InitialisationModelesDossierSante
{
    public static int Ajouter(IReadOnlyList<Modele> existants, IModeleRepository repository, IZonesServies zonesServies)
    {
        var crees = 0;
        if (!zonesServies.Sert(ZoneDocument.Medicale))
        {
            return crees;
        }

        foreach (var d in ModelesDossierSante.Definitions.Where(d => !existants.Any(m => m.Code == d.Code && m.Langue == d.Langue)))
        {
            repository.Add(Modele.Creer(d.Code, d.Langue, TypeModele.Formulaire, ZoneDocument.Medicale, d.Libelle, ModelesDossierSante.Avertissement, d.Contenu, d.Champs));
            crees++;
        }

        return crees;
    }
}
