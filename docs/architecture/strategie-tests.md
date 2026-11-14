# Stratégie de tests

> Ticket ARC-23 (#196). Ce document décrit ce qui existe, où cela vit et ce qui reste à faire. Il ne promet rien que le dépôt ne fasse pas.

## 1. Principes
- **Le test le moins cher qui prouve la règle.** La logique métier se teste sans infrastructure ; la base de données et le HTTP ne se testent que là où ils portent le risque.
- **Un garde-fou est un test.** Les règles d'architecture, de sécurité et de contrat sont vérifiées par des tests qui échouent en CI (voir `CLAUDE.md`, « Garde-fous d'architecture »), pas par des revues.
- **Aucune donnée réelle.** Toutes les données de test sont fictives (§7).
- **Un test instable est corrigé ou mis en quarantaine avec un ticket.** Jamais ignoré en silence.

## 2. Pyramide

| Niveau | Quantité | Vitesse | Ce qu'il prouve |
|---|---|---|---|
| Domaine et application (unitaires, sans infrastructure) | la majorité | millisecondes | règles métier, cas limites, décisions des gestionnaires de commandes |
| Adaptateurs et API (intégration, PostgreSQL éphémère via Testcontainers) | moyen | secondes | persistance EF, migrations, outbox et inbox, endpoints HTTP, autorisations |
| Contrat (instantanés OpenAPI, consommateur-fournisseur) | peu, mais larges | millisecondes à secondes | une API ou un double de test ne dérive pas de ce que l'autre côté attend |
| Architecture et conformité (garde-fous) | peu | secondes | couches, zones de données, droits, configuration, Terraform |
| Bout en bout entre services (`tests/Sepp.Sagas.Tests`) | peu | secondes | sagas et enchaînements d'événements |
| Fronts (`ng test`, Vitest) | moyen | secondes | composants et services Angular |

Pas encore couverts : E2E navigateur sur les parcours critiques, tests de charge dérivés des exigences non fonctionnelles, accessibilité automatisée (axe-core). Voir §9.

## 3. Où vivent les tests

Une solution par service ou BFF (ADR 0002) : `src/Services/<Service>/<Service>.slnx`.

| Projet | Contenu |
|---|---|
| `Sepp.<Service>.Domain.Tests` | agrégats, objets valeur, règles ; aucune dépendance d'infrastructure |
| `Sepp.<Service>.Application.Tests` | gestionnaires de commandes et de requêtes, avec des doubles des ports |
| `Sepp.<Service>.Integration.Tests` | adaptateurs réels sur PostgreSQL éphémère, hôte ASP.NET Core en mémoire, contrat OpenAPI, contrat consommateur |
| `Sepp.<Service>.Architecture.Tests` | règles de couches (`tests/Sepp.Testing.Architecture`), configuration propre au service |
| `src/Bff/<Canal>/tests/…Tests` et `…Integration.Tests` | écrans et traductions (unitaires) ; BFF complet face aux services simulés au niveau HTTP |
| `tests/Sepp.BuildingBlocks.Tests` | socle et garde-fous transverses : matrice des droits, Terraform, configuration, durées légales en dur |
| `tests/Sepp.Contracts.Tests` | contrats d'événements ↔ catalogue `docs/architecture/evenements.md` |
| `tests/Sepp.Sagas.Tests` | scénarios de bout en bout entre services |
| `tests/Sepp.Testing.Contrats`, `tests/Sepp.Testing.Architecture` | bibliothèques d'aide (pas de tests propres) |
| `src/Frontends/veilla-web` | tests Angular (`npm run test:ci`) |

Outils : xUnit v3 sur Microsoft.Testing.Platform, Shouldly, NSubstitute, Testcontainers PostgreSQL, `WebApplicationFactory`.

## 4. Tests de contrat

### 4.1 Choix : instantanés OpenAPI, pas Pact
Le contrat de chaque service et BFF est déjà versionné : `docs/api/openapi/<nom>.json`, comparé au document réellement servi (`ContratOpenApiTests`, #430). On en tire un contrat **consommateur-fournisseur sans dépendance nouvelle** :

- chaque consommateur **déclare** les opérations, paramètres, champs de corps et champs de réponse qu'il utilise ;
- un test vérifie que l'instantané du fournisseur satisfait ces déclarations ;
- les doubles de test des BFF sont confrontés au même instantané.

**Pourquoi pas Pact.** Il demande un paquet par langage, un courtier ou des fichiers de pacte à publier et à faire vérifier côté fournisseur, et une seconde description du même contrat à maintenir à côté d'OpenAPI. Les consommateurs et les fournisseurs vivent dans le même dépôt, sont compilés et testés dans la même CI, et les ruptures sont déjà détectées sur l'instantané. Le coût de Pact (outillage, workflow de vérification, paquets à auditer pour la licence et la sécurité) n'apporte rien tant que le monodépôt tient. À reconsidérer si un consommateur externe (partenaire, `docs/architecture/api-partenaires.md`) doit prouver son contrat sans accès au dépôt.

### 4.2 Les trois mécanismes (`tests/Sepp.Testing.Contrats`)

| Mécanisme | Où | Ce qu'il détecte |
|---|---|---|
| **Attentes du consommateur** (`AttenteConsommateur`, `ContratConsommateur.Verifier`) | `ContratConsommateurTests` dans le projet d'intégration de chaque consommateur | opération supprimée ou renommée, paramètre de requête supprimé ou devenu obligatoire, champ de corps supprimé ou devenu obligatoire, code de succès modifié, champ de réponse supprimé (si la réponse est décrite) |
| **Doubles de test conformes** (`ControleContratDouble`, branché dans `ServicesAvalSimules` des 3 BFF) | à chaque `Repondre(...)` | un double qui répond sur une route inexistante ; une réponse décrite par le fournisseur dont les champs ou les valeurs d'énumération (casse comprise) ne correspondent pas : c'est la classe du bug #436 (« Personne » au lieu de « personne ») |
| **Requêtes réellement émises** (même contrôle, vérifié dans `DisposeAsync` des tests d'API des BFF) | chaque test d'API | le code du BFF appelle une route, un paramètre ou un champ de corps qui n'existe pas chez le service |

Consommateurs couverts :

| Consommateur | Fournisseurs | Fichier |
|---|---|---|
| BFF Travailleur | Planification, Surveillance médicale, Personnes, Affiliés, Obligations, Documents | `src/Bff/Travailleur/tests/…Integration.Tests/ContratConsommateurTests.cs` |
| BFF Employeur | Obligations, Affiliés, Personnes, Postes et risques | `src/Bff/Employeur/tests/…` |
| BFF Interne | Personnes, Affiliés (le relais YARP ne consomme aucune route précise) | `src/Bff/Interne/tests/…` |
| Affiliés | Intégrations (BCE) | `src/Services/Affilies/tests/…Integration.Tests/` |
| Communications | Personnes, Affiliés (annuaire) | `src/Services/Communications/tests/…` |
| Documents | Affiliés, Personnes (source linguistique), Surveillance médicale (dossier de santé) | `src/Services/Documents/tests/…` |
| Intégrations | Personnes (DIMONA, registre national, identification) | `src/Services/Integrations/tests/…` |

Le compte technique (jeton OIDC en client credentials) appelle le fournisseur d'identité, pas un service interne : il n'a pas de contrat OpenAPI interne et reste couvert par ses tests unitaires.

### 4.3 Limites connues
1. **Les réponses ne sont pas décrites.** Aucun endpoint des services ne déclare le type de sa réponse (`Produces<T>`) : aucune opération des services ne décrit un corps de réponse (un code `200` sans schéma). Les « champs de réponse lus » déclarés par les consommateurs sont donc signalés comme non vérifiables (la vérification s'active dès qu'un service typera sa réponse), et la casse des énumérations dans les réponses n'est contrôlée que sur les doubles dont la réponse est décrite. Suite : typer les réponses des endpoints consommés par les BFF et les services (ticket de suite).
2. **Codes d'erreur** (404, 409…) : non déclarés dans les instantanés (réponses `problem+json` génériques), donc non contrôlés.
3. **`required` n'est pas fiable** : le générateur OpenAPI de .NET marque comme requises toutes les propriétés d'un record, nullables comprises. Le contrôle n'exige que les propriétés non nullables.
4. **Enumération `TypeDestinataire`** : l'instantané de Documents annonce `Affilie, Personne, Dossier` (PascalCase) alors que le service publie `personne` (#436). Écart à corriger côté Documents quand ses réponses seront typées ; le BFF Travailleur compare sans casse en attendant.
5. Les adaptateurs HTTP de service à service (annuaire, source linguistique, BCE, DIMONA) n'ont pas encore de test avec un double HTTP : leurs attentes sont vérifiées contre les instantanés, pas leur lecture des réponses.

### 4.4 Ajouter ou changer un appel
1. Ajouter l'appel dans la liste `Attentes` du consommateur (méthode, chemin modèle de l'instantané, paramètres, champs du corps, champs lus).
2. Si le fournisseur change volontairement : `bash tools/regenerer-openapi.sh`, committer l'instantané, puis adapter les consommateurs en échec. Une rupture exige une nouvelle version d'API (ARC-30).
3. Un test d'API de BFF qui programme un double (`Repondre`) est contrôlé automatiquement.

## 5. Couverture

### 5.1 Règle
Cible : **80 % des lignes** des assemblies `Sepp.<Service>.Domain` et `Sepp.<Service>.Application`, par service, bloquante en CI.

Mise en œuvre : `tools/verifier-couverture.py`, étape « Seuil de couverture Domain et Application » du job `build-test` de `.github/workflows/dotnet.yml`.

- Les rapports Cobertura des projets de tests d'une solution sont fusionnés (une ligne est couverte si un projet de tests l'exerce).
- Seuils dans `tools/couverture-seuils.json`. Un service absent est tenu à la cible de 80 %.
- Un service sous la cible porte un **seuil propre au niveau actuel** (cliquet) : il ne peut que monter. Le script signale quand un seuil peut être relevé. Un seuil ne se baisse jamais ; il se relève dans la PR qui ajoute des tests.
- Les PR qui ne touchent pas au .NET ne l'exécutent pas : le job `build-test` est ignoré (job `changements`) et « Statut .NET » reste vert.
- La synthèse `irongut/CodeCoverageSummary` reste informative (`fail_below_min: false`) : elle agrège tous les assemblies, tests et adaptateurs compris, et ne sait pas appliquer un seuil par couche.

### 5.2 État mesuré
Mesure du 2026-11-14 (lignes, tous projets de tests de la solution fusionnés) :

| Service | Domain | Application | Seuil appliqué |
|---|---:|---:|---|
| Affiliés | 94,6 % | 95,2 % | 80 % |
| Audit | 95,7 % | 98,9 % | 80 % |
| Communications | 96,5 % | 96,6 % | 80 % |
| Documents | 90,9 % | 87,3 % | 80 % |
| Intégrations | 95,5 % | 93,3 % | 80 % |
| Obligations | 96,3 % | 95,1 % | 80 % |
| Personnes | 93,6 % | 86,8 % | 80 % |
| Planification | 94,1 % | 88,3 % | 80 % |
| Postes et risques | 90,2 % | 81,6 % | 80 % (marge de 1,6 point) |
| Référentiels | 90,1 % | 86,2 % | 80 % |
| Réintégration | 91,8 % | 84,9 % | 80 % |
| **Surveillance médicale** | 86,2 % | **72,6 %** | Application : **71 %** (cliquet, écart de 7,4 points) |
| **Socle (BuildingBlocks)** | **68,5 %** | 98,4 % | Domain : **67 %** (cliquet, écart de 11,5 points) |

Sagas et BFF n'ont pas d'assembly Domain ni Application : le script n'y trouve aucune ligne et ne bloque pas.

## 6. Intégration continue
`dotnet.yml` : un job par solution (compilation Release sans avertissement, tests et couverture, seuil), plus style (`dotnet format`), solutions à jour et analyse des dépendances NuGet. `frontend.yml` : lint, tests, build et audit npm. Les résultats de tests sont publiés (`dorny/test-reporter`) et les rapports archivés 30 jours.

## 7. Données de test
- Données **fictives uniquement** (dépôt public, NF-14) : identifiants `Guid.CreateVersion7()`, noms et adresses inventés, domaines `.test` ou `exemple.test`, jamais de NISS ni de coordonnées réels.
- Bases : PostgreSQL éphémère par classe ou collection de tests (Testcontainers), détruite en fin d'exécution ; aucune base partagée.
- Services aval : doubles HTTP (`ServicesAvalSimules`), jamais un service réel ; ils sont contrôlés contre le contrat (§4).
- Clés de chiffrement et secrets : valeurs de développement générées pour le test, jamais de secret réel.
- Le jeu de démonstration (`tools/donnees-demo`, #393) est destiné à la démo, pas aux tests automatisés.

## 8. Règles de contribution
- Toute modification de comportement s'accompagne d'un test au niveau le plus bas qui l'exprime.
- Un bug se reproduit par un test avant d'être corrigé. Si le bug vient d'un écart consommateur-fournisseur, ajouter l'attente ou corriger le double dans le contrat (§4).
- Les tests nomment le comportement en français (`Le_contenu_d_un_document_d_un_autre_destinataire_n_est_jamais_lu`).
- Commandes : `dotnet test --solution src/Services/<X>/<X>.slnx -c Release` ; avec couverture : ajouter `--coverage --coverage-output-format cobertura --results-directory TestResults`, puis `python3 tools/verifier-couverture.py --solution <X> --resultats TestResults`.

## 9. Reste à faire
- Typer les réponses des endpoints consommés (`Produces<T>`) pour activer la vérification des champs de réponse.
- Tests HTTP des adaptateurs service à service avec un double contrôlé par le contrat.
- E2E navigateur des parcours critiques, tests de charge et accessibilité automatisée.
- Remonter les seuils de couverture des services sous la cible jusqu'à 80 %.
