using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Web;
using Sepp.Communications.Adapters;
using Sepp.Communications.Adapters.Api;
using Sepp.Communications.Adapters.Persistence;
using Sepp.Communications.Application;

// Racine de composition du service Communications (couche Infrastructure, §14.5).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("communications");
builder.Services.AddCommunicationsApplication();
builder.Services.AddCommunicationsAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Communications.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapCommunicationsEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
