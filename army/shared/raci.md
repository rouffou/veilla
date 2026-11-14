# Matrice RACI des agents

R = Réalise · A = Approuve (décide) · C = Consulté · I = Informé

| Activité | PM | PO | BA | EA | SA | SwA | SecA | DataA | Dev | Rev | QA | DevOps | Cloud | SRE | TW |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Charte projet, planning, budget | **R/A** | C | I | C | C | I | I | I | I | I | I | I | I | I | I |
| Vision produit, backlog, priorités | C | **R/A** | C | I | C | I | I | I | I | I | C | I | I | I | I |
| Analyse métier, specs fonctionnelles | I | A | **R** | C | C | I | I | C | I | I | C | I | I | I | I |
| Alignement SI, principes, roadmap SI | C | C | I | **R/A** | C | I | C | C | I | I | I | I | C | I | I |
| Architecture de la solution, NFR | C | C | C | C | **R/A** | C | C | C | I | I | C | C | C | C | I |
| Conception logicielle d'un composant | I | I | I | I | A | **R** | C | C | C | C | I | I | I | I | I |
| Modèle de menaces, exigences sécu | I | I | I | C | C | C | **R/A** | C | I | C | C | C | C | C | I |
| Modèle de données | I | C | C | C | A | C | C | **R** | C | I | I | I | I | I | I |
| Implémentation | I | I | I | I | I | A | I | I | **R** | C | I | I | I | I | I |
| Revue de code | I | I | I | I | I | C | C | I | C | **R/A** | I | I | I | I | I |
| Stratégie et exécution des tests | I | C | C | I | I | C | C | I | C | I | **R/A** | C | I | I | I |
| CI/CD | I | I | I | I | C | C | C | I | C | I | C | **R/A** | C | C | I |
| Infrastructure cloud, landing zone | C | I | I | C | A | I | C | I | I | I | I | C | **R** | C | I |
| SLO, observabilité, incidents | I | C | I | I | C | I | C | I | C | I | I | C | C | **R/A** | I |
| Documentation utilisateur / technique | I | C | C | I | C | C | I | I | C | I | I | I | I | I | **R/A** |

**Abréviations** : PM `project-manager` · PO `product-owner` · BA `business-analyst` · EA `enterprise-architect` · SA `solution-architect` · SwA `software-architect-*` · SecA `security-architect` · DataA `data-architect` · Dev `developer-*` / `frontend-developer` · Rev `code-reviewer` · QA `qa-engineer` · DevOps `devops-engineer` · Cloud `cloud-architect` · SRE `sre` · TW `technical-writer`.
