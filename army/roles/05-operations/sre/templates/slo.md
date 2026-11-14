---
titre: SLO — <Service>
auteur: sre
statut: brouillon
date: AAAA-MM-JJ
---

# SLO — <Service>

| Parcours utilisateur | SLI (définition précise) | SLO | Fenêtre | Budget d'erreur | Source de mesure |
|---|---|---|---|---|---|
| Consulter <X> | % de requêtes HTTP non-5xx | 99,9 % | 30 jours glissants | 43 min | |
| Consulter <X> | % de requêtes < 300 ms | 95 % | 30 jours | | |

## Politique de budget d'erreur
- Budget > 50 % : livraison normale.
- Budget < 25 % : priorité aux actions de fiabilité.
- Budget épuisé : gel des déploiements non correctifs (sauf accord PO).

## Alertes (burn rate)
| Alerte | Condition | Sévérité | Runbook |
|---|---|---|---|
| Consommation rapide | burn rate > 14,4 sur 1 h | page | |
| Consommation lente | burn rate > 6 sur 6 h | ticket | |
