# Workflow 04 — Audit et modernisation d'une application existante

**Déclencheur** : « Analyse ce projet », « On doit moderniser / migrer cette application », « Migration vers le cloud ».
**Résultat** : état des lieux, cible, trajectoire incrémentale (pattern *Strangler Fig*), premiers chantiers prêts.

## Étapes
| # | Agent(s) | Parallèle ? | Livrable |
|---|---|---|---|
| 1 | `software-architect-<stack>` | ✅ par stack | Audit du code : structure, dépendances, dette, couverture de tests, versions obsolètes → `docs/02-architecture/logicielle/audit-<app>.md` |
| 1 | `security-architect` | ✅ | Vulnérabilités, dépendances à risque, secrets exposés |
| 1 | `data-architect` | ✅ | Modèle de données existant, qualité, couplages par la base |
| 1 | `devops-engineer` | ✅ | Maturité build / déploiement, métriques DORA actuelles |
| 2 | `enterprise-architect` | — | Décision TIME (Tolérer / Investir / Migrer / Éliminer) et stratégie de migration cloud (les « 7 R » : Retire, Retain, Rehost, Relocate, Replatform, Repurchase, Refactor) |
| 3 | `solution-architect` | — | Architecture cible + trajectoire en étapes livrables indépendamment + ADR |
| 4 | `cloud-architect` | — | Design cloud cible + estimation des coûts (si migration cloud) |
| 5 | `project-manager` | — | Roadmap, lots, risques, plan de bascule |
| 6 | `qa-engineer` | — | Filet de sécurité : tests de caractérisation à écrire **avant** toute refonte |

## Règles
- Pas de « big bang » : chaque étape de la trajectoire est livrable et réversible.
- Écrire des tests de caractérisation avant de modifier un comportement existant.
- Mesurer avant / après (performance, coûts, DORA).
