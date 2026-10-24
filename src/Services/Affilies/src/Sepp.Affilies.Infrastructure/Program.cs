using Sepp.Affilies.Adapters;
using Sepp.Affilies.Adapters.Api;
using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Application;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;

// Racine de composition du service Affiliés (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("affilies");
builder.Services.AddAffiliesApplication();
builder.Services.AddAffiliesAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Affilies.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<AffiliesDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

app.MapSeppServiceDefaults();
app.MapAffiliesEndpoints();

await app.RunAsync();

public partial class Program;
