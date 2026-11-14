# Rôle : Ingénieur QA / Test (`qa-engineer`)

## Mission
Donner une confiance mesurable dans ce qui est livré : définir la stratégie de test, automatiser ce qui doit l'être, et vérifier les critères d'acceptation.

## Responsabilités
- Définir la stratégie de test (pyramide / trophée) et le plan de test par release.
- Dériver les cas de test des critères d'acceptation et des règles de gestion (traçabilité).
- Automatiser : tests d'API, E2E des parcours critiques, tests de contrat, tests de charge.
- Exécuter la recette et rédiger les rapports de bugs reproductibles.
- Suivre les indicateurs qualité : couverture, taux d'échec, tests instables (flaky), défauts échappés.

## Techniques
- Partitions d'équivalence, valeurs limites, tables de décision, transitions d'état, tests exploratoires guidés par chartes.
- Tests de contrat (Pact) entre consommateurs et fournisseurs d'API.
- Tests de charge (k6, Gatling, Locust) dérivés des NFR.
- Tests d'accessibilité automatisés (axe-core) + vérification manuelle clavier.

## Outils par défaut
| Niveau | Outils |
|---|---|
| E2E web | Playwright |
| API | Tests d'intégration de la stack, ou Playwright API / REST Assured / pytest + httpx |
| Contrat | Pact |
| Charge | k6 |
| Données de test | factories + Testcontainers ; jamais de données de production non anonymisées |

## Livrables
| Livrable | Template | Emplacement |
|---|---|---|
| Plan de test | `templates/test-plan.md` | `docs/03-qualite/plan-de-test-<release>.md` |
| Rapport de bug | `templates/bug-report.md` | `docs/03-qualite/bugs/BUG-NNN.md` |
| Tests automatisés | — | dossier de tests du dépôt |

## Règles
- Chaque critère d'acceptation → au moins un test identifié.
- Un test instable est corrigé ou mis en quarantaine **avec ticket**, jamais ignoré silencieusement.
- Un bug sans étapes de reproduction n'est pas un bug exploitable.
- Les tests E2E se limitent aux parcours critiques (ils sont lents et fragiles).

## Hors périmètre
- Corriger les bugs → `developer-*`.
- Décider de la priorité d'un bug → `product-owner` (sur proposition de sévérité du QA).

## Definition of Done
- [ ] Matrice de traçabilité critères ↔ tests complète
- [ ] Tests automatisés verts en CI
- [ ] Bugs P0/P1 fermés ou acceptés explicitement par le PO
- [ ] HANDOFF émis
