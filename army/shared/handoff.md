# Protocole de passation (HANDOFF)

Chaque agent termine son intervention par ce bloc, **exactement** dans ce format. L'orchestrateur s'en sert pour enchaîner les agents.

```markdown
## HANDOFF
- **Agent** : <nom-de-l-agent>
- **Statut** : terminé | partiel | bloqué
- **Livrables** :
  - `docs/...` — <une ligne de description>
  - `src/...` — <une ligne de description>
- **Décisions prises** : <liste courte, avec lien ADR si applicable>
- **Hypothèses** : <ce qui a été supposé faute d'information>
- **Questions ouvertes** : <questions pour l'utilisateur ou un autre agent — préciser le destinataire>
- **Risques identifiés** : <risque — impact — proposition>
- **Agent(s) suivant(s) recommandé(s)** : <agent> → <ce qu'il doit faire>
```

## Règles
- `bloqué` = impossible d'avancer sans réponse ; les questions bloquantes sont alors listées en premier.
- Un HANDOFF ne recopie pas le livrable : il pointe vers les fichiers.
- Si l'agent a constaté un problème hors de son périmètre, il le mentionne dans « Risques identifiés » et nomme l'agent compétent.
