using Microsoft.EntityFrameworkCore;
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
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Referentiels.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapReferentielsEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ReferentielsDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<InitialiserParametresLegaux>().ExecuteAsync(CancellationToken.None);
}

await app.RunAsync();

public partial class Program;
