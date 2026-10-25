extern alias AffiliesHote;
extern alias DocumentsHote;
extern alias PersonnesHote;

using System.Security.Cryptography;

using Npgsql;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Personnes.Adapters.Persistence;

using Testcontainers.PostgreSql;

using AffiliesProgram = AffiliesHote::Program;
using DocumentsProgram = DocumentsHote::Program;
using PersonnesProgram = PersonnesHote::Program;

namespace Sepp.Sagas.Tests.Plateforme;

/// <summary>
/// Plateforme du rattrapage des projections (ADR 0011) : producteurs Personnes et Affiliés, consommateurs Documents (projection
/// linguistique, NF-41) et Affiliés (occupations projetées, POR-11), reliés par le <see cref="BusDeTest"/> routé comme
/// Terraform. Horloge système : les horodatages des états (<c>updated_at</c>) et des événements v1 (<c>OccurredAt</c>) viennent
/// de la même horloge, comme en production, ce qui permet de vérifier qu'un instantané n'écrase pas un état plus récent.
/// </summary>
public sealed class PlateformeRattrapage : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _racineDocuments = Path.Combine(Path.GetTempPath(), "sepp-rattrapage-tests", Guid.NewGuid().ToString("N"));
    private readonly List<HoteService> _hotes = [];

    public BusDeTest Bus { get; } = new();

    public HoteService Personnes { get; private set; } = null!;

    public HoteService Affilies { get; private set; } = null!;

    public HoteService Documents { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        foreach (var nom in new[] { "personnes", "affilies", "documents" })
        {
            await using var connexion = new NpgsqlConnection(_postgres.GetConnectionString());
            await connexion.OpenAsync();
            await using var commande = new NpgsqlCommand($"CREATE DATABASE {nom}", connexion);
            await commande.ExecuteNonQueryAsync();
        }

        // Personnes ne consomme rien : il n'est pas abonné au bus de test.
        // ADR 0012 : DEK et clé de l'index aveugle enveloppées par une clé racine locale de test.
        var personnes = ChiffrementChamps.ConfigurationLocale("Encryption", "test",
            new Dictionary<string, byte[]> { ["BlindIndex:CleEnveloppee"] = RandomNumberGenerator.GetBytes(32) });
        personnes["Republication:TailleLot"] = "2";
        Personnes = HoteService.Demarrer<PersonnesProgram, PersonnesDbContext>("personnes", "Personnes", Chaine("personnes"), personnes, TimeProvider.System, Bus);
        _hotes.Add(Personnes);

        Affilies = Demarrer<AffiliesProgram, AffiliesDbContext>("affilies", "Affilies", new Dictionary<string, string> { ["Republication:TailleLot"] = "2" });

        var documents = new Dictionary<string, string>
        {
            ["Documents:Adaptateurs:Horodatage"] = "Simulateur",
            ["Documents:Adaptateurs:Signature"] = "Simulateur",
            ["Documents:Adaptateurs:SourceLinguistique"] = "Simulateur",
            ["Documents:Zones:0"] = "standard",
            ["Documents:Zones:1"] = "medicale",
            ["Documents:Zones:2"] = "psychosociale",
            ["Documents:Stockage:Type"] = "Local",
            ["Documents:Stockage:Racine"] = _racineDocuments,
            ["Documents:Enveloppe:Type"] = "Local",
        };
        foreach (var zone in new[] { "standard", "medicale", "psychosociale" })
        {
            documents[$"Documents:Enveloppe:CleKek:{zone}:Courante"] = "v1";
            documents[$"Documents:Enveloppe:CleKek:{zone}:Versions:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        Documents = Demarrer<DocumentsProgram, DocumentsDbContext>("documents", "Documents", documents);
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
            Directory.Delete(_racineDocuments, recursive: true);
        }
    }

    /// <summary>
    /// Pompe déterministe : lots de republication, publication des outbox, livraison aux abonnés, jusqu'à stabilité.
    /// </summary>
    public async Task PomperAsync(CancellationToken cancellationToken)
    {
        for (var tour = 0; tour < 500; tour++)
        {
            var activite = 0;
            foreach (var hote in _hotes)
            {
                while (await hote.TraiterRepublication(cancellationToken))
                {
                    activite++;
                }

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

        throw new InvalidOperationException("La pompe ne se stabilise pas après 500 tours.");
    }

    /// <summary>Publie les outbox sans livrer : les messages en file sont ensuite abandonnés (consommateur pas encore abonné).</summary>
    public async Task PublierSansLivrerAsync(CancellationToken cancellationToken)
    {
        foreach (var hote in _hotes)
        {
            while (await hote.TraiterOutbox(cancellationToken) > 0)
            {
            }
        }

        Bus.Reinitialiser(20261025);
    }

    private string Chaine(string base_) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = base_ }.ConnectionString;

    private HoteService Demarrer<TProgram, TContext>(string nom, string nomChaine, Dictionary<string, string> reglages)
        where TProgram : class
        where TContext : Sepp.BuildingBlocks.Infrastructure.Persistence.SeppDbContext
    {
        var hote = HoteService.Demarrer<TProgram, TContext>(nom, nomChaine, Chaine(nom), reglages, TimeProvider.System, Bus);
        Bus.Abonner(nom, hote.Distribuer);
        _hotes.Add(hote);
        return hote;
    }
}
