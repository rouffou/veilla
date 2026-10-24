using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;
using Sepp.Reintegration.Adapters;
using Sepp.Reintegration.Adapters.Api;
using Sepp.Reintegration.Adapters.Persistence;
using Sepp.Reintegration.Application;

// Racine de composition du service Réintégration, zone médicale (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("reintegration");
builder.Services.AddReintegrationApplication(
    builder.Configuration.GetSection("Reintegration").Get<OptionsReintegration>() ?? new OptionsReintegration());
builder.Services.AddReintegrationAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Reintegration.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<ReintegrationDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

// Échec au démarrage si les clés de chiffrement de la zone médicale sont absentes ou invalides (ARC-45).
_ = app.Services.GetRequiredService<ChiffrementZoneMedicale>();

app.MapSeppServiceDefaults();
app.MapReintegrationEndpoints();

await app.RunAsync();

public partial class Program;
