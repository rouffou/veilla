#!/usr/bin/env bash
# Tests de deployer-service.sh avec une CLI « az » simulée (tools/deploy/tests/az-simule.sh).
# Usage : bash tools/deploy/tests/deployer-service.test.sh
# Les conditions sont évaluées par « eval » (guillemets simples voulus) ; code et sortie sont lus par ces conditions.
# shellcheck disable=SC2016,SC2034
set -uo pipefail

racine=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script="$racine/../deployer-service.sh"
travail=$(mktemp -d)
trap 'rm -rf "$travail"' EXIT
mkdir -p "$travail/bin"
cp "$racine/az-simule.sh" "$travail/bin/az"
chmod +x "$travail/bin/az"

empreinte="sha256:$(printf 'a%.0s' {1..64})"
echecs=0
total=0

# executer <nom> [VAR=valeur...] : exécute le script ; code de sortie dans $code, sortie dans $sortie.
executer() {
  AZ_JOURNAL="$travail/az.log"
  : >"$AZ_JOURNAL"
  : >"$travail/resume.md"
  sortie=$(env PATH="$travail/bin:$PATH" AZ_JOURNAL="$AZ_JOURNAL" GITHUB_STEP_SUMMARY="$travail/resume.md" \
    ENVIRONNEMENT=dev SERVICE=personnes IMAGE_PAR_EMPREINTE="acr.azurecr.io/sepp/personnes@${empreinte}" \
    SUFFIXE_REVISION=r1 ATTENTE_PALIER_S=0 INTERVALLE_S=0 DELAI_SONDES_S=0 DELAI_MIGRATION_S=0 \
    "$@" bash "$script" 2>&1)
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

echo "Succès avec migration et trois paliers"
executer FAKE_COURANTE=ca-personnes--ancienne
verifier "code 0" '[ "$code" -eq 0 ]'
verifier "migration lancée" '[ "$(appel "job start")" -eq 1 ]'
verifier "palier 10 %" 'grep -q -- "--revision-weight ca-personnes--ancienne=90 ca-personnes--r1=10" "$travail/az.log"'
verifier "palier 50 %" 'grep -q -- "--revision-weight ca-personnes--ancienne=50 ca-personnes--r1=50" "$travail/az.log"'
verifier "palier 100 %" 'grep -q -- "--revision-weight ca-personnes--r1=100" "$travail/az.log"'
verifier "ancienne révision désactivée" 'grep -q -- "revision deactivate .*--revision ca-personnes--ancienne" "$travail/az.log"'
verifier "résumé écrit" 'grep -q "succès" "$travail/resume.md"'

echo "Service sans job de migration"
executer FAKE_COURANTE=ca-personnes--ancienne FAKE_JOB=
verifier "code 0" '[ "$code" -eq 0 ]'
verifier "aucune migration lancée" '[ "$(appel "job start")" -eq 0 ]'

echo "Migration en échec : arrêt avant toute révision"
executer FAKE_COURANTE=ca-personnes--ancienne FAKE_JOB_STATUT=Failed
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "aucune révision créée" '[ "$(appel "containerapp update")" -eq 0 ]'
verifier "aucune désactivation" '[ "$(appel "revision deactivate")" -eq 0 ]'

echo "Sondes en échec : retour arrière"
executer FAKE_COURANTE=ca-personnes--ancienne FAKE_SANTE="Running Unhealthy"
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "trafic remis sur l'ancienne" 'tail -n 3 "$travail/az.log" | grep -q -- "--revision-weight ca-personnes--ancienne=100"'
verifier "nouvelle désactivée" 'grep -q -- "revision deactivate .*--revision ca-personnes--r1" "$travail/az.log"'
verifier "ancienne non désactivée" '! grep -q -- "revision deactivate .*--revision ca-personnes--ancienne" "$travail/az.log"'
verifier "résumé : retour arrière" 'grep -q "retour arrière" "$travail/resume.md"'

echo "Taux de 5xx au-dessus du seuil : retour arrière"
executer FAKE_COURANTE=ca-personnes--ancienne "FAKE_MESURE=200	10" SEUIL_5XX_POURCENT=1
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "pas de palier 50 %" '! grep -q -- "ca-personnes--r1=50" "$travail/az.log"'
verifier "trafic remis sur l'ancienne" 'grep -q -- "--revision-weight ca-personnes--ancienne=100" "$travail/az.log"'
verifier "nouvelle désactivée" 'grep -q -- "revision deactivate .*--revision ca-personnes--r1" "$travail/az.log"'

echo "Taux de 5xx sous le seuil"
executer FAKE_COURANTE=ca-personnes--ancienne "FAKE_MESURE=200	1" SEUIL_5XX_POURCENT=1
verifier "code 0" '[ "$code" -eq 0 ]'

echo "Plusieurs révisions avec trafic : refus"
executer "FAKE_COURANTE=ca-personnes--a
ca-personnes--b"
verifier "code 1" '[ "$code" -eq 1 ]'
verifier "aucune révision créée" '[ "$(appel "containerapp update")" -eq 0 ]'

echo "Premier déploiement (aucune révision courante)"
executer FAKE_COURANTE=
verifier "code 0" '[ "$code" -eq 0 ]'
verifier "100 % direct" 'grep -q -- "--revision-weight ca-personnes--r1=100" "$travail/az.log"'
verifier "pas de palier 10 %" '! grep -q -- "r1=10$" "$travail/az.log"'

echo "Entrées invalides"
executer FAKE_COURANTE=x ENVIRONNEMENT=inconnu
verifier "environnement refusé" '[ "$code" -eq 1 ]'
executer FAKE_COURANTE=x IMAGE_PAR_EMPREINTE=acr.azurecr.io/sepp/personnes:1.0
verifier "étiquette refusée" '[ "$code" -eq 1 ]'
executer FAKE_COURANTE=x PALIERS="10 50"
verifier "dernier palier ≠ 100 refusé" '[ "$code" -eq 1 ]'

echo
echo "${total} vérifications, ${echecs} échec(s)"
[ "$echecs" -eq 0 ]
