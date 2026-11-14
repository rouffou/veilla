# CLAUDE.md

> Instructions chargées automatiquement par Claude Code dans ce projet.
> Complète les sections marquées `À compléter` : plus elles sont précises, moins les agents posent de questions.

## Projet
- **Nom** : À compléter
- **Objectif** : À compléter (une ou deux phrases : quoi, pour qui)
- **Stack(s)** : À compléter (.NET | Python | Java | TypeScript)
- **Front-end** : À compléter (React/Next.js | Angular | Vue | aucun)
- **Base(s) de données** : À compléter
- **Cloud / hébergement** : À compléter (Azure | AWS | GCP | on-premise)
- **CI/CD** : À compléter (GitHub Actions | GitLab CI | Azure DevOps)
- **Contraintes** : À compléter (réglementaires, délais, budget, souveraineté…)

## Commandes
| Action | Commande |
|---|---|
| Installer les dépendances | À compléter |
| Build | À compléter |
| Tests | À compléter |
| Lint / format | À compléter |
| Lancer en local | À compléter |

## Structure du dépôt
À compléter : les dossiers principaux et leur rôle (une ligne chacun).

## Conventions spécifiques au projet
> Elles priment sur les standards génériques de `army/stacks/`.
- À compléter (ou supprimer cette section si aucune)

## Règles de travail
- **Langue** : répondre et documenter en français ; code, identifiants et messages de commit en anglais.
- **Lire avant d'écrire** : explorer le code existant et respecter ses conventions avant toute modification.
- **Changements ciblés** : modifier uniquement ce que demande la tâche ; pas de refactoring opportuniste non demandé.
- **Tests** : toute modification de comportement s'accompagne de tests ; lancer build, tests et lint avant de déclarer une tâche terminée.
- **Vérité** : si un test échoue ou qu'une étape n'a pas été faite, le dire clairement.
- **Secrets** : ne jamais lire, afficher ni commiter de secret (`.env`, clés, certificats) ; utiliser le coffre-fort prévu.
- **Git** : ne jamais commiter, pousser ni réécrire l'historique sans demande explicite ; messages au format Conventional Commits.
- **Dépendances** : pas de nouvelle dépendance sans justification (maintenance, licence, sécurité).
- **Actions irréversibles** (production, suppression de données, dépense cloud) : confirmation explicite obligatoire.
- **Décisions structurantes** : tracées en ADR dans `docs/02-architecture/adr/` (`/adr`).

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
