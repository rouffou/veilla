using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;
using Sepp.Communications.Adapters;
using Sepp.Communications.Adapters.Api;
using Sepp.Communications.Adapters.Persistence;
using Sepp.Communications.Application;

// Racine de composition du service Communications (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("communications");
builder.Services.AddCommunicationsApplication();
builder.Services.AddCommunicationsAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Communications.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<CommunicationsDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

app.MapSeppServiceDefaults();
app.MapCommunicationsEndpoints();

await app.RunAsync();

public partial class Program;
