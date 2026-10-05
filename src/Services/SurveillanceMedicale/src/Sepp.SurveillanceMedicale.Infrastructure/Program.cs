using Microsoft.EntityFrameworkCore;

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
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.SurveillanceMedicale.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Échec au démarrage si les clés de chiffrement de la zone médicale sont absentes ou invalides (ARC-45).
_ = app.Services.GetRequiredService<ChiffrementZoneMedicale>();

app.MapSeppServiceDefaults();
app.MapSurveillanceMedicaleEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<SurveillanceMedicaleDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<InitialiserProtocoles>().ExecuteAsync(CancellationToken.None);
}

await app.RunAsync();

public partial class Program;
