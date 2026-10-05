using System.Text.Json.Serialization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Sepp.Bff.Employeur.Aval;
using Sepp.Bff.Employeur.Ecrans;
using Sepp.Bff.Employeur.Securite;
using Sepp.BuildingBlocks.Web;

// BFF du portail employeur (ARC-43) : un BFF par canal, sans logique métier ni base de données.
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("bff-employeur");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<ErreursAvalHandler>();
builder.Services.AddHealthChecks().AddCheck("bff", () => HealthCheckResult.Healthy(), [ServiceDefaults.ReadyTag]);

// POR-01 : seul un employeur ou un SIPP (jeton du portail, audience sepp-api) utilise ce BFF.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PerimetreEmployeur.Politique, p => p.RequireAuthenticatedUser().RequireClaim("roles", PerimetreEmployeur.RolesAutorises));

// Le portail est servi depuis une autre origine que le BFF : origines autorisées explicites (aucun joker).
var origines = builder.Configuration.GetSection("Cors:Origines").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origines)
    .WithMethods("GET", "POST")
    .WithHeaders("Authorization", "Content-Type", "Accept-Language", CorrelationIdMiddleware.Header)
    .WithExposedHeaders("Content-Disposition", CorrelationIdMiddleware.Header)));

builder.Services.AddServicesAval(builder.Configuration);
builder.Services.AddScoped<EcransEmployeur>();

var app = builder.Build();

app.UseCors();
app.MapSeppServiceDefaults();
app.MapEcransEmployeur();

await app.RunAsync();

public partial class Program;
