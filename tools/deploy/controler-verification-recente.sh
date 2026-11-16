#!/usr/bin/env bash
# Exige, pour chaque environnement dont les clés de chiffrement changent, une exécution RÉUSSIE de
# verification-cles.yml, lancée à la main sur main pour cet environnement, datant de moins de 24 h (#474, décision D5).
# Lit seulement l'API GitHub (GITHUB_TOKEN avec « actions: read ») et l'historique git : aucun accès Azure, aucun secret,
# aucun environnement. À exécuter dans un dépôt cloné en historique complet (refs/remotes/origin/main présent).
# Entrées (variables d'environnement) :
#   ENVIRONNEMENTS  environnements à contrôler, séparés par des espaces ou des retours à la ligne (vide = rien à faire)
#   GITHUB_REPOSITORY  dépôt « propriétaire/nom » ; GH_TOKEN  jeton de lecture
#   AGE_MAX_S       ancienneté maximale en secondes (défaut 86400)
#   REF_BASE        référence de main (défaut refs/remotes/origin/main, jamais un nom court qu'une étiquette pourrait usurper)
#   JQ              commande jq (défaut jq)
#   MAINTENANT_EPOCH  date courante en secondes epoch (tests)
# Comment une exécution retrouve son environnement : verification-cles.yml a un run-name « Vérification des clés : <env> »,
# repris ici comme titre d'affichage. Une exécution est retenue seulement si :
#   - elle vient de la branche main, de l'évènement workflow_dispatch, avec la conclusion « success » ;
#   - sa date de DÉMARRAGE (run_started_at) a moins de 24 h. Re-lancer une ancienne exécution (Re-run) fait repartir
#     run_started_at : c'est alors une vraie nouvelle exécution des jobs verif-<service> (état actuel de la base), mais avec
#     le code de workflow du commit d'origine ; la défense (a) ci-dessous borne ce risque ;
#   - (a, défense en profondeur contre une étiquette ou une branche « main » forgée) son head_sha est un ancêtre de REF_BASE ;
#   - (b) un déploiement GitHub réussi existe dans l'environnement pour ce head_sha : un job qui référence un
#     environnement crée un déploiement GitHub, dont le statut passe à success quand le job réussit. À défaut de cette
#     preuve, la vérification n'est pas retenue. Limite : un déploiement de deploy.yml dans le même environnement au même
#     commit ne se distingue pas de celui de verification-cles.yml ; (a) et le titre de l'exécution portent le reste.
set -euo pipefail

ENVIRONNEMENTS="${ENVIRONNEMENTS-}"
AGE_MAX_S="${AGE_MAX_S:-86400}"
REF_BASE="${REF_BASE:-refs/remotes/origin/main}"
JQ="${JQ:-jq}"
DEPOT="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY requis}"
RESUME="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
maintenant="${MAINTENANT_EPOCH:-$(date -u +%s)}"

command -v "$JQ" >/dev/null 2>&1 || {
  echo "::error::jq est introuvable ('${JQ}') : il est indispensable au contrôle des clés (installer jq, présent sur ubuntu-latest)."
  exit 1
}

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

# Vrai si un déploiement GitHub réussi existe dans l'environnement $1 pour le commit $2.
deploiement_reussi() {
  local env="$1" sha="$2" deploiements id
  deploiements=$(gh api "repos/${DEPOT}/deployments?environment=${env}&sha=${sha}")
  for id in $(printf '%s' "$deploiements" | "$JQ" -r '.[].id'); do
    # Statuts du plus récent au plus ancien : le premier fait foi.
    if [ "$(gh api "repos/${DEPOT}/deployments/${id}/statuses" | "$JQ" -r '.[0].state // ""')" = "success" ]; then
      return 0
    fi
  done
  return 1
}

{ echo "### Contrôle des clés de chiffrement"; echo; } >>"$RESUME"
echecs=()
for env in "$@"; do
  # shellcheck disable=SC2016  # filtre jq : les $ sont des variables jq
  candidates=$(printf '%s' "$executions" | "$JQ" -r --arg titre "Vérification des clés : ${env}" --argjson maintenant "$maintenant" --argjson age "$AGE_MAX_S" '
    [ .workflow_runs[]
      | select(.head_branch == "main" and .event == "workflow_dispatch" and .conclusion == "success" and .display_title == $titre)
      | select(($maintenant - (.run_started_at | fromdateiso8601)) <= $age) ]
    | sort_by(.run_started_at) | reverse | .[] | "\(.id) \(.head_sha) \(.run_started_at) \(.html_url)"')
  trouvee=""
  while read -r id sha demarre url; do
    [ -n "${id:-}" ] || continue
    if ! git merge-base --is-ancestor "$sha" "$REF_BASE" 2>/dev/null; then
      echo "  exécution ${id} écartée : ${sha} n'est pas un ancêtre de ${REF_BASE}." >>"$RESUME"
      continue
    fi
    if ! deploiement_reussi "$env" "$sha"; then
      echo "  exécution ${id} écartée : aucun déploiement GitHub réussi dans ${env} pour ${sha}." >>"$RESUME"
      continue
    fi
    trouvee="${id} (démarrée ${demarre}, ${url})"
    break
  done <<<"$candidates"
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
