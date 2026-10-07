using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Web;
using Sepp.Obligations.Adapters;
using Sepp.Obligations.Adapters.Api;
using Sepp.Obligations.Adapters.Persistence;
using Sepp.Obligations.Application;

// Racine de composition du service Obligations (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("obligations");
builder.Services.AddObligationsApplication();
builder.Services.AddObligationsAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Obligations.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapObligationsEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ObligationsDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
