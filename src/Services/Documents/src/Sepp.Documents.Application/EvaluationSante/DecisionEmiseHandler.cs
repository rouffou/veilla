using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Decisions;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Application.EvaluationSante;

/// <summary>
/// Libellés des catégories de décision reçues de la Surveillance médicale (SAN-30, SAN-31, DOC-01), un par code partagé
/// <see cref="CategoriesDecision"/> et dans les quatre langues (FR, NL, DE, EN) : aucun code brut n'est imprimé sur le
/// formulaire. Un code inconnu fait échouer la génération (l'événement reste à traiter, puis est rejoué quand la table
/// est complétée), comme un modèle non publié.
/// </summary>
/// <remarks>
/// Les libellés français reprennent les termes de SAN-31 tels que les cite le cahier des charges (§5.5) ; le cahier ne
/// reproduit pas le texte de l'annexe I.4-2 du code du bien-être au travail ni ses versions néerlandaise et allemande.
/// Les libellés NL, DE et EN sont des formulations neutres : à valider par le département médical, comme les libellés
/// français, contre le texte officiel de l'annexe I.4-2 avant mise en production. Aucun n'est présenté comme officiel.
/// </remarks>
public static class LibellesDecision
{
    private static readonly Dictionary<string, LocalizedLabel> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        // À valider par le département médical (NL, DE, EN : formulations neutres, hors cahier des charges).
        [CategoriesDecision.Apte] = new("Apte", "Geschikt", "Tauglich", "Fit"),
        [CategoriesDecision.ApteAvecMesures] = new("Apte avec mesures", "Geschikt mits maatregelen", "Tauglich mit Maßnahmen", "Fit with measures"),
        [CategoriesDecision.InaptitudeTemporaire] = new("Inaptitude temporaire", "Tijdelijke ongeschiktheid", "Vorübergehende Untauglichkeit", "Temporary unfitness"),
        [CategoriesDecision.InaptitudeDefinitive] = new("Inaptitude définitive", "Definitieve ongeschiktheid", "Endgültige Untauglichkeit", "Permanent unfitness"),
        [CategoriesDecision.Mutation] = new("Mutation", "Overplaatsing", "Versetzung", "Transfer to another job"),
        [CategoriesDecision.EcartementMaternite] = new("Écartement (maternité)", "Werkverwijdering (moederschap)", "Arbeitsfreistellung (Mutterschaft)", "Removal from work (maternity)"),
    };

    /// <summary>Libellé de la catégorie, ou <c>null</c> si le code n'est pas connu.</summary>
    public static LocalizedLabel? Libelle(string code) => Categories.GetValueOrDefault(code.Trim());

    /// <summary>Libellé imprimé ; jamais le code brut (#293).</summary>
    public static string Categorie(string code, Language langue)
    {
        var libelle = Libelle(code)
            ?? throw new InvalidOperationException($"Catégorie de décision sans libellé : « {code} ». Compléter LibellesDecision (codes partagés CategoriesDecision).");
        return langue == Language.En
            ? libelle.En ?? throw new InvalidOperationException($"Catégorie de décision sans libellé anglais : « {code} ».")
            : libelle.In(langue);
    }
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
