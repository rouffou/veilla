using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Personnes.Adapters;
using Sepp.Personnes.Adapters.Api;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Application;

// Racine de composition du service Personnes et occupations (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("personnes");
builder.Services.AddPersonnesApplication();
builder.Services.AddPersonnesAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Personnes.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<PersonnesDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

// Échec au démarrage si les clés de chiffrement ou d'index aveugle sont absentes ou invalides (ARC-45, DAT-06).
_ = app.Services.GetRequiredService<FieldEncryptor>();
_ = app.Services.GetRequiredService<IFieldKeyProvider>();
_ = app.Services.GetRequiredService<BlindIndex>();

app.MapSeppServiceDefaults();
app.MapPersonnesEndpoints();
app.MapSeppRepublications();

await app.RunAsync();

public partial class Program;
