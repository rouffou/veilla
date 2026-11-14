# Standards Java

> Lu par `software-architect-java`, `developer-java` et `code-reviewer` sur du code Java (ou Kotlin JVM, en adaptant). Les conventions existantes du dépôt priment : signaler l'écart, ne pas tout réécrire.

## 1. Socle
- **JDK** : dernière LTS (ex. Java 25, a minima 21) ; distribution Temurin ou équivalent.
- **Build** : Maven (par défaut en entreprise) ou Gradle (Kotlin DSL) ; wrapper versionné (`mvnw` / `gradlew`).
- **Framework** : Spring Boot (dernière version majeure supportée) ; Quarkus si démarrage rapide / natif requis (ADR).
- **Qualité** : Spotless (google-java-format ou palantir), Checkstyle, Error Prone, SpotBugs, SonarQube.

## 2. Architecture de référence
**Hexagonale** (ports & adapters), packaging **par fonctionnalité** puis par couche.
```
src/main/java/com/acme/myapp/
├── order/
│   ├── domain/          Order, OrderLine, OrderStatus, règles — aucun import Spring/JPA
│   ├── application/     OrderService (use cases), ports (interfaces in/out)
│   ├── adapter/in/web/  OrderController, DTO requête/réponse
│   └── adapter/out/persistence/  OrderJpaEntity, OrderRepositoryAdapter
├── shared/              éléments transverses
└── MyAppApplication.java
src/test/java/...        miroir + ArchitectureTest.java
```
- Règles de dépendance vérifiées par **ArchUnit** ; option **Spring Modulith** pour un monolithe modulaire.

## 3. Choix techniques par défaut
| Besoin | Choix par défaut | Alternatives |
|---|---|---|
| API HTTP | Spring Web MVC + virtual threads | WebFlux (si besoin réactif avéré) |
| Docs API | springdoc-openapi | — |
| Persistance | Spring Data JPA (Hibernate) | jOOQ / JdbcClient pour SQL complexe |
| Migrations | Flyway | Liquibase |
| Validation | Jakarta Bean Validation | — |
| Mapping | MapStruct | mapping manuel |
| Messaging | Spring for Apache Kafka / Spring AMQP / Spring Cloud Stream | — |
| Résilience | Resilience4j | — |
| Observabilité | Micrometer + OpenTelemetry, logs JSON (Logback/ECS) | — |
| Sécurité | Spring Security (OAuth2 Resource Server, OIDC) | — |
| Tests | JUnit 5, AssertJ, Mockito, Testcontainers, `@WebMvcTest`, `@DataJpaTest` | — |

## 4. Conventions de code
- `PascalCase` classes, `camelCase` méthodes/variables, `UPPER_SNAKE_CASE` constantes, packages en minuscules.
- **Records** pour DTO et value objects ; classes `final` par défaut ; immutabilité privilégiée.
- Injection **par constructeur** uniquement (pas de `@Autowired` sur champ).
- `Optional` en retour de méthode uniquement, jamais en paramètre ni en champ.
- Entités JPA séparées des objets de domaine quand le domaine est riche.
- Exceptions métier non vérifiées dédiées ; `@RestControllerAdvice` → `ProblemDetail` (RFC 9457).
- `java.time` exclusivement (`Instant`, `OffsetDateTime`), `Clock` injecté pour les tests.
- Sealed interfaces + pattern matching (`switch`) pour modéliser les états.

## 5. JPA
- `FetchType.LAZY` par défaut ; `@EntityGraph` / fetch join pour éviter le N+1.
- Transactions sur la couche application (`@Transactional` sur les use cases), `readOnly = true` pour les lectures.
- `spring.jpa.open-in-view=false`.
- Jamais de `ddl-auto=update` hors poste local : Flyway uniquement.

## 6. Sécurité
- Secrets via Spring Cloud Config / Vault / coffre du cloud ; jamais dans `application.yml` versionné.
- Endpoints Actuator restreints ; CSRF actif pour les apps à session.
- Scans : OWASP Dependency-Check ou Snyk / Dependabot.

## 7. Tests
- Nommage : `should<Résultat>_when<Condition>()` + `@DisplayName` lisible.
- Tranches de tests Spring (`@WebMvcTest`, `@DataJpaTest`) plutôt que `@SpringBootTest` partout.
- Testcontainers pour bases et brokers ; pas de H2 pour simuler PostgreSQL/Oracle.

## 8. Anti-patterns à refuser en revue
- Services « god class » ; entités anémiques + logique éparpillée dans des utilitaires statiques.
- Exposition directe des entités JPA dans les API.
- `catch (Exception e) { e.printStackTrace(); }`.
- Champs mutables partagés dans des beans singleton.

## 9. Commandes
```bash
./mvnw -B verify
./mvnw spotless:check
./mvnw dependency-check:check
# Gradle : ./gradlew build check
```
