using System.Text.Json.Serialization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Sepp.Bff.Travailleur.Aval;
using Sepp.Bff.Travailleur.Ecrans;
using Sepp.Bff.Travailleur.Securite;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;

// BFF du portail travailleur (ARC-43) : un BFF par canal, sans logique métier ni base de données.
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("bff-travailleur");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<ErreursAvalHandler>();
builder.Services.AddHealthChecks().AddCheck("bff", () => HealthCheckResult.Healthy(), [ServiceDefaults.ReadyTag]);

// POR-10 : seul un travailleur authentifié (jeton du portail, audience sepp-api) utilise ce BFF ; un profil interne ou
// employeur reçoit 403. Le périmètre (claim personne_id) est vérifié route par route par PerimetreTravailleur.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PerimetreTravailleur.Politique, p => p.RequireAuthenticatedUser().RequireClaim("roles", Roles.Travailleur));

// Le portail est servi depuis une autre origine que le BFF : origines autorisées explicites (aucun joker).
var origines = builder.Configuration.GetSection("Cors:Origines").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origines)
    .WithMethods("GET", "POST")
    .WithHeaders("Authorization", "Content-Type", "Accept-Language", CorrelationIdMiddleware.Header)
    .WithExposedHeaders("Content-Disposition", CorrelationIdMiddleware.Header)));

builder.Services.AddServicesAval(builder.Configuration);
builder.Services.AddScoped<EcransRendezVous>();
builder.Services.AddScoped<EcransQuestionnaires>();
builder.Services.AddScoped<EcransDocuments>();
builder.Services.AddScoped<EcransAccueil>();

var app = builder.Build();

app.UseCors();
app.MapSeppServiceDefaults();
app.MapEcransTravailleur();

await app.RunAsync();

public partial class Program;
