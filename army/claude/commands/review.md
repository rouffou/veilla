---
description: Revue de code priorisée (P0-P3) de la branche courante, d'une branche ou de fichiers
argument-hint: [branche, plage de commits ou fichiers — par défaut les changements en cours]
---
Délègue à l'agent `code-reviewer` la revue de : $ARGUMENTS

Si aucun argument : revoir les changements non commités (`git diff` et `git diff --staged`), ou à défaut la branche courante par rapport à la branche principale.

Consignes :
- Le relecteur identifie la stack et applique `army/stacks/<stack>/STANDARDS.md`.
- Si le diff touche l'authentification, la cryptographie, les uploads, la désérialisation ou des données personnelles, ajoute une revue en parallèle par `security-architect`.
- Le rapport va dans `docs/03-qualite/reviews/`.
- Aucune modification de code sans ma demande.
- Termine par le verdict et la liste des points bloquants.
