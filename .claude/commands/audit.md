---
description: Audite une application existante (code, sécurité, données, CI/CD) et propose une trajectoire de modernisation
argument-hint: [périmètre : dossier, service ou objectif, ex. "migration vers Azure"]
---
Réalise l'audit et la proposition de trajectoire décrits dans `army/workflows/04-modernisation-legacy.md`.

Périmètre / objectif : $ARGUMENTS
(si vide : tout le dépôt, objectif = état des lieux et recommandations)

Consignes :
- Détecte les stacks présentes (fichiers `.csproj`, `pyproject.toml`, `pom.xml`, `build.gradle`, `package.json`…) pour choisir les `software-architect-<stack>`.
- Lance en parallèle les audits de l'étape 1 (architecture logicielle, sécurité, données, DevOps).
- Chaque constat est étayé par un fichier / une ligne ; distingue faits et hypothèses.
- Ne modifie aucun code pendant l'audit.
- Termine par : top 5 des risques, quick wins, trajectoire par étapes, décisions à prendre.
