using System.Security.Claims;

using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Scalar.AspNetCore;

using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.BuildingBlocks.Web;

/// <summary>
/// Gabarit commun des services (ARC-24) : observabilité (ARC-47), sondes de santé (CTR-12),
/// authentification JWT (ARC-40), autorisation par permissions (ARC-41), ProblemDetails, OpenAPI (ARC-30).
/// </summary>
public static class ServiceDefaults
{
    public const string ReadyTag = "ready";

    public static WebApplicationBuilder AddSeppServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o =>
        {
            o.IncludeScopes = true;
            o.UseUtcTimestamp = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
                .AddHttpClientInstrumentation()
                .AddSource("Azure.*")
                .AddSource(serviceName))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }
        else if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // ARC-30 : appels HTTP sortants avec délai d'expiration, reprises temporisées et disjoncteur.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ConflictExceptionHandler>();
        builder.Services.AddOpenApi();
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

        AddSecurity(builder);
        return builder;
    }

    public static WebApplication MapSeppServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) }).AllowAnonymous();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) }).AllowAnonymous();

        app.MapOpenApi().AllowAnonymous();
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference().AllowAnonymous();
        }

        return app;
    }

    /// <summary>Exige une permission de la matrice des droits (§3.3).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicy.Name(permission));

    private static void AddSecurity(WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Authentication");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = section["Authority"];
                o.Audience = section["Audience"];
                // En conteneur, les métadonnées peuvent être lues par une adresse interne différente de l'émetteur public.
                if (!string.IsNullOrWhiteSpace(section["MetadataAddress"]))
                {
                    o.MetadataAddress = section["MetadataAddress"]!;
                }

                if (!string.IsNullOrWhiteSpace(section["ValidIssuer"]))
                {
                    o.TokenValidationParameters.ValidIssuer = section["ValidIssuer"];
                }

                o.RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true);
                o.MapInboundClaims = false;
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.TokenValidationParameters.NameClaimType = "sub";
                o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });

        builder.Services.AddAuthorization(o =>
        {
            o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            foreach (var permission in typeof(Permissions).GetFields().Select(f => (string)f.GetRawConstantValue()!))
            {
                o.AddPolicy(PermissionPolicy.Name(permission), p => p
                    .RequireAuthenticatedUser()
                    .RequireAssertion(ctx => HttpCurrentUser.PermissionsOf(ctx.User).Contains(permission)));
            }
        });
    }
}

public static class PermissionPolicy
{
    public static string Name(string permission) => $"perm:{permission}";
}

/// <summary>Utilisateur courant issu du jeton ; « system » hors requête HTTP (traitements d'arrière-plan).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string UserId => IsAuthenticated ? Principal!.FindFirstValue("sub") ?? "inconnu" : "system";

    public IReadOnlySet<string> Roles =>
        Principal is null ? new HashSet<string>() : RolesOf(Principal);

    public bool HasPermission(string permission) =>
        Principal is not null && PermissionsOf(Principal).Contains(permission);

    internal static IReadOnlySet<string> RolesOf(ClaimsPrincipal principal) =>
        principal.FindAll("roles").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    internal static IReadOnlySet<string> PermissionsOf(ClaimsPrincipal principal) =>
        RolePermissions.For(RolesOf(principal));
}

/// <summary>Propage l'identifiant de corrélation de bout en bout (ARC-47).</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100)
        {
            correlationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString();
        }

        context.TraceIdentifier = correlationId;
        context.Response.Headers[Header] = correlationId;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
