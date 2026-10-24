using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;
using Sepp.Referentiels.Adapters;
using Sepp.Referentiels.Adapters.Api;
using Sepp.Referentiels.Adapters.Persistence;
using Sepp.Referentiels.Application;
using Sepp.Referentiels.Application.Parametres;

// Racine de composition du service Référentiels (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("referentiels");
builder.Services.AddReferentielsApplication();
builder.Services.AddReferentielsAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Referentiels.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<ReferentielsDbContext>((services, annulation) =>
    services.GetRequiredService<InitialiserParametresLegaux>().ExecuteAsync(annulation)) is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

app.MapSeppServiceDefaults();
app.MapReferentielsEndpoints();

await app.RunAsync();

public partial class Program;
