# ADR 0002 — Monodépôt avec pipelines par service

## Contexte
Le §14.5 décrit la structure du dépôt d'un service et CTR-20 une chaîne CI/CD par service. ARC-24 exige un gabarit commun garantissant l'homogénéité.

## Décision
Un **monodépôt** au démarrage :

```
src/BuildingBlocks/      socle technique partagé (aucune logique métier)
src/Contracts/           contrats d'événements d'intégration versionnés (ARC-34)
src/Services/<Service>/  un dossier autonome par microservice : src/, tests/, Dockerfile (§14.5)
src/Frontends/           workspace Angular
infra/                   Terraform
deploy/                  environnement local Docker Compose (CTR-23)
```

Chaque service conserve son cycle de vie : workflow CI filtré par chemin, image et version propres, base de données propre (ARC-02). Aucune référence de projet entre services : seuls `BuildingBlocks` et `Contracts` sont partagés.

## Alternatives
Un dépôt par service : isolation maximale, mais coût élevé tant que l'équipe est réduite (gabarit dupliqué, montées de version du socle dans une vingtaine de dépôts). Chaque service étant un dossier autonome, l'éclatement reste possible plus tard.

## Conséquences
- Un test d'architecture vérifie qu'aucun service ne dépend d'un autre.
- Le socle partagé doit rester stable et minimal.
