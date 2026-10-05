using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Web;
using Sepp.PostesRisques.Adapters;
using Sepp.PostesRisques.Adapters.Api;
using Sepp.PostesRisques.Adapters.Persistence;
using Sepp.PostesRisques.Application;

// Racine de composition du service Postes et risques (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("postes-risques");
builder.Services.AddPostesRisquesApplication();
builder.Services.AddPostesRisquesAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.PostesRisques.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapPostesRisquesEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PostesRisquesDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
