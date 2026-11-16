#!/usr/bin/env bash
# Exige, pour chaque environnement dont les clés de chiffrement changent, une exécution RÉUSSIE de
# verification-cles.yml, lancée à la main sur main pour cet environnement, datant de moins de 24 h (#474, décision D5).
# Lit seulement l'API GitHub (GITHUB_TOKEN avec « actions: read ») : aucun accès Azure, aucun secret, aucun environnement.
# Entrées (variables d'environnement) :
#   ENVIRONNEMENTS  environnements à contrôler, séparés par des espaces ou des retours à la ligne (vide = rien à faire)
#   GITHUB_REPOSITORY  dépôt « propriétaire/nom » ; GH_TOKEN  jeton de lecture
#   AGE_MAX_S       ancienneté maximale en secondes (défaut 86400)
#   MAINTENANT_EPOCH  date courante en secondes epoch (tests)
# Comment une exécution retrouve son environnement : verification-cles.yml a un run-name « Vérification des clés : <env> »,
# repris ici comme titre d'affichage. Une exécution est retenue seulement si elle vient de la branche main, de
# l'évènement workflow_dispatch et si sa conclusion est « success ». Les environnements GitHub étant restreints à main,
# une exécution lancée depuis une autre branche ne pourrait de toute façon pas se connecter à Azure.
set -euo pipefail

ENVIRONNEMENTS="${ENVIRONNEMENTS-}"
AGE_MAX_S="${AGE_MAX_S:-86400}"
DEPOT="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY requis}"
RESUME="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
maintenant="${MAINTENANT_EPOCH:-$(date -u +%s)}"

# shellcheck disable=SC2086
set -- $ENVIRONNEMENTS
if [ "$#" -eq 0 ]; then
  echo "Aucune modification de clés de chiffrement : rien à contrôler."
  exit 0
fi
for env in "$@"; do
  [[ "$env" =~ ^(dev|test|recette|preprod|prod)$ ]] || { echo "::error::Environnement inconnu : $env"; exit 1; }
done

executions=$(gh api "repos/${DEPOT}/actions/workflows/verification-cles.yml/runs?branch=main&event=workflow_dispatch&status=success&per_page=100")

{ echo "### Contrôle des clés de chiffrement"; echo; } >>"$RESUME"
echecs=()
for env in "$@"; do
  trouvee=$(printf '%s' "$executions" | jq -r --arg titre "Vérification des clés : ${env}" --argjson maintenant "$maintenant" --argjson age "$AGE_MAX_S" '
    [ .workflow_runs[]
      | select(.head_branch == "main" and .event == "workflow_dispatch" and .conclusion == "success" and .display_title == $titre)
      | select(($maintenant - (.updated_at | fromdateiso8601)) <= $age) ]
    | sort_by(.updated_at) | last | if . == null then "" else "\(.id) \(.updated_at) \(.html_url)" end')
  if [ -n "$trouvee" ]; then
    echo "- ${env} : vérification réussie, exécution ${trouvee}" | tee -a "$RESUME"
  else
    echo "- ${env} : **aucune vérification réussie sur main depuis moins de $((AGE_MAX_S / 3600)) h**. Lancer : \`gh workflow run verification-cles.yml --ref main -f environnement=${env}\`" | tee -a "$RESUME"
    echecs+=("$env")
  fi
done

if [ "${#echecs[@]}" -gt 0 ]; then
  echo "::error::Clés de chiffrement modifiées sans vérification récente pour : ${echecs[*]}. Lancer verification-cles.yml sur main (approbation de l'environnement), puis relancer ce contrôle."
  exit 1
fi
