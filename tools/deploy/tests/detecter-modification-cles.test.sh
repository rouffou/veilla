#!/usr/bin/env bash
# Tests de detecter-modification-cles.sh sur un dépôt git temporaire.
# Usage : bash tools/deploy/tests/detecter-modification-cles.test.sh
# shellcheck disable=SC2016,SC2034
set -uo pipefail

racine=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script="$racine/../detecter-modification-cles.sh"
travail=$(mktemp -d)
trap 'rm -rf "$travail"' EXIT
echecs=0
total=0

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

cd "$travail" || exit 1
depot() { git -C "$travail" "$@"; }
depot init -q .
depot config user.email "test@example.invalid"
depot config user.name "Test"
depot config commit.gpgsign false
mkdir -p infra/environments/dev infra/environments/prod

ecrire_dev() {
  cat >infra/environments/dev/terraform.tfvars <<FIN
location = "westeurope" # encryption_key_ids = ["X"]
# encryption_key_ids = ["COMMENTE"]
encryption_key_ids = $1
encryption_legacy_key_ids = $2
log_retention_days = ${3:-30}
FIN
}
valider() {
  depot add -A
  depot commit -qm "$1"
}
detecter() {
  sortie=$(cd "$travail" && bash "$script" "$base" "$(git rev-parse HEAD)" 2>&1)
  code=$?
}

ecrire_dev '["D1"]' '["K1"]'
printf 'encryption_key_ids = ["D1"]\nencryption_legacy_key_ids = ["K1"]\n' >infra/environments/prod/terraform.tfvars
valider base
base=$(git rev-parse HEAD)

echo "Aucun changement de clé"
ecrire_dev '["D1"]' '["K1"]' 60
valider "autre variable"
detecter
verifier "autre variable : rien" '[ "$code" -eq 0 ] && [ -z "$sortie" ]'

echo "Mise en forme seule (espaces, liste sur plusieurs lignes, commentaire)"
cat >infra/environments/dev/terraform.tfvars <<'FIN'
location = "westeurope"
encryption_key_ids = [
  "D1", # courante
]
encryption_legacy_key_ids   =   [ "K1" ]
log_retention_days = 60
FIN
valider "mise en forme"
detecter
verifier "mise en forme : rien" '[ "$code" -eq 0 ] && [ -z "$sortie" ]'

echo "Retrait d'une ancienne clé"
ecrire_dev '["D1"]' '[]' 60
valider "retrait"
detecter
verifier "dev détecté" '[ "$code" -eq 0 ] && [ "$sortie" = "dev" ]'

echo "Rotation de la DEK dans prod, en plus"
printf 'encryption_key_ids = ["D2", "D1"]\nencryption_legacy_key_ids = ["K1"]\n' >infra/environments/prod/terraform.tfvars
valider "rotation"
detecter
verifier "dev et prod détectés" '[ "$code" -eq 0 ] && [ "$sortie" = "$(printf "dev\nprod")" ]'

echo "Suppression du fichier"
depot rm -q infra/environments/prod/terraform.tfvars
valider "suppression"
detecter
verifier "suppression détectée" 'printf "%s" "$sortie" | grep -qx prod'

echo "Branchement dans main.tf de l'environnement"
printf 'module "sepp" {\n  encryption_key_ids = var.encryption_key_ids\n  encryption_legacy_key_ids = var.encryption_legacy_key_ids\n}\n' >infra/environments/dev/main.tf
valider "main.tf"
base=$(git rev-parse HEAD)
printf 'module "sepp" {\n  encryption_key_ids = ["D9"]\n  encryption_legacy_key_ids = var.encryption_legacy_key_ids\n}\n' >infra/environments/dev/main.tf
valider "main.tf en dur"
detecter
verifier "valeur en dur dans main.tf détectée" '[ "$code" -eq 0 ] && [ "$sortie" = "dev" ]'
printf 'module "sepp" {\n  name = "x"\n  encryption_key_ids = ["D9"]\n  encryption_legacy_key_ids = var.encryption_legacy_key_ids\n}\n' >infra/environments/dev/main.tf
valider "main.tf autre"
base=$(git rev-parse HEAD)
printf 'module "sepp" {\n  name = "y"\n  encryption_key_ids = ["D9"]\n  encryption_legacy_key_ids = var.encryption_legacy_key_ids\n}\n' >infra/environments/dev/main.tf
valider "main.tf autre 2"
detecter
verifier "autre argument de main.tf : rien" '[ "$code" -eq 0 ] && [ -z "$sortie" ]'

echo "Clé précédente de l'index aveugle (#510), absente puis présente"
mkdir -p infra/environments/recette
printf 'location = "x"\n' >infra/environments/recette/terraform.tfvars
valider "recette sans la variable"
base=$(git rev-parse HEAD)
printf 'location = "y"\n' >infra/environments/recette/terraform.tfvars
valider "autre variable seulement"
detecter
verifier "variable absente des deux révisions : rien" '[ "$code" -eq 0 ] && ! printf "%s" "$sortie" | grep -qx recette'
printf 'location = "y"\nblind_index_previous_key_enabled = true\n' >infra/environments/recette/terraform.tfvars
valider "variable ajoutée"
base=$(git rev-parse HEAD)
printf 'location = "y"\nblind_index_previous_key_enabled = false\n' >infra/environments/recette/terraform.tfvars
valider "clé précédente retirée"
detecter
verifier "passage à false détecté dans les tfvars" '[ "$code" -eq 0 ] && printf "%s" "$sortie" | grep -qx recette'
base=$(git rev-parse HEAD)
printf 'module "sepp" {\n  blind_index_previous_key_enabled = var.blind_index_previous_key_enabled\n}\n' >infra/environments/recette/main.tf
valider "branchement"
base=$(git rev-parse HEAD)
printf 'module "sepp" {\n  blind_index_previous_key_enabled = false\n}\n' >infra/environments/recette/main.tf
valider "valeur en dur"
detecter
verifier "valeur en dur dans main.tf détectée" '[ "$code" -eq 0 ] && printf "%s" "$sortie" | grep -qx recette'

echo "Dossier d'environnement inconnu"
mkdir -p infra/environments/autre
printf 'encryption_key_ids = ["D1"]\n' >infra/environments/autre/terraform.tfvars
valider "inconnu"
detecter
verifier "refusé" '[ "$code" -eq 1 ]'

echo
echo "${total} vérifications, ${echecs} échec(s)"
[ "$echecs" -eq 0 ]
