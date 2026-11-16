#!/usr/bin/env bash
# Tests de controler-verification-recente.sh avec une CLI « gh » simulée (réponse de l'API dans $FAKE_EXECUTIONS).
# Usage : bash tools/deploy/tests/controler-verification-recente.test.sh   (nécessite jq)
# shellcheck disable=SC2016,SC2034
set -uo pipefail

racine=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script="$racine/../controler-verification-recente.sh"
travail=$(mktemp -d)
trap 'rm -rf "$travail"' EXIT
mkdir -p "$travail/bin"
printf '#!/usr/bin/env bash\ncat "$FAKE_EXECUTIONS"\n' >"$travail/bin/gh"
chmod +x "$travail/bin/gh"
echecs=0
total=0

# 2026-11-15T12:00:00Z
maintenant=1794744000

# execution <id> <titre> <branche> <évènement> <conclusion> <date> : un élément de workflow_runs.
execution() {
  printf '{"id":%s,"display_title":"%s","head_branch":"%s","event":"%s","conclusion":"%s","updated_at":"%s","html_url":"https://example.invalid/run/%s"}' \
    "$1" "$2" "$3" "$4" "$5" "$6" "$1"
}
api() { printf '{"workflow_runs":[%s]}' "$(IFS=,; echo "$*")" >"$travail/executions.json"; }

executer() {
  sortie=$(env PATH="$travail/bin:$PATH" FAKE_EXECUTIONS="$travail/executions.json" GITHUB_REPOSITORY=o/d \
    GITHUB_STEP_SUMMARY="$travail/resume.md" MAINTENANT_EPOCH="$maintenant" "$@" bash "$script" 2>&1)
  code=$?
}

verifier() {
  local description="$1" condition="$2"
  total=$((total + 1))
  if eval "$condition"; then
    echo "  ok   $description"
  else
    echo "  ECHEC $description (sortie : ${sortie:-})"
    echecs=$((echecs + 1))
  fi
}

recente="2026-11-15T03:00:00Z"
ancienne="2026-11-13T03:00:00Z"
t_dev="Vérification des clés : dev"
t_prod="Vérification des clés : prod"

echo "Rien à contrôler"
api
executer ENVIRONNEMENTS=""
verifier "code 0" '[ "$code" -eq 0 ]'

echo "Vérification récente sur main"
api "$(execution 1 "$t_prod" main workflow_dispatch success "$recente")"
executer ENVIRONNEMENTS="prod"
verifier "prod accepté" '[ "$code" -eq 0 ] && printf "%s" "$sortie" | grep -q "exécution 1 "'

echo "Mauvais environnement"
executer ENVIRONNEMENTS="dev prod"
verifier "dev refusé, commande indiquée" '[ "$code" -eq 1 ] && printf "%s" "$sortie" | grep -q "gh workflow run verification-cles.yml --ref main -f environnement=dev"'

echo "Exécution trop ancienne"
api "$(execution 2 "$t_dev" main workflow_dispatch success "$ancienne")"
executer ENVIRONNEMENTS="dev"
verifier "plus de 24 h : refusé" '[ "$code" -eq 1 ]'

echo "Exécutions à écarter"
api "$(execution 3 "$t_dev" feat/x workflow_dispatch success "$recente")" \
  "$(execution 4 "$t_dev" main pull_request success "$recente")" \
  "$(execution 5 "$t_dev" main workflow_dispatch failure "$recente")"
executer ENVIRONNEMENTS="dev"
verifier "autre branche, autre évènement, échec : refusés" '[ "$code" -eq 1 ]'

echo "Plusieurs environnements, tous vérifiés"
api "$(execution 6 "$t_dev" main workflow_dispatch success "$recente")" "$(execution 7 "$t_prod" main workflow_dispatch success "$recente")"
executer ENVIRONNEMENTS=$'dev\nprod'
verifier "dev et prod acceptés" '[ "$code" -eq 0 ]'

echo "Entrée invalide"
executer ENVIRONNEMENTS="inconnu"
verifier "environnement refusé" '[ "$code" -eq 1 ]'

echo
echo "${total} vérifications, ${echecs} échec(s)"
[ "$echecs" -eq 0 ]
