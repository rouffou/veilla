using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Web;
using Sepp.Documents.Adapters;
using Sepp.Documents.Adapters.Api;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Application;
using Sepp.Documents.Application.EvaluationSante;

// Racine de composition du service Documents (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("documents");
builder.Services.AddDocumentsApplication();
builder.Services.AddDocumentsAdapters(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Documents.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Échec au démarrage si la clé racine d'une zone servie est absente ou invalide, ou si une clé est partagée entre zones (ARC-44, ARC-45).
_ = app.Services.GetRequiredService<IChiffrementDocuments>();

app.MapSeppServiceDefaults();
app.MapDocumentsEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().Database.MigrateAsync();

    // DOC-01 : modèles de départ du formulaire d'évaluation de santé, en brouillon jusqu'à validation par le CPMT dirigeant.
    await scope.ServiceProvider.GetRequiredService<InitialiserModelesParDefaut>().ExecuteAsync(CancellationToken.None);
}

await app.RunAsync();

public partial class Program;
