# Conventions communes à TOUS les agents

> Ce fichier est lu par chaque agent avant toute action. Il prime sur les préférences individuelles, mais pas sur une instruction explicite de l'utilisateur.

## 1. Principes de travail
1. **Rester dans son périmètre.** Chaque `ROLE.md` contient une section « Hors périmètre ». Si une demande en relève, ne la traite pas : signale-la dans le HANDOFF et nomme l'agent compétent.
2. **Ne rien inventer.** Toute information manquante devient soit une **question** (bloquante), soit une **hypothèse** explicite (non bloquante), listée dans le livrable sous `## Hypothèses`.
3. **Tracer les décisions.** Toute décision structurante (techno, pattern, découpage, compromis) fait l'objet d'un ADR (`army/shared/templates/adr.md`).
4. **Préférer le simple.** La solution la plus simple qui satisfait les exigences (fonctionnelles + non fonctionnelles) gagne. Toute complexité ajoutée doit être justifiée.
5. **Lire avant d'écrire.** Explorer le code et la doc existants avant de proposer quoi que ce soit. Réutiliser l'existant plutôt que dupliquer.
6. **Livrables vérifiables.** Un livrable = un fichier dans `docs/` (ou du code + tests). Pas de livrable « uniquement dans la conversation ».

## 2. Langue et style
- Langue des livrables : **français** (code, identifiants et messages de commit en **anglais**).
- Style : phrases courtes, listes, tableaux. Pas de remplissage.
- Chaque livrable commence par un en-tête :
  ```
  ---
  titre: <titre>
  auteur: <nom-de-l-agent>
  statut: brouillon | en revue | validé
  date: AAAA-MM-JJ
  ---
  ```

## 3. Où écrire les livrables
```
docs/
├── 00-pilotage/        charte, planning, RAID, comptes rendus, status reports
├── 01-produit/         PRD, épiques, user stories, specs fonctionnelles
├── 02-architecture/
│   ├── entreprise/     cartographie des capacités, principes, roadmap SI
│   ├── solution/       dossiers d'architecture solution (SAD), NFR
│   ├── logicielle/     conception logicielle par composant
│   ├── securite/       modèles de menaces, exigences sécurité
│   ├── donnees/        modèles de données, flux, gouvernance
│   └── adr/            ADR-NNNN-titre-court.md (numérotation continue)
├── 03-qualite/         plans de test, rapports de revue, bugs
└── 04-operations/      pipelines, infra, runbooks, SLO, post-mortems
```
- Nom de fichier : `kebab-case.md`, préfixé par la date si c'est un document daté (`2026-11-13-status-report.md`).

## 4. Niveaux de priorité / sévérité (communs)
| Niveau | Sens |
|---|---|
| **P0 / Bloquant** | Empêche la livraison ou met la prod / la sécurité en danger |
| **P1 / Majeur** | Doit être traité avant la fin de l'itération |
| **P2 / Mineur** | À planifier |
| **P3 / Suggestion** | Amélioration optionnelle |

## 5. Escalade
- Conflit entre deux agents → l'**orchestrateur** tranche en s'appuyant sur `raci.md` ; à défaut, il pose la question à l'utilisateur.
- Conflit architecture ↔ délai/coût → arbitrage **chef de projet** + **architecte solution**, décision tracée en ADR.
- Toute action irréversible (suppression de données, déploiement en production, dépense cloud) → **confirmation humaine obligatoire**.

## 6. Fin de chaque intervention
Chaque agent termine **toujours** par un bloc HANDOFF au format défini dans `handoff.md`.
