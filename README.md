# Veilla — Logiciel métier SEPP

Logiciel métier pour un Service Externe de Prévention et de Protection au travail (SEPP) agréé en Belgique : affiliés, surveillance de la santé, gestion des risques (sécurité, ergonomie, hygiène), aspects psychosociaux, planification, unités de prévention, portails employeur et travailleur, reporting.

- Cahier des charges : [docs/cahier-des-charges-sepp.pdf](docs/cahier-des-charges-sepp.pdf) (v1.1)
- Architecture cible : microservices DDD en clean architecture, trois zones de sensibilité (standard, médicale, psychosociale), conteneurs sur Azure (§14).
- Contribuer : [CONTRIBUTING.md](CONTRIBUTING.md) (branches, commits, PR, protection de `main`) ; sécurité : [SECURITY.md](SECURITY.md).

## Organisation du suivi

- **Milestones** : Lot 0 (fondations techniques) puis Lots 1 à 5 du §16.1, plus un jalon transverse (conformité, recette, mise en service).
- **Epics** : un par microservice (§14.3) ; chaque exigence du cahier des charges (AFF-01, SAN-10, …) est une sous-issue de l'epic de son service propriétaire (matrice de traçabilité ARC-09).
- **Labels** : `type:*`, `svc:*`, `zone:medicale`, `zone:psychosociale`.
