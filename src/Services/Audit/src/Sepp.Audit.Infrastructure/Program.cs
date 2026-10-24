using Sepp.Audit.Adapters;
using Sepp.Audit.Adapters.Api;
using Sepp.Audit.Adapters.Persistence;
using Sepp.Audit.Application;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;

// Racine de composition du service Audit (couche Infrastructure, §14.5) : journal infalsifiable des accès (NF-04).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("audit");
builder.Services.AddAuditApplication();
builder.Services.AddAuditAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Audit.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<AuditDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

app.MapSeppServiceDefaults();
app.MapAuditEndpoints();

await app.RunAsync();

public partial class Program;
