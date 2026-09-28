using Microsoft.EntityFrameworkCore;

using Sepp.Affilies.Adapters;
using Sepp.Affilies.Adapters.Api;
using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Application;
using Sepp.BuildingBlocks.Web;

// Racine de composition du service Affiliés (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("affilies");
builder.Services.AddAffiliesApplication();
builder.Services.AddAffiliesAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Affilies.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapAffiliesEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AffiliesDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
