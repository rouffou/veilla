#!/usr/bin/env bash
# Tests de controler-verification-recente.sh avec une CLI « gh » simulée et un dépôt temporaire.
# Usage : bash tools/deploy/tests/controler-verification-recente.test.sh   (nécessite jq)
# shellcheck disable=SC2016,SC2034
set -uo pipefail

racine=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script="$racine/../controler-verification-recente.sh"
travail=$(mktemp -d)
trap 'rm -rf "$travail"' EXIT
mkdir -p "$travail/bin" "$travail/depot"
# « gh api » simulé : exécutions, déploiements d'un environnement/commit, statuts d'un déploiement.
cat >"$travail/bin/gh" <<'FIN'
#!/usr/bin/env bash
args="$*"
case "$args" in
  *"/runs?"*) cat "$FAKE_EXECUTIONS" ;;
  *"/deployments?environment="*)
    env=${args##*environment=}; env=${env%%&*}
    sha=${args##*sha=}
    cat "$FAKE_DEPLOIEMENTS/${env}-${sha}.json" 2>/dev/null || echo '[]' ;;
  *"/deployments/"*"/statuses"*)
    id=${args##*deployments/}; id=${id%%/*}
    cat "$FAKE_DEPLOIEMENTS/statuts-${id}.json" 2>/dev/null || echo '[]' ;;
  *) echo "gh simulé : appel inattendu : $*" >&2; exit 2 ;;
esac
FIN
chmod +x "$travail/bin/gh"
mkdir -p "$travail/deploiements"
echecs=0
total=0

# Dépôt temporaire : « principal » (ancêtre de la base) et « etrangere » (hors de la base, comme une étiquette forgée).
cd "$travail/depot" || exit 1
d() { git -C "$travail/depot" "$@"; }
d init -q .
d config user.email "test@example.invalid"
d config user.name "Test"
d config commit.gpgsign false
d commit -q --allow-empty -m un
sha_principal=$(d rev-parse HEAD)
d commit -q --allow-empty -m deux
d update-ref refs/remotes/origin/main HEAD
d checkout -q -b etrangere "$sha_principal"
d commit -q --allow-empty -m forge
sha_etranger=$(d rev-parse HEAD)
d checkout -q -
d checkout -q --detach refs/remotes/origin/main

# 2026-11-15T12:00:00Z
maintenant=1794744000

# execution <id> <titre> <branche> <évènement> <conclusion> <date> <sha>
execution() {
  printf '{"id":%s,"display_title":"%s","head_branch":"%s","event":"%s","conclusion":"%s","run_started_at":"%s","updated_at":"2026-01-01T00:00:00Z","head_sha":"%s","html_url":"https://example.invalid/run/%s"}' \
    "$1" "$2" "$3" "$4" "$5" "$6" "$7" "$1"
}
api() { printf '{"workflow_runs":[%s]}' "$(IFS=,; echo "$*")" >"$travail/executions.json"; }
# deploiement <env> <sha> <id> <état> : un déploiement et son dernier statut.
deploiement() {
  printf '[{"id":%s}]' "$3" >"$travail/deploiements/$1-$2.json"
  printf '[{"state":"%s"}]' "$4" >"$travail/deploiements/statuts-$3.json"
}

executer() {
  sortie=$(cd "$travail/depot" && env PATH="$travail/bin:$PATH" FAKE_EXECUTIONS="$travail/executions.json" FAKE_DEPLOIEMENTS="$travail/deploiements" \
    GITHUB_REPOSITORY=o/d GITHUB_STEP_SUMMARY="$travail/resume.md" MAINTENANT_EPOCH="$maintenant" "$@" bash "$script" 2>&1)
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

echo "jq indispensable"
api
executer ENVIRONNEMENTS="dev" JQ=jq-inexistant
verifier "message clair et échec" '[ "$code" -eq 1 ] && printf "%s" "$sortie" | grep -q "jq est introuvable"'

echo "Rien à contrôler"
executer ENVIRONNEMENTS=""
verifier "code 0" '[ "$code" -eq 0 ]'

echo "Vérification récente sur main, déploiement réussi"
api "$(execution 1 "$t_prod" main workflow_dispatch success "$recente" "$sha_principal")"
deploiement prod "$sha_principal" 11 success
executer ENVIRONNEMENTS="prod"
verifier "prod accepté" '[ "$code" -eq 0 ] && printf "%s" "$sortie" | grep -q "exécution 1 "'

echo "Mauvais environnement"
executer ENVIRONNEMENTS="dev prod"
verifier "dev refusé, commande indiquée" '[ "$code" -eq 1 ] && printf "%s" "$sortie" | grep -q "gh workflow run verification-cles.yml --ref main -f environnement=dev"'

echo "Exécution trop ancienne (run_started_at)"
api "$(execution 2 "$t_dev" main workflow_dispatch success "$ancienne" "$sha_principal")"
deploiement dev "$sha_principal" 12 success
executer ENVIRONNEMENTS="dev"
verifier "plus de 24 h : refusé" '[ "$code" -eq 1 ]'

echo "Exécutions à écarter"
api "$(execution 3 "$t_dev" feat/x workflow_dispatch success "$recente" "$sha_principal")" \
  "$(execution 4 "$t_dev" main pull_request success "$recente" "$sha_principal")" \
  "$(execution 5 "$t_dev" main workflow_dispatch failure "$recente" "$sha_principal")"
executer ENVIRONNEMENTS="dev"
verifier "autre branche, autre évènement, échec : refusés" '[ "$code" -eq 1 ]'

echo "Défense (a) : commit hors de l'historique de main (étiquette forgée)"
api "$(execution 6 "$t_dev" main workflow_dispatch success "$recente" "$sha_etranger")"
deploiement dev "$sha_etranger" 13 success
executer ENVIRONNEMENTS="dev"
verifier "non ancêtre : refusé" '[ "$code" -eq 1 ] && grep -q "n.est pas un ancêtre" "$travail/resume.md"'

echo "Défense (b) : déploiement GitHub"
api "$(execution 7 "$t_dev" main workflow_dispatch success "$recente" "$sha_principal")"
rm -f "$travail"/deploiements/*
executer ENVIRONNEMENTS="dev"
verifier "aucun déploiement : refusé" '[ "$code" -eq 1 ] && grep -q "aucun déploiement GitHub réussi" "$travail/resume.md"'
deploiement dev "$sha_principal" 14 failure
executer ENVIRONNEMENTS="dev"
verifier "déploiement en échec : refusé" '[ "$code" -eq 1 ]'
deploiement dev "$sha_principal" 15 success
executer ENVIRONNEMENTS="dev"
verifier "déploiement réussi : accepté" '[ "$code" -eq 0 ]'

echo "Plusieurs environnements, tous vérifiés"
api "$(execution 8 "$t_dev" main workflow_dispatch success "$recente" "$sha_principal")" "$(execution 9 "$t_prod" main workflow_dispatch success "$recente" "$sha_principal")"
deploiement dev "$sha_principal" 16 success
deploiement prod "$sha_principal" 17 success
executer ENVIRONNEMENTS=$'dev\nprod'
verifier "dev et prod acceptés" '[ "$code" -eq 0 ]'

echo "Entrée invalide"
executer ENVIRONNEMENTS="inconnu"
verifier "environnement refusé" '[ "$code" -eq 1 ]'

echo
echo "${total} vérifications, ${echecs} échec(s)"
[ "$echecs" -eq 0 ]
