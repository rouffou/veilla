# Contribuer à Veilla

## Branches (trunk-based)

`main` est la seule branche longue : elle est toujours livrable et **protégée** (voir plus bas). Tout changement
passe par une branche courte et une pull request, y compris pour le mainteneur.

Nom de branche : `<type>/<numéro-d'issue>-<résumé-court>` en minuscules, mots séparés par des tirets, sans accents.
Le numéro d'issue est facultatif pour `chore`, `ci`, `docs` et `deps` quand aucune issue n'existe.

| Type | Usage | Exemple |
|---|---|---|
| `feat` | Nouvelle fonctionnalité ou exigence du cahier des charges | `feat/264-saga-examen-reprise` |
| `fix` | Correction d'anomalie | `fix/312-delai-reprise-jour-ferie` |
| `refactor` | Restructuration sans changement de comportement | `refactor/obligations-moteur` |
| `test` | Tests seuls | `test/sagas-bout-en-bout` |
| `docs` | Documentation, ADR | `docs/adr-0009-reporting` |
| `ci` | Workflows GitHub Actions | `ci/protection-branche-main` |
| `infra` | Terraform (`infra/`) | `infra/abonnements-service-bus` |
| `chore` | Outillage, configuration du dépôt | `chore/gitignore` |
| `deps` | Mise à jour manuelle de dépendances (Dependabot utilise `dependabot/...`) | `deps/azurerm-5` |
| `hotfix` | Correctif urgent de production | `hotfix/fuite-journal-audit` |

Une branche vit quelques jours au plus : on la rebase sur `main` (`git pull --rebase origin main`) plutôt que d'y
fusionner `main`. Elle est supprimée automatiquement après fusion.

## Commits et titre de PR (Conventional Commits)

`<type>(<portée>): <description à l'impératif, en français>` — portée = service ou zone (`obligations`, `socle`,
`contrats`, `infra`, `portail-employeur`…). Les identifiants d'exigences vont dans le corps.

```text
feat(obligations): orchestrer la saga d'examen de reprise

Agrégat ProcessusReprise, minuteries en base, compensations (ARC-33, POR-04).
```

Les PR sont fusionnées en **squash** : le titre de la PR devient le message du commit sur `main` et doit donc suivre
ce format. Une rupture de contrat ou d'API se signale par `!` (`feat(contrats)!: …`) et une nouvelle version (ARC-34).

## Pull requests

- Remplir le modèle (`.github/pull_request_template.md`) : exigences couvertes, zone de sensibilité, tests, contrôles
  ARC-06.
- Une PR = un sujet. Les changements de fichiers partagés (contrats, `Security.cs`, `Directory.Packages.props`,
  `infra/`) sont isolés autant que possible.
- Les conversations de revue doivent être résolues avant la fusion.

## Protection de `main`

Règle de dépôt « Protection de main » (ruleset GitHub) :

- pull request obligatoire, fusion en squash uniquement, conversations résolues ;
- contrôles obligatoires, branche à jour avec `main` : `Statut .NET`, `Statut fronts`, `Statut Terraform`,
  `Statut images`, `Statut CodeQL` ;
- historique linéaire ; poussée forcée et suppression de la branche interdites.

Chaque workflow se déclenche sur toutes les PR. Son job « Détection des changements » ignore les jobs coûteux quand
la PR ne touche pas les chemins concernés, et le job « Statut … » reste alors vert. Un contrôle obligatoire ne reste
donc jamais en attente. Ajouter un service ou une solution ne demande aucun changement de la règle.

La règle n'exige pas de « résultats de code scanning » : CodeQL n'analyse que les PR qui touchent `src/` ou `tests/`,
et une telle exigence bloquerait indéfiniment les PR limitées à l'infrastructure ou à la documentation. Les alertes
CodeQL restent visibles sur la PR et dans l'onglet Security ; elles se traitent avant la fusion.

## Vérifications locales avant de pousser

```bash
dotnet build Sepp.slnx -c Release
dotnet format Sepp.slnx --verify-no-changes
dotnet test --solution Sepp.slnx -c Release
bash tools/regenerate-solutions.sh
```

Les tests d'intégration demandent Docker (Testcontainers). Pour le front : `npm ci`, `npm run lint`,
`npm run test:ci` et `npm run build` dans `src/Frontends/veilla-web`.

## Sécurité

Aucun secret réel dans le dépôt, qui est public : les valeurs de `deploy/local` et des `appsettings.Development.json`
sont des valeurs de développement local uniquement. Les vulnérabilités se signalent en privé (voir `SECURITY.md`).
