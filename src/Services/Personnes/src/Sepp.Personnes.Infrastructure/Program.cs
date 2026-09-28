using Microsoft.EntityFrameworkCore;

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
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Personnes.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Échec au démarrage si les clés de chiffrement ou d'index aveugle sont absentes ou invalides (ARC-45, DAT-06).
_ = app.Services.GetRequiredService<FieldEncryptor>();
_ = app.Services.GetRequiredService<IFieldKeyProvider>();
_ = app.Services.GetRequiredService<BlindIndex>();

app.MapSeppServiceDefaults();
app.MapPersonnesEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PersonnesDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
