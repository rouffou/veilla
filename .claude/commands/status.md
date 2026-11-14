---
description: Produit un point d'avancement du projet (météo, réalisé, à venir, risques, décisions attendues)
argument-hint: [période ou jalon, optionnel]
---
Délègue à l'agent `project-manager` un status report. $ARGUMENTS

Sources à consulter :
- `docs/00-pilotage/` (charte, planning, RAID, status reports précédents)
- `docs/01-produit/` (backlog, stories et leur statut)
- `docs/02-architecture/adr/` (décisions récentes)
- `docs/03-qualite/` (bugs ouverts, revues)
- l'historique git récent (`git log --since`), si le dépôt est versionné

Livrable : `docs/00-pilotage/<date>-status-report.md` selon le template du chef de projet, et mise à jour du registre RAID.
Termine par les 3 décisions ou actions les plus urgentes.
