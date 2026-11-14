---
description: Gère un incident de production (qualification, atténuation, correctif, post-mortem)
argument-hint: <symptômes, alertes, depuis quand, impact constaté>
---
Gère l'incident suivant en suivant `army/workflows/03-incident-production.md` :

$ARGUMENTS

Règles impératives :
- Commence par l'agent `sre` : sévérité, impact utilisateur, runbook applicable (`docs/04-operations/runbooks/`).
- Atténuer d'abord (rollback, feature flag, bascule), diagnostiquer ensuite.
- **Aucune action en production sans ma confirmation explicite**, même proposée par un runbook.
- Donne un point de situation court après chaque étape.
- Une fois le service rétabli, fais rédiger le post-mortem par `sre` dans `docs/04-operations/postmortems/`.
