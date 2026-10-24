using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;
using Sepp.PostesRisques.Adapters;
using Sepp.PostesRisques.Adapters.Api;
using Sepp.PostesRisques.Adapters.Persistence;
using Sepp.PostesRisques.Application;

// Racine de composition du service Postes et risques (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("postes-risques");
builder.Services.AddPostesRisquesApplication();
builder.Services.AddPostesRisquesAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.PostesRisques.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<PostesRisquesDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

app.MapSeppServiceDefaults();
app.MapPostesRisquesEndpoints();

await app.RunAsync();

public partial class Program;
