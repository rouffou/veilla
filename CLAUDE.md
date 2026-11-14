# CLAUDE.md

> Instructions chargées automatiquement par Claude Code dans ce projet.

## Projet
- **Nom** : veilla, logiciel métier SEPP (service externe de prévention et de protection au travail, Belgique).
- **Objectif** : couvrir le cahier des charges `docs/cahier-des-charges-sepp.pdf` pour le personnel interne (médecins du travail, infirmiers, planificateurs, gestionnaires), les employeurs affiliés et les travailleurs. Objectif à court terme : une démo commerciale pleinement opérationnelle (épique #389).
- **Stack(s)** : .NET 10, en microservices clean architecture (Domain / Application / Adapters / Infrastructure), DDD, outbox et inbox, sagas persistantes.
- **Front-end** : Angular 22. Un seul espace de travail `src/Frontends/veilla-web` contient les projets app-interne, portail-employeur, portail-travailleur et shared.
- **Base(s) de données** : PostgreSQL, une base par service.
- **Cloud / hébergement** : Azure (Container Apps, PostgreSQL, Key Vault et Managed HSM, Service Bus, Storage WORM, APIM), en Terraform (`infra/`). En local, Docker compose.
- **CI/CD** : GitHub Actions (dépôt public `rouffou/veilla`), avec CodeQL, Dependabot, images signées cosign et déploiement progressif (`deploy.yml`).
- **Contraintes** :
  - données de santé (RGPD art. 9) et cloisonnement en zones standard, médicale et psychosociale (ARC-04, ARC-06) ;
  - hébergement dans l'UE ;
  - authentification forte (NF-01) ;
  - accessibilité WCAG 2.1 AA ;
  - interface en FR, NL et DE ;
  - licence propriétaire (`LICENSE`).

## Commandes
| Action | Commande |
|---|---|
| Installer les dépendances | `dotnet restore Sepp.slnx` · `cd src/Frontends/veilla-web && npm ci` |
| Build | `dotnet build Sepp.slnx -c Release` (zéro avertissement exigé) · `npm run build` |
| Tests | `dotnet test` (par solution : `src/Services/<X>/<X>.slnx`, `tests/…`) · `npm run test:ci` |
| Lint / format | `dotnet format Sepp.slnx --verify-no-changes` · `npm run lint` · `terraform fmt -check -recursive` puis `terraform validate` |
| Solutions | `bash tools/regenerate-solutions.sh` : ne committer que les vrais changements, pas les fins de ligne CRLF |
| Lancer en local | `docker compose -f deploy/local/compose.yaml up -d --build` (voir `deploy/local/README.md`) |

## Structure du dépôt
- `src/Services/<Service>` : un microservice par domaine, avec ses tests. Services : Affiliés, Audit, Communications, Documents, Intégrations, Obligations, Personnes, Planification, Postes et risques, Référentiels, Réintégration, Surveillance médicale.
- `src/Bff/{Interne,Employeur,Travailleur}` : un BFF par canal (ADR 0006, 0013).
- `src/BuildingBlocks` : socle commun (sécurité `Security.cs`, messagerie, persistance, web).
- `src/Contracts/Sepp.Contracts` : contrats d'événements ARC-06, catalogués dans `docs/architecture/evenements.md`.
- `src/Sagas`, `tests/Sepp.Sagas.Tests` : solution de tests de bout en bout entre services. Les sagas elles-mêmes vivent dans leur service propriétaire (ADR 0008 : `ProcessusReprise` dans Obligations, `Trajet` dans Réintégration).
- `src/Frontends/veilla-web` : les fronts Angular.
- `infra/` : Terraform (modules et environnements). `deploy/local/` : compose, Keycloak et PostgreSQL locaux.
- `docs/` : `adr/`, `architecture/`, `securite/`, `conformite/`, `api/openapi/` (instantanés de contrat) et le cahier des charges.
- `tools/` : scripts (solutions, déploiement, backlog GitHub).

## Conventions spécifiques au projet
> Elles priment sur les standards génériques de `army/`.
- **Langue** : tout en **français**, y compris les identifiants métier du code (`ParametresLegaux`, `EcransDocuments`…) et les **messages de commit**. Seuls les termes techniques consacrés restent en anglais.
- **Emplacement des livrables** : on garde l'arborescence existante plutôt que celle de `army/shared/conventions.md` :
  - ADR : `docs/adr/NNNN-titre.md`, avec l'index `docs/adr/README.md` (numérotation continue, vérifier le prochain numéro libre) ;
  - architecture : `docs/architecture/` ;
  - sécurité : `docs/securite/` ;
  - conformité : `docs/conformite/` ;
  - pilotage (état des lieux du backlog, registre RAID, comptes rendus) : `docs/00-pilotage/`.
- **Tickets** : GitHub Issues de `rouffou/veilla`, avec un jalon par lot. On traite les lots **dans l'ordre** (Lot 0, puis 1, 2…). Chaque ticket est tenu à jour : commentaire au lancement, fermeture par la PR, ticket de suite pour ce qui reste.
- **Git** :
  - main est protégée : jamais de push direct ;
  - branche `<type>/<n°>-<résumé>` (feat, fix, refactor, docs, chore, ci, test, perf) ;
  - Conventional Commits en français, avec le trailer `Co-Authored-By` ;
  - PR vers main avec `Closes #n` ou `Refs #n`, fusion en squash ;
  - `--force-with-lease` est permis **uniquement** sur sa propre branche de travail.
  - Le responsable produit a autorisé l'orchestrateur à créer branches, commits et PR, et à fusionner une PR une fois la CI verte. **Les agents délégués ne fusionnent jamais.**
- **Garde-fous d'architecture**. Des tests les vérifient, il faut les tenir à jour :
  - catalogue des événements ↔ contrats ;
  - abonnements DI ⊆ `subscribes_to` Terraform (`AbonnementsTerraformTests`) ;
  - clés de configuration ⊆ injections Terraform (`ConfigurationTerraformTests`) ;
  - matrice des droits `docs/securite/matrice-droits.csv` ↔ `Security.cs` ;
  - aucune durée légale écrite en dur (les règles légales viennent de Référentiels) ;
  - instantanés OpenAPI.
- **Zones** : aucune donnée de santé ni psychosociale ne sort de sa zone (ARC-06). Les événements ne portent que des identifiants, des dates et des catégories.
- **Docker** : ne jamais arrêter ni supprimer le projet compose `veilla` de l'utilisateur ni ses volumes. Les tests réels se font sur un projet distinct (`-p veilla-<ticket>`), puis `down -v` sur ce projet seulement.
- **Dépôt public** : aucun secret réel ni aucune donnée personnelle réelle, même en exemple.
- **Choix du modèle des agents délégués** :
  - Opus pour la conception, la sécurité et les zones médicale et psychosociale ;
  - Sonnet pour l'implémentation ;
  - Haiku pour les tâches mécaniques.

## Règles de travail
- **Lire avant d'écrire** : explorer le code existant et respecter ses conventions avant toute modification.
- **Changements ciblés** : modifier uniquement ce que demande la tâche ; pas de refactoring opportuniste non demandé.
- **Tests** : toute modification de comportement s'accompagne de tests. Lancer build, tests et lint avant de déclarer une tâche terminée.
- **Vérité** : si un test échoue ou qu'une étape n'a pas été faite, le dire clairement.
- **Secrets** : ne jamais lire, afficher ni committer de secret (`.env`, clés, certificats) ; utiliser le coffre prévu.
- **Dépendances** : pas de nouvelle dépendance sans justification (maintenance, licence, sécurité).
- **Actions irréversibles** (production, suppression de données, dépense cloud) : confirmation explicite obligatoire.
- **Décisions structurantes** : tracées en ADR dans `docs/adr/` (`/adr`).

## Commandes de l'équipe d'agents
| Commande | Usage |
|---|---|
| `/cadrage <besoin>` | Cadrer un nouveau projet de bout en bout |
| `/feature <story ou description>` | Livrer une fonctionnalité (conception → dev → revue → QA) |
| `/bugfix <description>` | Reproduire, corriger et vérifier un bug |
| `/incident <symptômes>` | Gérer un incident de production |
| `/audit [périmètre]` | Auditer l'existant et proposer une trajectoire |
| `/review [branche ou fichiers]` | Revue de code priorisée |
| `/adr <décision>` | Rédiger un ADR |
| `/status` | Point d'avancement du projet |

## Équipe d'agents
@army/ORCHESTRATOR.md
