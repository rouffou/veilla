using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Application.EvaluationSante;

/// <summary>
/// Libellés des catégories de décision reçues de la Surveillance médicale. Les codes sont ceux du service Surveillance
/// médicale (développé en parallèle) : table à aligner sur sa nomenclature ; un code inconnu est imprimé tel quel.
/// </summary>
public static class LibellesDecision
{
    private static readonly Dictionary<string, LocalizedLabel> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["APTE"] = new("Apte", "Geschikt", "Tauglich"),
        ["APTE_AVEC_MESURES"] = new("Apte moyennant des mesures", "Geschikt mits maatregelen", "Tauglich mit Maßnahmen"),
        ["INAPTE_TEMPORAIRE"] = new("Inapte temporairement", "Tijdelijk ongeschikt", "Vorübergehend untauglich"),
        ["INAPTE_DEFINITIF"] = new("Inapte définitivement", "Definitief ongeschikt", "Endgültig untauglich"),
        ["DECISION_REPORTEE"] = new("Décision reportée", "Beslissing uitgesteld", "Entscheidung aufgeschoben"),
    };

    public static string Categorie(string code, Language langue) =>
        Categories.TryGetValue(code.Trim(), out var libelle) ? libelle.In(langue) : code.Trim();
}

/// <summary>
/// Formulaire d'évaluation de santé (saga de reprise, §14.6 étape 5) : à chaque <see cref="DecisionEmise"/>, génère trois
/// exemplaires — employeur (zone standard : uniquement la décision, c'est-à-dire catégorie, mesures et validité),
/// travailleur et dossier de santé (zone médicale) — puis publie les exemplaires employeur et travailleur pour que
/// Communications les dépose. Idempotent : inbox (ARC-31) et clé d'idempotence par exemplaire. Chaque document a pour
/// objet la décision (<c>ObjetType = "decision"</c>, <c>ObjetId = DecisionId</c>), repris dans <c>DocumentPublie</c> pour
/// le processus de reprise (ARC-33). <c>DecisionEmise.ExamenId</c> (ajout facultatif v1) n'est pas nécessaire : une
/// décision d'un producteur antérieur, sans examen, est traitée de la même façon.
/// </summary>
/// <remarks>
/// Si un modèle n'est pas publié dans la langue requise, l'événement échoue (exception) et reste à traiter : le bus le
/// représente, puis le place en file des messages morts, d'où il est rejoué après publication du modèle.
/// </remarks>
public sealed class DecisionEmiseHandler(GenerateurDocuments generateur, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : IIntegrationEventHandler<DecisionEmise>
{
    public const string ServiceProprietaire = "surveillance-medicale";
    public const string ObjetType = "decision";

    public async Task HandleAsync(DecisionEmise integrationEvent, CancellationToken cancellationToken)
    {
        var dateDecision = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(integrationEvent.OccurredAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels")).DateTime);
        var exemplaires = new (string Exemplaire, string Code, TypeDestinataire Type, Guid DestinataireId, bool Publier)[]
        {
            ("employeur", ModelesParDefaut.CodeEmployeur, TypeDestinataire.Affilie, integrationEvent.AffilieId, true),
            ("travailleur", ModelesParDefaut.CodeTravailleur, TypeDestinataire.Personne, integrationEvent.PersonneId, true),
            ("dossier", ModelesParDefaut.CodeDossier, TypeDestinataire.Dossier, integrationEvent.PersonneId, false),
        };

        var aPublier = new List<Document>();
        foreach (var (exemplaire, code, type, destinataire, publier) in exemplaires)
        {
            var resultat = await generateur.GenererAsync(new DemandeGeneration(
                code, type, destinataire, ServiceProprietaire, ObjetType, integrationEvent.DecisionId,
                langue => Valeurs(integrationEvent, dateDecision, langue),
                new ContexteLinguistique(null, null, null, null),
                integrationEvent.AffilieId,
                integrationEvent.PersonneId,
                exemplaire,
                $"decision:{integrationEvent.DecisionId}:{exemplaire}"), cancellationToken);

            if (!resultat.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Formulaire d'évaluation de santé non généré pour la décision {integrationEvent.DecisionId} ({exemplaire}) : {resultat.Error!.Code} — {resultat.Error.Message}");
            }

            if (publier && !resultat.Value.Existant)
            {
                aPublier.Add(resultat.Value.Document);
            }
        }

        foreach (var document in aPublier)
        {
            generateur.Publier(document, outbox);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Uniquement le contenu de l'événement : catégorie, mesures, validité, référence et date (ARC-06, §2.1).</summary>
    private static Dictionary<string, ValeurChamp> Valeurs(DecisionEmise decision, DateOnly date, Language langue)
    {
        var valeurs = new Dictionary<string, ValeurChamp>(StringComparer.Ordinal)
        {
            ["reference_decision"] = ValeurChamp.Simple(decision.DecisionId.ToString()),
            ["date_decision"] = ValeurChamp.Date(date),
            ["categorie"] = ValeurChamp.Simple(LibellesDecision.Categorie(decision.Categorie, langue)),
            ["mesures"] = ValeurChamp.Liste(decision.CodesMesures ?? []),
        };
        if (decision.ValideJusquAu is { } fin)
        {
            valeurs["valide_jusqu_au"] = ValeurChamp.Date(fin);
        }

        return valeurs;
    }
}
