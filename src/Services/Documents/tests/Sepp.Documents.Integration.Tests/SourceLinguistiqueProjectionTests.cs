using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.Affilies;
using Sepp.Contracts.Decisions;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Langues;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// NF-41, ADR 0009 (lot 7, #366) : l'instance médicale, configurée avec <c>Documents:SourceLinguistique=Projection</c>, choisit
/// la langue des documents à partir de la projection <c>contexte_linguistique</c> alimentée par les événements d'Affiliés et
/// de Personnes, sans appeler ces services. Sans information projetée, la règle se rabat sur le français.
/// </summary>
public sealed class SourceLinguistiqueProjectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "sepp-documents-langue-tests", Guid.NewGuid().ToString("N"));
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private WebApplicationFactory<Program> _medicale = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _medicale = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Documents", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Documents:Adaptateurs:Horodatage", "Simulateur");
            b.UseSetting("Documents:Adaptateurs:Signature", "Simulateur");
            // Adaptateur réel, alimenté par la projection : aucune adresse d'Affiliés ni de Personnes n'est configurée.
            b.UseSetting("Documents:Adaptateurs:SourceLinguistique", "Reel");
            b.UseSetting("Documents:SourceLinguistique", "Projection");
            b.UseSetting("Documents:Stockage:Type", "Local");
            b.UseSetting("Documents:Stockage:Racine", _racine);
            b.UseSetting("Documents:Enveloppe:Type", "Local");
            b.UseSetting("Documents:Zones:0", "medicale");
            b.UseSetting("Sepp:Zone", "medicale");
            b.UseSetting("Documents:Enveloppe:CleKek:medicale:Courante", "v1");
            b.UseSetting("Documents:Enveloppe:CleKek:medicale:Versions:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        });
        _ = _medicale.Server;

        await using var scope = _medicale.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        foreach (var modele in (await db.Modeles.ToListAsync(_ct)).Where(m => m.Code.StartsWith("SANTE.EVALUATION", StringComparison.Ordinal)))
        {
            modele.Valider("cpmt-dirigeant", DateTimeOffset.UtcNow);
            modele.Publier("admin", DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(_ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _medicale.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_racine))
        {
            foreach (var fichier in Directory.EnumerateFiles(_racine, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(fichier, FileAttributes.Normal);
            }

            Directory.Delete(_racine, recursive: true);
        }
    }

    private async Task<int> RecevoirAsync<TEvent>(TEvent evenement)
        where TEvent : IntegrationEvent =>
        await _medicale.Services.GetRequiredService<IntegrationEventDispatcher<DocumentsDbContext>>()
            .DispatchAsync(Guid.CreateVersion7(), EventContractAttribute.Of(typeof(TEvent)).FullName, JsonSerializer.Serialize(evenement, EventSerialization.Options), _ct);

    private async Task<List<(string Exemplaire, Language Langue, string Motif)>> ExemplairesAsync(Guid decisionId)
    {
        await using var scope = _medicale.Services.CreateAsyncScope();
        var documents = await scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().Documents.Where(d => d.ObjetId == decisionId).ToListAsync(_ct);
        return [.. documents.Select(d => (d.Exemplaire!, d.Langue, d.MotifLangue)).OrderBy(e => e.Item1, StringComparer.Ordinal)];
    }

    [Fact]
    public void La_source_linguistique_est_la_projection_et_non_les_appels_http()
    {
        using var scope = _medicale.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISourceLinguistique>().GetType().Name.ShouldBe("SourceLinguistiqueParProjection");
    }

    [Fact]
    public async Task Les_documents_suivent_la_langue_projetee_depuis_les_evenements_d_affilies_et_de_personnes()
    {
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Affilié bruxellois de langue française ; le travailleur a choisi le néerlandais après son entrée en service.
        (await RecevoirAsync(new AffilieCree(affilie, "0202239951", "B", "BruxellesCapitale", "Fr") { OccurredAt = t0 })).ShouldBe(1);
        (await RecevoirAsync(new OccupationDebutee(Guid.CreateVersion7(), personne, affilie, new DateOnly(2026, 1, 1), "Fr") { OccurredAt = t0 })).ShouldBe(1);
        (await RecevoirAsync(new LangueModifiee(personne, "Nl") { OccurredAt = t0.AddMinutes(1) })).ShouldBe(1);

        await using (var scope = _medicale.Services.CreateAsyncScope())
        {
            var projection = await scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().ContextesLinguistiques.ToListAsync(_ct);
            projection.Count.ShouldBe(2);
            projection.Single(c => c.Sujet == SujetLinguistique.Affilie).Regime.ShouldBe(RegimeLinguistique.BruxellesCapitale);
            projection.Single(c => c.Sujet == SujetLinguistique.Personne).Langue.ShouldBe(Language.Nl);
        }

        var decision = new DecisionEmise(Guid.CreateVersion7(), personne, affilie, CategoriesDecision.Apte, [], new DateOnly(2027, 10, 24));
        (await RecevoirAsync(decision)).ShouldBe(1);

        (await ExemplairesAsync(decision.DecisionId)).ShouldBe(
        [
            ("dossier", Language.Nl, "Bruxelles-Capitale : langue choisie par le travailleur"),
            ("travailleur", Language.Nl, "Bruxelles-Capitale : langue choisie par le travailleur"),
        ]);
    }

    [Fact]
    public async Task Sans_information_projetee_la_langue_par_defaut_est_le_francais()
    {
        var decision = new DecisionEmise(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), CategoriesDecision.Apte, [], null);

        (await RecevoirAsync(decision)).ShouldBe(1);

        (await ExemplairesAsync(decision.DecisionId)).ShouldAllBe(e => e.Langue == Language.Fr && e.Motif == "régime inconnu : français par défaut");
    }
}
