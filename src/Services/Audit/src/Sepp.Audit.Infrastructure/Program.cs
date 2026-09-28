using Microsoft.EntityFrameworkCore;

using Sepp.Audit.Adapters;
using Sepp.Audit.Adapters.Api;
using Sepp.Audit.Adapters.Persistence;
using Sepp.Audit.Application;
using Sepp.BuildingBlocks.Web;

// Racine de composition du service Audit (couche Infrastructure, §14.5) : journal infalsifiable des accès (NF-04).
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("audit");
builder.Services.AddAuditApplication();
builder.Services.AddAuditAdapters(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(Sepp.Audit.Adapters.DependencyInjection.ConnectionStringName)!, name: "postgres", tags: [ServiceDefaults.ReadyTag]);

var app = builder.Build();

app.MapSeppServiceDefaults();
app.MapAuditEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
