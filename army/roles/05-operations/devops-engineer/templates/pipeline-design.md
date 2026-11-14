---
titre: Conception CI/CD — <Projet>
auteur: devops-engineer
statut: brouillon
date: AAAA-MM-JJ
---

# Conception CI/CD

## 1. Stratégie de branches
<trunk-based (défaut) | GitFlow — justification>

## 2. Environnements
| Env | Déclencheur | Approbation | URL | Données |
|---|---|---|---|---|
| dev | push sur branche | non | | synthétiques |
| staging | merge sur main | non | | anonymisées |
| prod | tag / release | oui | | réelles |

## 3. Étapes du pipeline
| Étape | Outil | Bloquante | Seuil |
|---|---|---|---|
| Build | | oui | 0 erreur |
| Tests unitaires | | oui | 100 % verts, couverture ≥ __ % |
| SAST | | oui | 0 critique/haute |
| SCA | | oui | 0 critique |
| Secrets scan | | oui | 0 |
| Scan d'image | | oui | 0 critique |

## 4. Stratégie de déploiement et rollback
<rolling / blue-green / canary ; critères de rollback automatique>

## 5. Gestion des secrets et de la configuration

## 6. Métriques DORA — cibles
| Métrique | Cible |
|---|---|
| Fréquence de déploiement | |
| Lead time des changements | |
| Taux d'échec des changements | |
| Temps de restauration | |
