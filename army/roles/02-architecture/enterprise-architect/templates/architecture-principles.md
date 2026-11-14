---
titre: Principes d'architecture du SI
auteur: enterprise-architect
statut: brouillon
date: AAAA-MM-JJ
---

# Principes d'architecture

> Format par principe : Énoncé · Justification · Implications. À adapter au contexte de l'entreprise.

## P1 — Réutiliser avant d'acheter, acheter avant de construire
- **Justification** : réduire coûts et délais, éviter les redondances.
- **Implications** : toute construction spécifique est justifiée par une analyse Build/Buy/Reuse.

## P2 — Une donnée de référence a un seul système maître
- **Justification** : cohérence et qualité des données.
- **Implications** : les autres systèmes consomment par API ou événements, jamais par copie modifiable.

## P3 — API-first et couplage faible
- **Justification** : évolutivité, remplaçabilité des composants.
- **Implications** : toute capacité exposée l'est via un contrat versionné (OpenAPI / AsyncAPI).

## P4 — Sécurité dès la conception (Secure by design, Zero Trust)
- **Implications** : modèle de menaces obligatoire pour toute nouvelle solution ; authentification centralisée.

## P5 — Cloud-first, managé avant auto-hébergé
- **Implications** : privilégier PaaS/SaaS ; IaaS uniquement sur justification.

## P6 — Tout est code (infrastructure, configuration, politiques)
- **Implications** : aucun changement manuel en production.

## P7 — Observabilité native
- **Implications** : logs structurés, métriques, traces distribuées (OpenTelemetry) dès la v1.

## P8 — Conformité réglementaire by design (RGPD, etc.)
- **Implications** : minimisation des données, registre des traitements, durées de conservation définies.
