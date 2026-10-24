using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.Decisions;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Application;
using Sepp.Documents.Application.DossierSante;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Modeles;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// Décision #356 : une instance « médicale seule » et une instance « standard seule » (une base chacune, comme en
/// exploitation) traitent le même <c>DecisionEmise</c> et produisent ensemble les trois exemplaires, sans doublon.
/// </summary>
public sealed class InstancesParZoneTests : IAsyncLifetime
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Personne = Guid.CreateVersion7();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "sepp-documents-zones-tests", Guid.NewGuid().ToString("N"));
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private WebApplicationFactory<Program> _standard = null!;
    private WebApplicationFactory<Program> _medicale = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _standard = Instance("standard");
        _medicale = Instance("medicale");
        _ = _standard.Server;
        _ = _medicale.Server;
        await PublierModelesAsync(_standard);
        await PublierModelesAsync(_medicale);
    }

    public async ValueTask DisposeAsync()
    {
        await _standard.DisposeAsync();
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

    private WebApplicationFactory<Program> Instance(string zone)
    {
        var connexion = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = $"documents_{zone}" }.ConnectionString;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Documents", connexion);
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Documents:Adaptateurs:Horodatage", "Simulateur");
            b.UseSetting("Documents:Adaptateurs:Signature", "Simulateur");
            b.UseSetting("Documents:Adaptateurs:SourceLinguistique", "Simulateur");
            b.UseSetting("Documents:Stockage:Type", "Local");
            b.UseSetting("Documents:Stockage:Racine", _racine);
            b.UseSetting("Documents:Zones:0", zone);
            b.UseSetting("Sepp:Zone", zone);
            foreach (var z in new[] { "standard", "medicale", "psychosociale" })
            {
                b.UseSetting($"Documents:Chiffrement:{z}:Encryption:CurrentKeyId", $"test-{z}");
                b.UseSetting($"Documents:Chiffrement:{z}:Encryption:Keys:test-{z}", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            }
        });
    }

    /// <summary>Valide et publie les modèles de départ amorcés par l'instance, comme le département médical en exploitation.</summary>
    private async Task PublierModelesAsync(WebApplicationFactory<Program> instance)
    {
        await using var scope = instance.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var modeles = await db.Modeles.ToListAsync(_ct);
        foreach (var modele in modeles.Where(m => m.Code.StartsWith("SANTE.EVALUATION", StringComparison.Ordinal)))
        {
            modele.Valider("cpmt-dirigeant", DateTimeOffset.UtcNow);
            modele.Publier("admin", DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(_ct);
    }

    private async Task<List<(string Exemplaire, ZoneDocument Zone)>> ExemplairesAsync(WebApplicationFactory<Program> instance, Guid decisionId)
    {
        await using var scope = instance.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var documents = await db.Documents.Where(d => d.ObjetId == decisionId).ToListAsync(_ct);
        return [.. documents.Select(d => (d.Exemplaire!, d.Zone))];
    }

    private async Task<int> DispatcherAsync(WebApplicationFactory<Program> instance, Guid message, string payload) =>
        await instance.Services.GetRequiredService<IntegrationEventDispatcher<DocumentsDbContext>>()
            .DispatchAsync(message, EventContractAttribute.Of(typeof(DecisionEmise)).FullName, payload, _ct);

    [Fact]
    public void Les_consommateurs_medicaux_ne_sont_enregistres_que_par_l_instance_medicale()
    {
        _standard.Services.GetService<CopieDossierValideeHandler>().ShouldBeNull();
        _standard.Services.GetService<VaccinationAdministreeHandler>().ShouldBeNull();
        _medicale.Services.GetService<CopieDossierValideeHandler>().ShouldNotBeNull();
        _medicale.Services.GetService<VaccinationAdministreeHandler>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Les_amorcages_ne_creent_que_les_modeles_de_la_zone_servie()
    {
        await using var standard = _standard.Services.CreateAsyncScope();
        await using var medicale = _medicale.Services.CreateAsyncScope();

        (await standard.ServiceProvider.GetRequiredService<DocumentsDbContext>().Modeles.Select(m => m.Zone).Distinct().ToListAsync(_ct))
            .ShouldBe([ZoneDocument.Standard]);
        (await medicale.ServiceProvider.GetRequiredService<DocumentsDbContext>().Modeles.Select(m => m.Zone).Distinct().ToListAsync(_ct))
            .ShouldBe([ZoneDocument.Medicale]);
    }

    [Fact]
    public async Task Deux_instances_de_zones_differentes_traitent_la_meme_decision_sans_doublon()
    {
        var decision = new DecisionEmise(Guid.CreateVersion7(), Personne, Affilie, CategoriesDecision.ApteAvecMesures, ["AMENAGEMENT_POSTE"], new DateOnly(2027, 10, 5));
        var message = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(decision, EventSerialization.Options);

        // Le bus remet le même message aux deux instances (une subscription chacune), puis le rejoue.
        (await DispatcherAsync(_standard, message, payload)).ShouldBe(1);
        (await DispatcherAsync(_medicale, message, payload)).ShouldBe(1);
        (await DispatcherAsync(_standard, message, payload)).ShouldBe(0);
        (await DispatcherAsync(_medicale, message, payload)).ShouldBe(0);
        await DispatcherAsync(_standard, Guid.CreateVersion7(), payload);
        await DispatcherAsync(_medicale, Guid.CreateVersion7(), payload);

        var standard = await ExemplairesAsync(_standard, decision.DecisionId);
        var medicale = await ExemplairesAsync(_medicale, decision.DecisionId);

        standard.ShouldBe([("employeur", ZoneDocument.Standard)]);
        medicale.OrderBy(e => e.Exemplaire, StringComparer.Ordinal).ShouldBe([("dossier", ZoneDocument.Medicale), ("travailleur", ZoneDocument.Medicale)]);
        standard.Concat(medicale).Select(e => e.Exemplaire).Order().ShouldBe(["dossier", "employeur", "travailleur"]);
    }
}
