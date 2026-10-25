using System.Text.Json.Serialization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Sepp.Bff.Interne.Aval;
using Sepp.Bff.Interne.Ecrans;
using Sepp.Bff.Interne.Relais;
using Sepp.Bff.Interne.Securite;
using Sepp.BuildingBlocks.Web;

// BFF de l'application interne (ARC-43, ADR 0013) : relais vers une liste fermée de services et écrans composites,
// sans logique métier ni base de données.
var builder = WebApplication.CreateBuilder(args);

builder.AddSeppServiceDefaults("bff-interne");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<ErreursAvalHandler>();
builder.Services.AddHealthChecks().AddCheck("bff", () => HealthCheckResult.Healthy(), [ServiceDefaults.ReadyTag]);

// §3.1 : seul un profil interne (jeton de l'application interne, audience sepp-api, émetteur validé par
// AddSeppServiceDefaults) utilise ce BFF ; un jeton externe (employeur, SIPP, travailleur) ou technique reçoit 403.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PerimetreInterne.Politique, p => p.RequireAuthenticatedUser().RequireAssertion(c => PerimetreInterne.EstInterne(c.User)));

// L'application est servie depuis une autre origine que le BFF : origines exactes (Cors:OriginesAutorisees), aucun joker,
// pas de credentials (jeton porteur). Motif d'accès et bris de glace (zone médicale) en plus des en-têtes communs.
builder.AddSeppPortailCors(["GET", "POST", "PUT", "DELETE"], [RelaisServices.EnTeteMotifAcces, RelaisServices.EnTeteBrisDeGlace]);

// Adresses de la liste fermée des services : absentes ou invalides, le démarrage échoue.
var adresses = ServicesRelayes.Resoudre(builder.Configuration);
builder.Services.AddRelaisServices(adresses);
builder.Services.AddServicesAval(adresses);
builder.Services.AddScoped<Annuaire>();

var app = builder.Build();

app.UseCors();
app.MapSeppServiceDefaults();
app.MapEcransInterne();
app.MapRelaisServices();

await app.RunAsync();

public partial class Program;
