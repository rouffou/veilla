# Génération du backlog GitHub

Scripts qui créent le backlog GitHub à partir du cahier des charges SEPP (v1.1) : labels, milestones, epics, une issue par exigence, sous-issues et Project.

| Fichier | Rôle |
|---|---|
| `data.py` | Exigences du cahier des charges (texte, section), service propriétaire, lot, labels, schémas de données |
| `create.py` | Crée les éléments sur GitHub via l'API REST/GraphQL (jeton de `gh auth token`) |
| `state.json` | Correspondance identifiant d'exigence → numéro d'issue, sous-issues et éléments du Project déjà créés |

## Utilisation

```bash
cd tools/github-backlog
PYTHONIOENCODING=utf-8 python create.py rouffou/veilla
```

Le script est idempotent : tout ce qui figure dans `state.json` est ignoré. Pour une nouvelle version du cahier des charges :

1. ajouter les nouvelles exigences dans `REQS_RAW` (`ID|section|titre|texte`) et leur service dans `OWNER` ;
2. ajuster le lot si nécessaire (`_lot(...)`) ;
3. relancer le script, puis committer le `state.json` mis à jour.

Limite : le script ne met pas à jour les issues existantes (texte modifié d'une exigence) ; ces changements se font à la main ou via une évolution du script.
