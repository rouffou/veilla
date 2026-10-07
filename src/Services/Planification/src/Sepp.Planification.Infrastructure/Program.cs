using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Web;
using Sepp.Planification.Adapters;
using Sepp.Planification.Adapters.Api;
using Sepp.Planification.Adapters.Persistence;
using Sepp.Planification.Application;

// Racine de composition du service Planification (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("planification");
builder.Services.AddPlanificationApplication();
builder.Services.AddPlanificationAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Planification.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapPlanificationEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PlanificationDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
