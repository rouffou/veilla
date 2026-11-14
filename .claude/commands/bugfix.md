---
description: Reproduit un bug par un test, le corrige, fait relire et vérifie
argument-hint: <description du bug, message d'erreur, étapes>
---
Corrige le bug suivant en suivant la variante « correctif de bug » de `army/workflows/02-livraison-feature.md` :

$ARGUMENTS

Étapes :
1. `qa-engineer` : reproduire le bug et écrire un test automatisé qui échoue (rapport `docs/03-qualite/bugs/` si le bug n'est pas trivial).
2. `developer-<stack>` : identifier la cause racine (pas seulement le symptôme) et corriger avec le changement minimal ; le test doit passer.
3. `code-reviewer` : revue ciblée correction + régressions.
4. `qa-engineer` : vérifier la correction et l'absence de régression.

Termine par : cause racine, correctif, test ajouté, risque résiduel.
