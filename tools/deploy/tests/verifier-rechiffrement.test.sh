#!/usr/bin/env bash
# Tests de verifier-rechiffrement.sh avec une CLI « az » simulée (tools/deploy/tests/az-simule.sh).
# Usage : bash tools/deploy/tests/verifier-rechiffrement.test.sh
# shellcheck disable=SC2016,SC2034
set -uo pipefail

racine=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script="$racine/../verifier-rechiffrement.sh"
travail=$(mktemp -d)
trap 'rm -rf "$travail"' EXIT
mkdir -p "$travail/bin"
cp "$racine/az-simule.sh" "$travail/bin/az"
chmod +x "$travail/bin/az"

echecs=0
total=0

executer() {
  AZ_JOURNAL="$travail/az.log"
  : >"$AZ_JOURNAL"
  : >"$travail/resume.md"
  sortie=$(env PATH="$travail/bin:$PATH" AZ_JOURNAL="$AZ_JOURNAL" GITHUB_STEP_SUMMARY="$travail/resume.md" \
    ENVIRONNEMENT=dev SERVICES="personnes integrations" INTERVALLE_S=0 DELAI_S=0 \
    FAKE_JOBS_CLES="verif-personnes verif-integrations" "$@" bash "$script" 2>&1)
  code=$?
}

verifier() {
  local description="$1" condition="$2"
  total=$((total + 1))
  if eval "$condition"; then
    echo "  ok   $description"
  else
    echo "  ECHEC $description"
    echecs=$((echecs + 1))
  fi
}

appel() { grep -c -- "$1" "$travail/az.log" || true; }

echo "Tous les jobs renvoient 0"
executer
verifier "code 0" '[ "$code" -eq 0 ]'
verifier "un job démarré par service" '[ "$(appel "job start")" -eq 2 ]'
verifier "jamais rech-" '! grep -q -- "rech-" "$travail/az.log"'

echo "Un job échoue : l'étape échoue, les autres services sont tout de même vérifiés"
executer FAKE_VERIF_STATUT=Failed
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "les deux services sont lancés" '[ "$(appel "job start")" -eq 2 ]'
verifier "échec signalé" 'grep -q "échec" "$travail/resume.md"'

echo "Job absent : non vérifié donc échec"
executer FAKE_JOBS_CLES="verif-personnes"
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "un seul job démarré" '[ "$(appel "job start")" -eq 1 ]'

echo "Exécution déjà en cours : rien n'est lancé"
executer FAKE_EN_COURS=verif-personnes-abc
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "aucun démarrage" '[ "$(appel "job start")" -eq 0 ]'

echo "Heures creuses en production"
executer ENVIRONNEMENT=prod HEURE_UTC=11 FAKE_GROUPE_JOB=rg-sepp-prod-standard
verifier "refusé en journée" '[ "$code" -eq 1 ] && [ "$(appel "job start")" -eq 0 ]'
executer ENVIRONNEMENT=prod HEURE_UTC=22 FAKE_GROUPE_JOB=rg-sepp-prod-standard
verifier "accepté la nuit" '[ "$code" -eq 0 ]'
executer ENVIRONNEMENT=prod HEURE_UTC=05 FAKE_GROUPE_JOB=rg-sepp-prod-standard
verifier "5 h UTC refusé" '[ "$code" -eq 1 ]'
executer ENVIRONNEMENT=dev HEURE_UTC=11
verifier "aucune fenêtre en dev" '[ "$code" -eq 0 ]'

echo "Entrées invalides"
executer ENVIRONNEMENT=inconnu
verifier "environnement refusé" '[ "$code" -eq 1 ]'
executer SERVICES="personnes;rm"
verifier "nom de service refusé" '[ "$code" -eq 1 ]'

echo
echo "${total} vérifications, ${echecs} échec(s)"
[ "$echecs" -eq 0 ]
