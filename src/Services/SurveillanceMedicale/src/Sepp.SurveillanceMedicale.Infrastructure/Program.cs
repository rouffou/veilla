using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Web;
using Sepp.SurveillanceMedicale.Adapters;
using Sepp.SurveillanceMedicale.Adapters.Api;
using Sepp.SurveillanceMedicale.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Application.Protocoles;

// Racine de composition du service Surveillance médicale, zone médicale (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("surveillance-medicale");
builder.Services.AddSurveillanceMedicaleApplication(
    builder.Configuration.GetSection("SurveillanceMedicale").Get<OptionsSurveillanceMedicale>() ?? new OptionsSurveillanceMedicale());
builder.Services.AddSurveillanceMedicaleAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddSeppPostgresHealthCheck(builder.Configuration, Sepp.SurveillanceMedicale.Adapters.DependencyInjection.ConnectionStringName, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Migrations (ADR 0010) : « migrer puis quitter » pour le job de migration Azure, migration au démarrage en local.
// Avant toute vérification de clé : le job de migration ne reçoit aucun secret.
if (await app.MigrerSchemaAsync<SurveillanceMedicaleDbContext>((services, annulation) =>
    services.GetRequiredService<InitialiserProtocoles>().ExecuteAsync(annulation)) is { } codeSortie)
{
    Environment.ExitCode = codeSortie;
    return;
}

// Échec au démarrage si les clés de chiffrement de la zone médicale sont absentes ou invalides (ARC-45).
_ = app.Services.GetRequiredService<ChiffrementZoneMedicale>();

app.MapSeppServiceDefaults();
app.MapSurveillanceMedicaleEndpoints();

await app.RunAsync();

public partial class Program;
