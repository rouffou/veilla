using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Personnes;
using Sepp.Obligations.Adapters.Traitement;
using Sepp.Sagas.Tests.Plateforme;

using Shouldly;

namespace Sepp.Sagas.Tests;

/// <summary>Un travailleur et sa reprise du travail annoncée par son employeur, dans la fenêtre de temps d'un scénario.</summary>
public sealed record Travailleur(Guid Personne, Guid Affilie, DateOnly DateReprise, DateOnly DebutAbsence)
{
    /// <summary>Mercredi suivant la reprise : jour du créneau d'urgence, dans le délai légal.</summary>
    public DateOnly JourCreneau => DateReprise.AddDays(2);
}

/// <summary>Instantané comparable de l'état final d'un scénario (sans identifiants), pour comparer deux exécutions.</summary>
public sealed record EtatFinal(
    string StatutReprise,
    string StatutObligation,
    bool HorsDelai,
    int Documents,
    int DocumentsPublies,
    string Messages,
    string StatutRendezVous);

/// <summary>Gestes du scénario sur la plateforme : appels d'API par rôle, pompe d'événements, lectures d'état.</summary>
public sealed partial class OutilsScenario(PlateformeSaga plateforme)
{
    /// <summary>Termes qui trahiraient un contenu clinique, psychosocial ou une donnée d'identité (ARC-06, voir ContractRulesTests).</summary>
    private static readonly string[] TermesInterdits =
    [
        "niss", "nom", "prenom", "adresse", "email", "telephone", "naissance", "diagnostic", "anamnese", "clinique",
        "pathologie", "symptome", "traitement", "observation", "resultat", "commentaire", "description", "contenu",
        "faits", "temoignage", "remarque", "note",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PlateformeSaga Plateforme => plateforme;

    /// <summary>Fenêtre de temps du scénario : un lundi, huit semaines après celle du scénario précédent (aucun créneau partagé).</summary>
    public Travailleur Demarrer(int fenetre, int semainesAbsence = 5)
    {
        plateforme.Bus.Reinitialiser(20261009 + fenetre);
        plateforme.Horloge.Demarrer(new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero).AddDays(56 * fenetre));
        var aujourdhui = Aujourdhui();
        return new Travailleur(Guid.CreateVersion7(), Guid.CreateVersion7(), aujourdhui, aujourdhui.AddDays(-7 * semainesAbsence));
    }

    public DateOnly Aujourdhui() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(plateforme.Horloge.GetUtcNow(), Bruxelles).DateTime);

    public Task PomperAsync() => plateforme.PomperAsync(Ct);

    /// <summary>Envoie les messages échus de Communications puis propage les preuves d'envoi.</summary>
    public async Task ExpedierEtPomperAsync()
    {
        await plateforme.ExpedierMessagesAsync(Ct);
        await plateforme.PomperAsync(Ct);
    }

    // --- Planification --------------------------------------------------------------------------------------------

    /// <summary>Un conseiller compétent pour l'examen de reprise et un créneau le jour indiqué (PLA-01, PLA-03, PLA-06).</summary>
    public async Task OuvrirCreneauAsync(DateOnly jour)
    {
        var responsable = plateforme.Planification.Client(Roles.ResponsableCentre);
        var planificateur = plateforme.Planification.Client(Roles.Planificateur);
        var lieu = await CreerAsync(responsable, "/api/v1/lieux", new { type = "CentreFixe", nom = $"Centre {Guid.NewGuid():N}", codePostal = "5000" });
        var conseiller = await CreerAsync(responsable, "/api/v1/ressources",
            new { type = "Conseiller", libelle = "Dr Reprise", referenceId = $"kc-{Guid.NewGuid():N}", competences = new[] { TypesExamen.ExamenReprise } });
        var modele = await CreerAsync(planificateur, "/api/v1/modeles-agenda", new
        {
            ressourceId = conseiller,
            lieuId = lieu,
            valideDu = jour.AddDays(-30),
            plages = new[]
            {
                new
                {
                    jour = jour.DayOfWeek.ToString().ToLowerInvariant(),
                    debut = "09:00",
                    fin = "10:00",
                    typeActe = TypesExamen.ExamenReprise,
                    dureeMinutes = 30,
                    reserveUrgence = false,
                    ouvertEnLigne = false,
                },
            },
        });
        var generation = await planificateur.PostAsJsonAsync($"/api/v1/modeles-agenda/{modele}/creneaux", new { du = jour, au = jour }, Json, Ct);
        generation.StatusCode.ShouldBe(HttpStatusCode.OK, await generation.Content.ReadAsStringAsync(Ct));
    }

    public async Task<JsonElement> RendezVousDeAsync(Guid personne)
    {
        var liste = await plateforme.Planification.Client(Roles.Planificateur).GetFromJsonAsync<JsonElement>($"/api/v1/rendez-vous?personneId={personne}", Json, Ct);
        return liste;
    }

    public async Task<JsonElement> RendezVousAsync(Guid rendezVousId) =>
        await plateforme.Planification.Client(Roles.Planificateur).GetFromJsonAsync<JsonElement>($"/api/v1/rendez-vous/{rendezVousId}", Json, Ct);

    public async Task ConstaterAbsenceAsync(Guid rendezVousId)
    {
        var reponse = await plateforme.Planification.Client(Roles.Planificateur).PostAsync($"/api/v1/rendez-vous/{rendezVousId}/absence", null, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reponse.Content.ReadAsStringAsync(Ct));
    }

    // --- Obligations ----------------------------------------------------------------------------------------------

    /// <summary>Annonce la reprise comme le fait l'application interne (gestionnaire) : <c>POST /api/v1/reprises</c>.</summary>
    public async Task<Guid> AnnoncerAsync(Travailleur travailleur)
    {
        // Préalable (#298) : le travailleur est occupé chez l'affilié avant toute annonce de reprise.
        await EtablirOccupationAsync(travailleur);
        var reponse = await plateforme.Obligations.Client(Roles.GestionnaireDossiers, "gest-1").PostAsJsonAsync("/api/v1/reprises",
            new { personneId = travailleur.Personne, affilieId = travailleur.Affilie, dateReprise = travailleur.DateReprise, debutAbsence = travailleur.DebutAbsence }, Json, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Ct));
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("repriseId").GetGuid();
    }

    /// <summary>Remet à Obligations l'événement <c>personnes.occupation-debutee</c> du travailleur (occupation en cours depuis un an).</summary>
    public async Task EtablirOccupationAsync(Travailleur travailleur)
    {
        var evenement = new OccupationDebutee(Guid.CreateVersion7(), travailleur.Personne, travailleur.Affilie, travailleur.DateReprise.AddYears(-1));
        await plateforme.Obligations.Distribuer(
            Guid.CreateVersion7(), EventContractAttribute.Of(typeof(OccupationDebutee)).FullName, JsonSerializer.Serialize(evenement, EventSerialization.Options), Ct);
    }

    /// <summary>Annonce sans vérifier l'issue : renvoie le statut HTTP et le code d'erreur éventuel (ProblemDetails).</summary>
    public async Task<(HttpStatusCode Statut, string? Code)> TenterAnnonceAsync(Travailleur travailleur)
    {
        var reponse = await plateforme.Obligations.Client(Roles.GestionnaireDossiers, "gest-1").PostAsJsonAsync("/api/v1/reprises",
            new { personneId = travailleur.Personne, affilieId = travailleur.Affilie, dateReprise = travailleur.DateReprise, debutAbsence = travailleur.DebutAbsence }, Json, Ct);
        var corps = await reponse.Content.ReadAsStringAsync(Ct);
        var code = reponse.IsSuccessStatusCode ? null : JsonDocument.Parse(corps).RootElement.GetProperty("code").GetString();
        return (reponse.StatusCode, code);
    }

    public async Task<JsonElement> RepriseAsync(Guid repriseId) =>
        await plateforme.Obligations.Client(Roles.Cpmt, "cpmt-1").GetFromJsonAsync<JsonElement>($"/api/v1/reprises/{repriseId}", Json, Ct);

    public async Task AnnulerRepriseAsync(Guid repriseId)
    {
        var reponse = await plateforme.Obligations.Client(Roles.GestionnaireDossiers, "gest-1")
            .PostAsJsonAsync($"/api/v1/reprises/{repriseId}/annulation", new { motif = "RepriseReportee" }, Json, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reponse.Content.ReadAsStringAsync(Ct));
    }

    public async Task<JsonElement> ObligationAsync(Guid personne)
    {
        var obligations = await plateforme.Obligations.Client(Roles.Cpmt, "cpmt-1").GetFromJsonAsync<JsonElement>($"/api/v1/personnes/{personne}/obligations", Json, Ct);
        return obligations.EnumerateArray().Single(o => o.GetProperty("type").GetString() == TypesExamen.ExamenReprise);
    }

    /// <summary>Passage des minuteries de reprise puis du traitement quotidien des échéances (ObligationEchue).</summary>
    public async Task TraiterEcheancesAsync()
    {
        await plateforme.Obligations.Services.GetRequiredService<MinuteriesRepriseService>().ExecuterUneFoisAsync(Ct);
        var reponse = await plateforme.Obligations.Client(Roles.Planificateur).PostAsync("/api/v1/traitement-echeances", null, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(Ct));
    }

    // --- Surveillance médicale ------------------------------------------------------------------------------------

    /// <summary>Consultation de reprise : dossier, examen sur le rendez-vous, clôture. Renvoie l'identifiant de l'examen.</summary>
    public async Task<(Guid Dossier, Guid Examen)> RealiserExamenAsync(Travailleur travailleur, Guid rendezVousId, string observationSecrete)
    {
        var cpmt = plateforme.SurveillanceMedicale.Client(Roles.Cpmt, "cpmt-1");
        var dossier = await CreerAsync(cpmt, "/api/v1/dossiers", new { personneId = travailleur.Personne });
        var examen = await CreerAsync(cpmt, "/api/v1/examens", new
        {
            dossierId = dossier,
            typeExamen = TypesExamen.ExamenReprise,
            affilieId = travailleur.Affilie,
            rendezVousId,
            date = Aujourdhui(),
        });
        var observation = await cpmt.PutAsJsonAsync($"/api/v1/examens/{examen}/observation", new { anamnese = observationSecrete }, Json, Ct);
        observation.StatusCode.ShouldBe(HttpStatusCode.NoContent, await observation.Content.ReadAsStringAsync(Ct));
        var cloture = await cpmt.PostAsJsonAsync($"/api/v1/examens/{examen}/cloture", new { date = Aujourdhui() }, Json, Ct);
        cloture.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cloture.Content.ReadAsStringAsync(Ct));
        return (dossier, examen);
    }

    /// <summary>Rédige puis signe la décision (SAN-31 à SAN-33) : publie <c>DecisionEmise</c> avec l'<c>ExamenId</c>.</summary>
    public async Task<Guid> SignerDecisionAsync(Guid examen, string justificationSecrete, string recommandationsSecretes)
    {
        var cpmt = plateforme.SurveillanceMedicale.Client(Roles.Cpmt, "cpmt-1");
        var decision = await CreerAsync(cpmt, $"/api/v1/examens/{examen}/decision", new
        {
            categorie = "ApteAvecMesures",
            mesures = new[] { "PROTECTION_AUDITIVE" },
            valideJusquAu = Aujourdhui().AddYears(1),
            justification = justificationSecrete,
            recommandations = recommandationsSecretes,
        });
        var signature = await cpmt.PostAsync($"/api/v1/decisions/{decision}/signature", null, Ct);
        signature.StatusCode.ShouldBe(HttpStatusCode.OK, await signature.Content.ReadAsStringAsync(Ct));
        return decision;
    }

    // --- Documents et Communications ------------------------------------------------------------------------------

    public async Task<JsonElement> DocumentsDeDecisionAsync(Guid decision) =>
        await plateforme.Documents.Client(Roles.Cpmt).GetFromJsonAsync<JsonElement>($"/api/v1/documents?objetType=decision&objetId={decision}", Json, Ct);

    /// <summary>Journal des messages adressés aux destinataires donnés (journal paginé de Communications, DOC-05).</summary>
    public async Task<List<JsonElement>> MessagesAsync(params Guid[] destinataires)
    {
        var resultat = new List<JsonElement>();
        foreach (var destinataire in destinataires)
        {
            var page = await plateforme.Communications.Client(Roles.GestionnaireDossiers)
                .GetFromJsonAsync<JsonElement>($"/api/v1/messages?destinataireId={destinataire}&taille=100", Json, Ct);
            resultat.AddRange(page.GetProperty("messages").EnumerateArray());
        }

        return resultat;
    }

    // --- Bus -------------------------------------------------------------------------------------------------------

    public IReadOnlyList<MessagePublie> Publies(string sujet) => [.. plateforme.Bus.Publies.Where(m => m.Sujet == sujet)];

    public IReadOnlyList<MessagePublie> PubliesPour(string sujet, Guid identifiant) =>
        [.. Publies(sujet).Where(m => m.Charge.Contains(identifiant.ToString(), StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// ARC-06 : aucune charge d'événement ne porte un champ interdit (nom de propriété évoquant une donnée de santé ou d'identité),
    /// aucune valeur secrète saisie pendant le scénario n'en sort, et aucun message n'est parti en lettre morte.
    /// </summary>
    public void VerifierCharges(params string[] valeursSecretes)
    {
        plateforme.Bus.LettresMortes.ShouldBeEmpty("des messages ont échoué après toutes leurs livraisons");
        plateforme.Bus.Publies.ShouldNotBeEmpty();
        foreach (var message in plateforme.Bus.Publies)
        {
            using var document = JsonDocument.Parse(message.Charge);
            foreach (var nom in NomsDePropriete(document.RootElement))
            {
                var mots = MotsCamel().Matches(nom).Select(m => m.Value.ToLowerInvariant());
                mots.Intersect(TermesInterdits).ShouldBeEmpty($"{message.Sujet} : le champ « {nom} » est interdit dans un événement (ARC-06).");
            }

            foreach (var secret in valeursSecretes)
            {
                message.Charge.ShouldNotContain(secret, Case.Insensitive, $"{message.Sujet} laisse sortir une donnée saisie en zone médicale.");
            }
        }
    }

    private static IEnumerable<string> NomsDePropriete(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propriete in element.EnumerateObject())
                {
                    yield return propriete.Name;
                    foreach (var enfant in NomsDePropriete(propriete.Value))
                    {
                        yield return enfant;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var enfant in element.EnumerateArray().SelectMany(NomsDePropriete))
                {
                    yield return enfant;
                }

                break;
        }
    }

    // --- Utilitaires ----------------------------------------------------------------------------------------------

    private static async Task<Guid> CreerAsync(HttpClient client, string url, object corps)
    {
        var reponse = await client.PostAsJsonAsync(url, corps, Json, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, $"{url} : {await reponse.Content.ReadAsStringAsync(Ct)}");
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>(Json, Ct)).GetProperty("id").GetGuid();
    }

    [GeneratedRegex("[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MotsCamel();

    public static string Texte(JsonElement element, string propriete) =>
        element.GetProperty(propriete).ValueKind == JsonValueKind.Null ? string.Empty : element.GetProperty(propriete).ToString();

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
