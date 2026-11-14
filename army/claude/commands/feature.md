---
description: Livre une fonctionnalité (story prête → conception → développement → revue → QA → doc)
argument-hint: <US-NNN ou description de la fonctionnalité>
---
Suis le workflow `army/workflows/02-livraison-feature.md` pour :

$ARGUMENTS

Consignes :
- Si l'argument est un identifiant de story, lis `docs/01-produit/stories/` ; sinon fais d'abord formaliser la story par `product-owner`.
- Détermine la stack concernée à partir du code et du `CLAUDE.md` pour choisir `software-architect-<stack>` et `developer-<stack>`.
- Boucle développement ↔ `code-reviewer` jusqu'à l'absence de remarque P0/P1 (3 itérations maximum, puis escalade).
- Ne commite et ne pousse rien sans demande explicite.
- Termine par une synthèse : fichiers modifiés, tests ajoutés et leur résultat, points ouverts.
