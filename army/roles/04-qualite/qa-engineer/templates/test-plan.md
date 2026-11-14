---
titre: Plan de test — <Release / Feature>
auteur: qa-engineer
statut: brouillon
date: AAAA-MM-JJ
---

# Plan de test — <Release>

## 1. Périmètre
- Testé :
- Non testé (et pourquoi) :

## 2. Stratégie
| Niveau | Part visée | Responsable | Outil |
|---|---|---|---|
| Unitaire | ~70 % | developer-* | |
| Intégration / API / contrat | ~20 % | developer-* + qa | |
| E2E | ~10 % (parcours critiques) | qa | Playwright |
| Non fonctionnel (charge, sécu, a11y) | selon NFR | qa + sre + security | k6, ZAP, axe |

## 3. Matrice de traçabilité
| Story | Critère / RG | Cas de test | Niveau | Automatisé | Statut |
|---|---|---|---|---|---|

## 4. Environnements et données

## 5. Critères d'entrée / de sortie
- Entrée : build déployé, tests unitaires verts
- Sortie : 0 bug P0/P1 ouvert, 100 % des critères d'acceptation passés, NFR vérifiées

## 6. Risques qualité
