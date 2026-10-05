using Microsoft.EntityFrameworkCore;

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
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Integrations.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

// Échec au démarrage si les clés de chiffrement des charges utiles sont absentes ou invalides (ARC-45).
_ = app.Services.GetRequiredService<FieldEncryptor>();
_ = app.Services.GetRequiredService<IFieldKeyProvider>();

app.MapSeppServiceDefaults();
app.MapIntegrationsEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
