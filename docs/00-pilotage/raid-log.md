---
titre: Registre RAID — veilla (logiciel métier SEPP)
auteur: project-manager
statut: en revue
date: 2026-11-14
---

# Registre RAID

Source : l'état des lieux du 2026-11-14 (`docs/00-pilotage/etat-des-lieux-backlog.md`), à partir des données GitHub extraites en lecture seule.
Criticité = probabilité × impact (échelle 1 à 5). Les probabilités et impacts sont une évaluation du chef de projet, à confirmer en revue.

## Décisions attendues du responsable produit (liste courte)

| ID | Question | Tickets | Effet si non tranchée |
|---|---|---|---|
| DA-01 | Scinder le Lot 0 : les tickets qui ne se ferment qu'au déploiement Azure passent dans un jalon « Mise en service Azure » ? | #282, #158, #204, #206, #208, #214, #236, #399 | le Lot 0 reste ouvert, et la règle « lots dans l'ordre » bloque les Lots 1 et suivants |
| DA-02 | Valider le chemin critique et les P0 et P1 de la démo, et autoriser la création des tickets A à H | #389 | écrans de démo non découpés, donc pas de projection possible |
| DA-03 | Trancher les 5 écarts de droits entre le cahier des charges et le code | #401 | droits de l'infirmier et du CPMT dirigeant incertains dans la démo ; #260 bloqué |
| DA-04 | Délais légaux initiaux : validation par le département médical, ou acceptation provisoire pour la démo | #404 (#53, #59) | valeurs non validées affichées en démo |
| DA-05 | Incapacités : quelle source fait foi (manuel ou eHealth), quelle tolérance sur la date de début | #385 | doublons de dossiers dans le parcours E |
| DA-06 | Fermer les 5 schémas initiaux et les 6 épiques soldés, après vérification | #269, #270, #271, #276, #277 ; #2, #3, #4, #11, #14, #15 | avancement sous-estimé, bruit dans le backlog |
| DA-07 | Appliquer l'organisation du board proposée (champs, vues, commandes de la section 6) | board 7 | pilotage sans priorité ni blocage visibles |

## Risques

| ID | Description | Probabilité (1-5) | Impact (1-5) | Criticité | Mitigation | Propriétaire | Statut |
|---|---|---|---|---|---|---|---|
| R-001 | Le Lot 0 ne peut pas être fermé sans le premier déploiement Azure (7 tickets dépendent de #282). Appliquée strictement, la règle des lots bloque toute la suite. | 5 | 4 | 20 | DA-01 : sortir ces tickets dans un jalon « Mise en service Azure » | responsable produit | ouvert |
| R-002 | Démo non prête : les 8 modules de routes de `app-interne` sont des pages provisoires, et les tickets des vagues 1 et 2 de #389 n'existent pas. | 5 | 5 | 25 | DA-02, puis créer et dimensionner les tickets A à H ; #393 en P0 | orchestrateur | ouvert |
| R-003 | Rythme en rafales et contributeur unique : 4 semaines sur 7 sans fermeture ; 303 des 331 commentaires d'un seul compte. | 4 | 4 | 16 | fixer une date cible de démo et un point hebdomadaire ; découper les tickets à la taille S ou M | responsable produit | ouvert |
| R-004 | Mode `Enforce` de l'authentification forte : le front ne gère pas encore le 401 `insufficient_user_authentication`, ce qui casse les parcours du dossier de santé si `Enforce` est activé. | 3 | 4 | 12 | P0 sur le reste de #156 ; démo en mode actuel tant que ce n'est pas livré | orchestrateur | ouvert |
| R-005 | Décisions métier en attente depuis le 2026-10-25 (#401, #404, #385) : écrans et règles de démo potentiellement faux. | 4 | 3 | 12 | DA-03, DA-04, DA-05 | responsable produit | ouvert |
| R-006 | Dépendances externes (CSAM, eHealth, formats BCE/DIMONA, prestataires de recommandé, SMS et eBox, signature qualifiée) : les Lots 1 à 3 ne peuvent pas être fermés à 100 %. | 5 | 3 | 15 | champ Blocage = « Prestataire externe » ; garder les simulateurs pour la démo ; fermer les tickets avec un ticket de suite par dépendance | responsable produit | ouvert |
| R-007 | Board non fiable : 13 issues absentes (dont l'épique démo #389), aucune priorité, statuts en retard sur les commentaires. | 5 | 3 | 15 | DA-07 ; activer l'ajout automatique ; contrôle hebdomadaire (section 6.6) | orchestrateur | ouvert |
| R-008 | Projection non fiable : tickets de taille hétérogène, aucun champ taille, 53 fermetures à l'import le 2026-09-28. | 5 | 2 | 10 | champ Taille ; mesurer la vitesse en taille sur 2 semaines après la création des tickets A à H | chef de projet | ouvert |
| R-009 | Règle des lots déjà enfreinte : Lots 1 à 3 avancés de 57 à 80 % avant la fermeture du Lot 0. La règle n'est plus un repère fiable. | 5 | 2 | 10 | DA-01, puis appliquer la règle au Lot 0 local | responsable produit | ouvert |
| R-010 | PR en attente : 7 PR ouvertes, dont 3 fonctionnelles (#429, #430, #433) et 4 Dependabot. | 3 | 2 | 6 | revue et fusion par l'orchestrateur une fois la CI verte | orchestrateur | ouvert |

## Actions

| ID | Action | Propriétaire | Échéance | Statut |
|---|---|---|---|---|
| A-001 | Faire trancher DA-01 à DA-07 | responsable produit | à fixer par le responsable produit | à faire |
| A-002 | Finaliser les PR #429, #433 et #430 (revue, CI, fusion) | orchestrateur | avant le lancement de la vague 1 de la démo | à faire |
| A-003 | Créer et dimensionner les tickets A, B, C, D, F (vague 1) puis E, H, vérification, script et présentation (vague 2) de #389 | orchestrateur, avec `product-owner` | après DA-02 | à faire |
| A-004 | Lancer #393 (données de démo) : sa dépendance #391 est fermée | orchestrateur | après A-002 (#429) | à faire |
| A-005 | Traiter le reste de #156 côté front (401 vers `acr_values=mfa`) | orchestrateur, développeur front | avant la démo | à faire |
| A-006 | Appliquer les commandes de la section 6 de l'état des lieux | responsable produit (exécution) | après DA-07 | à faire |
| A-007 | Revérifier #48 et #132 après #323 et #429 ; les fermer ou les mettre à jour | orchestrateur | avant le parcours B | à faire |
| A-008 | Planifier le premier déploiement Azure (#282) : abonnements, OIDC, runner VNet, Entra | responsable produit, `devops` | hors chemin critique de la démo | à faire |
| A-009 | Fournir les 12 informations contractuelles de #165 | responsable produit | avant la mise en service | à faire |
| A-010 | Fixer une date cible de démo et des échéances sur les jalons (aucune aujourd'hui) | responsable produit | au prochain point | à faire |

## Issues (problèmes avérés)

| ID | Description | Impact | Sévérité (P0-P3) | Propriétaire | Résolution | Statut |
|---|---|---|---|---|---|---|
| I-001 | L'application interne n'a aucun écran métier (8 modules de routes en page provisoire). | démo impossible en l'état | P0 | orchestrateur | A-003, A-004 | ouvert |
| I-002 | Les tickets de démo des vagues 1 et 2 annoncés le 2026-10-25 n'ont pas été créés. | pas de suivi ni de projection | P0 | orchestrateur | A-003 | ouvert |
| I-003 | 13 issues absentes du board, dont #389 à #393, #401 et #404. | pilotage aveugle sur la démo et les décisions | P1 | orchestrateur | section 6.2 | ouvert |
| I-004 | #111 fermé mais en `Todo` ; #165 en `In Progress` alors qu'il attend des informations contractuelles ; #152, #156, #208 et #369 « en cours » dans les commentaires mais en `Todo`. | statuts faux | P2 | orchestrateur | sections 6.2 et 6.4 | ouvert |
| I-005 | 5 issues sans jalon (#377, #385, #386, #399, #428) ; 2 sans aucun label (#399, #428). | traçabilité par lot | P2 | orchestrateur | section 6.5 | ouvert |
| I-006 | Restes à faire suivis en double dans plusieurs tickets (APIM, CSAM, clés, PRA, purge, droits). | double comptage, risque d'oubli | P2 | chef de projet | 1.4 de l'état des lieux ; à nettoyer à la clôture de chaque ticket | ouvert |
| I-007 | Aucune fermeture d'issue entre le 2026-10-26 et le 2026-11-14. | rythme de livraison | P1 | responsable produit | R-003, A-010 | ouvert |

## Décisions

Décisions déjà prises et tracées dans le dépôt.

| ID | Décision | Date | Décideur | Lien ADR |
|---|---|---|---|---|
| D-001 | Démo commerciale pleinement opérationnelle, en local (Docker compose), comme objectif à court terme | 2026-10-25 | responsable produit | épique #389 |
| D-002 | Bus d'événements de développement PostgreSQL plutôt que l'émulateur Azure Service Bus (licences de l'émulateur et de SQL Edge) | 2026-10-25 | orchestrateur, commentaire de #389 | — (PR #398) |
| D-003 | BFF interne : relais vers les services et écrans composites | 2026-10-25 | architecture | `docs/adr/0013-bff-interne-relais-ecrans-composites.md` |
| D-004 | Chiffrement des champs par enveloppe, clé racine non exportable | 2026-10-25 | architecture (validation de l'architecte demandée dans #282) | `docs/adr/0012-chiffrement-champs-enveloppe.md` |
| D-005 | Rattrapage des projections par republication des producteurs | 2026-10-25 | architecture | `docs/adr/0011-rattrapage-projections.md` |
| D-006 | Ne pas proposer PDFsharp >= 6.5, qui impose une licence | 2026-11-14 (commit 7440ca1) | orchestrateur | — (PR #426) |
| D-007 | Les lots se traitent dans l'ordre (Lot 0, puis 1, 2…) | — | responsable produit | `CLAUDE.md` (révision proposée : DA-01) |
