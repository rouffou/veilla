extern alias CommunicationsHote;
extern alias DocumentsHote;
extern alias ObligationsHote;
extern alias PlanificationHote;
extern alias SurveillanceMedicaleHote;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Communications.Adapters.Persistence;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Obligations.Adapters.Persistence;
using Sepp.Planification.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Adapters.Persistence;

using Shouldly;

using Testcontainers.PostgreSql;

using CommunicationsProgram = CommunicationsHote::Program;
using DocumentsProgram = DocumentsHote::Program;
using ObligationsProgram = ObligationsHote::Program;
using PlanificationProgram = PlanificationHote::Program;
using SurveillanceMedicaleProgram = SurveillanceMedicaleHote::Program;

namespace Sepp.Sagas.Tests.Plateforme;

/// <summary>
/// Plateforme de test de la saga « examen de reprise » (ARC-33) : un seul PostgreSQL héberge les cinq bases ; les hôtes
/// Obligations, Planification, Surveillance médicale, Communications et Documents tournent dans le même processus et
/// ne communiquent que par le <see cref="BusDeTest"/>, routé comme Terraform. Aucun service d'arrière-plan ne tourne :
/// la pompe du test publie l'outbox puis livre les messages jusqu'à stabilité (déterministe).
/// </summary>
public sealed class PlateformeSaga : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _racineDocuments = Path.Combine(Path.GetTempPath(), "sepp-sagas-tests", Guid.NewGuid().ToString("N"));
    private readonly List<HoteService> _hotes = [];

    public BusDeTest Bus { get; } = new();

    public HorlogeSaga Horloge { get; } = new();

    public HoteService Obligations { get; private set; } = null!;

    public HoteService Planification { get; private set; } = null!;

    public HoteService SurveillanceMedicale { get; private set; } = null!;

    public HoteService Communications { get; private set; } = null!;

    public HoteService Documents { get; private set; } = null!;

    public string ChaineConnexion(string base_) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = base_ }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        foreach (var nom in new[] { "obligations", "planification", "surveillance_medicale", "communications", "documents" })
        {
            await using var connexion = new NpgsqlConnection(_postgres.GetConnectionString());
            await connexion.OpenAsync();
            await using var commande = new NpgsqlCommand($"CREATE DATABASE {nom}", connexion);
            await commande.ExecuteNonQueryAsync();
        }

        // Les hôtes démarrent l'un après l'autre : le résolveur de point d'entrée de WebApplicationFactory observe un
        // écouteur de diagnostic global, deux démarrages simultanés pourraient se confondre.
        Obligations = Demarrer<ObligationsProgram, ObligationsDbContext>("obligations", "Obligations", "obligations", new Dictionary<string, string>
        {
            ["Obligations:Traitement:Actif"] = "false",
            ["Obligations:Reprise:Actif"] = "false",
        });
        Planification = Demarrer<PlanificationProgram, PlanificationDbContext>("planification", "Planification", "planification", new Dictionary<string, string>
        {
            ["Planification:Adaptateurs:OutilRh"] = "Simulateur",
            ["Planification:Adaptateurs:AgendaExterne"] = "Simulateur",
            ["Planification:Taches:Actif"] = "false",
        });
        SurveillanceMedicale = Demarrer<SurveillanceMedicaleProgram, SurveillanceMedicaleDbContext>("surveillance-medicale", "SurveillanceMedicale", "surveillance_medicale", new Dictionary<string, string>
        {
            ["ZoneMedicale:Encryption:CurrentKeyId"] = "test-medical",
            ["ZoneMedicale:Encryption:Keys:test-medical"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        });

        var communications = new Dictionary<string, string>
        {
            ["Communications:Expedition:Actif"] = "false",
            ["Communications:Liens:PortailTravailleur"] = "https://travailleur.exemple.test/messages",
        };
        foreach (var canal in new[] { "Annuaire", "Portail", "Email", "Sms", "Courrier", "RecommandeElectronique", "EBoxEntreprise", "EBoxCitoyen" })
        {
            communications[$"Communications:Adaptateurs:{canal}"] = "Simulateur";
        }

        Communications = Demarrer<CommunicationsProgram, CommunicationsDbContext>("communications", "Communications", "communications", communications);

        var documents = new Dictionary<string, string>
        {
            ["Documents:Adaptateurs:Horodatage"] = "Simulateur",
            ["Documents:Adaptateurs:Signature"] = "Simulateur",
            ["Documents:Adaptateurs:SourceLinguistique"] = "Simulateur",
            ["Documents:Stockage:Type"] = "Local",
            ["Documents:Stockage:Racine"] = _racineDocuments,
        };
        foreach (var zone in new[] { "standard", "medicale", "psychosociale" })
        {
            documents[$"Documents:Chiffrement:{zone}:Encryption:CurrentKeyId"] = $"test-{zone}";
            documents[$"Documents:Chiffrement:{zone}:Encryption:Keys:test-{zone}"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        Documents = Demarrer<DocumentsProgram, DocumentsDbContext>("documents", "Documents", "documents", documents);

        // Préparation : les modèles de départ de Documents sont en brouillon ; sans modèle publié, DecisionEmise échoue.
        await PublierModelesDocumentsAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hote in Enumerable.Reverse(_hotes))
        {
            await hote.DisposeAsync();
        }

        await _postgres.DisposeAsync();
        if (Directory.Exists(_racineDocuments))
        {
            foreach (var fichier in Directory.EnumerateFiles(_racineDocuments, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(fichier, FileAttributes.Normal);
            }

            Directory.Delete(_racineDocuments, recursive: true);
        }
    }

    private HoteService Demarrer<TProgram, TContext>(string nom, string nomChaine, string baseDeDonnees, Dictionary<string, string> reglages)
        where TProgram : class
        where TContext : Sepp.BuildingBlocks.Infrastructure.Persistence.SeppDbContext
    {
        var hote = HoteService.Demarrer<TProgram, TContext>(nom, nomChaine, ChaineConnexion(baseDeDonnees), reglages, Horloge, Bus);
        Bus.Abonner(nom, hote.Distribuer);
        _hotes.Add(hote);
        return hote;
    }

    /// <summary>
    /// Pompe déterministe : publie les outbox de tous les hôtes (<c>ProcessBatchAsync</c>) puis livre les messages aux abonnés,
    /// jusqu'à ce qu'il n'y ait plus rien à publier ni à livrer.
    /// </summary>
    public async Task PomperAsync(CancellationToken cancellationToken)
    {
        for (var tour = 0; tour < 200; tour++)
        {
            var activite = 0;
            foreach (var hote in _hotes)
            {
                int lot;
                while ((lot = await hote.TraiterOutbox(cancellationToken)) > 0)
                {
                    activite += lot;
                }
            }

            activite += await Bus.LivrerAsync(cancellationToken);
            if (activite == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("La pompe ne se stabilise pas après 200 tours : boucle d'événements ?");
    }

    /// <summary>Déclenche l'expédition des messages échus de Communications (traitement périodique désactivé en test).</summary>
    public async Task ExpedierMessagesAsync(CancellationToken cancellationToken)
    {
        var reponse = await Communications.Client(Roles.GestionnaireDossiers).PostAsync("/api/v1/messages/expedition", null, cancellationToken);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task PublierModelesDocumentsAsync(CancellationToken cancellationToken)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var modeles = await Documents.Client(Roles.AdministrateurFonctionnel).GetFromJsonAsync<JsonElement>("/api/v1/modeles", json, cancellationToken);
        var publies = 0;
        foreach (var modele in modeles.EnumerateArray().Where(m => m.GetProperty("code").GetString()!.StartsWith("SANTE.EVALUATION", StringComparison.Ordinal)))
        {
            var id = modele.GetProperty("id").GetGuid();
            var validateur = modele.GetProperty("zone").GetString() == "medicale" ? Roles.CpmtDirigeant : Roles.AdministrateurFonctionnel;
            (await Documents.Client(validateur).PostAsync($"/api/v1/modeles/{id}/validation", null, cancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Documents.Client(Roles.AdministrateurFonctionnel).PostAsync($"/api/v1/modeles/{id}/publication", null, cancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            publies++;
        }

        publies.ShouldBeGreaterThanOrEqualTo(9, "neuf modèles de départ (3 exemplaires × FR, NL, DE) attendus.");
    }
}
