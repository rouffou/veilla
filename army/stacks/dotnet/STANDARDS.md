# Standards .NET (C#)

> Lu par `software-architect-dotnet`, `developer-dotnet` et `code-reviewer` sur du code .NET. En cas de conflit avec le code existant du dépôt, **les conventions existantes du dépôt priment** : signaler l'écart, ne pas tout réécrire.

## 1. Socle
- **Runtime** : dernière version LTS de .NET (ex. .NET 10), C# à jour, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` en CI.
- Gestion centralisée : `Directory.Build.props` (options communes), `Directory.Packages.props` (Central Package Management).
- Analyse : analyzers .NET activés (`<AnalysisLevel>latest-recommended</AnalysisLevel>`), `.editorconfig` versionné, `dotnet format` en CI.

## 2. Architecture de référence
**Clean Architecture** pour un domaine riche, **Vertical Slices** pour une API majoritairement CRUD.
```
src/
├── MyApp.Domain/          entités, value objects, événements, règles — aucune dépendance NuGet technique
├── MyApp.Application/     use cases (commands/queries), interfaces (ports), DTO, validation
├── MyApp.Infrastructure/  EF Core, clients HTTP, messaging, implémentations des ports
└── MyApp.Api/             ASP.NET Core (Minimal APIs ou Controllers), composition root, middlewares
tests/
├── MyApp.Domain.Tests/
├── MyApp.Application.Tests/
├── MyApp.Api.IntegrationTests/
└── MyApp.ArchitectureTests/
```
- Règles de dépendance vérifiées par **NetArchTest** ou **ArchUnitNET**.
- Option distribuée : **.NET Aspire** pour l'orchestration locale, la configuration des service defaults et l'observabilité.

## 3. Choix techniques par défaut
| Besoin | Choix par défaut | Alternatives |
|---|---|---|
| API HTTP | ASP.NET Core Minimal APIs + `TypedResults` | Controllers si équipe habituée |
| Docs API | `Microsoft.AspNetCore.OpenApi` + Scalar | Swashbuckle |
| Accès données | EF Core (migrations versionnées) | Dapper pour requêtes de lecture critiques |
| Validation | FluentValidation | DataAnnotations pour cas simples |
| Médiation CQRS | Handlers explicites injectés | MediatR / Wolverine si justifié (ADR, vérifier la licence) |
| Mapping | Mapping manuel / méthodes d'extension | Mapperly (source generator) |
| Messaging | MassTransit / Wolverine / SDK natif du broker | — |
| Résilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) | — |
| Logs / traces | `ILogger` + OpenTelemetry (OTLP) | Serilog comme sink |
| Erreurs HTTP | `ProblemDetails` (RFC 9457) + `IExceptionHandler` | — |
| Auth | `Microsoft.Identity.Web` / JWT Bearer, OIDC | — |
| Tests | xUnit, FluentAssertions ou Shouldly, NSubstitute, Testcontainers, `WebApplicationFactory` | NUnit, Moq |

## 4. Conventions de code
- `PascalCase` types, méthodes, propriétés ; `camelCase` paramètres et variables ; `_camelCase` champs privés ; interfaces préfixées `I`.
- File-scoped namespaces ; un type public par fichier.
- `async`/`await` de bout en bout, suffixe `Async`, `CancellationToken` propagé sur toute méthode I/O. Jamais `.Result` ni `.Wait()`.
- `record` pour les DTO et value objects ; `sealed` par défaut sur les classes non conçues pour l'héritage.
- Injection de dépendances par constructeur (primary constructors autorisés) ; pas de service locator.
- Options typées : `IOptions<T>` + `ValidateOnStart()`.
- Pas d'exception pour le contrôle de flux métier : utiliser un type `Result<T>` ou des erreurs de domaine explicites.
- `DateTimeOffset` / `TimeProvider` (testable) plutôt que `DateTime.Now`.

## 5. EF Core
- `DbContext` dans Infrastructure ; configurations via `IEntityTypeConfiguration<T>`.
- `AsNoTracking()` pour les lectures ; projections `Select` vers DTO ; éviter le lazy loading.
- Pas de `ToList()` avant un filtre ; surveiller les N+1 (logs SQL en dev).
- Migrations générées, relues, appliquées par pipeline (bundle ou script idempotent), jamais au démarrage en prod.

## 6. Sécurité
- Secrets : User Secrets en local, Key Vault / équivalent en environnement ; identité managée.
- `[Authorize]` / `RequireAuthorization()` par défaut, `AllowAnonymous` explicite.
- Validation de toutes les entrées ; pas de `FromSqlRaw` avec concaténation.
- Rate limiting (`Microsoft.AspNetCore.RateLimiting`), HTTPS + HSTS, CORS restrictif.

## 7. Tests
- Nommage : `Methode_Scenario_ResultatAttendu`.
- Pattern Arrange / Act / Assert ; un comportement par test.
- Tests d'intégration contre une vraie base via **Testcontainers** (pas l'InMemory provider).
- Couverture cible : domaine et application > 80 %.

## 8. Anti-patterns à refuser en revue
- Logique métier dans les endpoints ou dans `DbContext`.
- Repository générique au-dessus d'EF Core sans valeur ajoutée.
- `async void` (hors handlers d'événements UI), `Task.Run` dans ASP.NET pour « rendre async ».
- `catch (Exception)` silencieux.
- `HttpClient` instancié manuellement (utiliser `IHttpClientFactory`).

## 9. Commandes
```bash
dotnet restore
dotnet build -c Release
dotnet test --collect:"XPlat Code Coverage"
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
```
