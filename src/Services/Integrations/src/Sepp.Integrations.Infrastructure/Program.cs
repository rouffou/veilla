using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.BuildingBlocks.Web;
using Sepp.Integrations.Adapters;
using Sepp.Integrations.Adapters.Api;
using Sepp.Integrations.Adapters.Persistence;
using Sepp.Integrations.Application;

// Racine de composition du service Intégrations (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("integrations");
builder.Services.AddIntegrationsApplication();
builder.Services.AddIntegrationsAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.Integrations.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

// Hôte libéré à la sortie, y compris par les modes « migrer puis quitter » et « rechiffrer puis quitter » : les derniers
// journaux sont transmis avant l'arrêt du processus (#482).
await using var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<IntegrationsDbContext>() is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

// Échec au démarrage si les clés de chiffrement des charges utiles sont absentes ou invalides (ARC-45).
_ = app.Services.GetRequiredService<FieldEncryptor>();
_ = app.Services.GetRequiredService<IFieldKeyProvider>();

// ADR 0012, #396, #482 : « rechiffrer puis quitter » ou « vérifier seulement » (jobs d'exploitation) : toutes les valeurs
// chiffrées passent à la DEK courante, ou sont seulement contrôlées (code 0 ou 1).
if (await app.RechiffrerChampsAsync<IntegrationsDbContext>(s => s.GetRequiredService<FieldEncryptor>()) is { } codeRechiffrement)
{
    Environment.ExitCode = codeRechiffrement;
    return;
}

app.MapSeppServiceDefaults();
app.MapIntegrationsEndpoints();

await app.RunAsync();

public partial class Program;
