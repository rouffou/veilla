#!/usr/bin/env bash
# Régénère les instantanés OpenAPI versionnés (docs/api/openapi/*.json) de tous les services et BFF (INT-01, ARC-30).
# À lancer après un changement volontaire d'API, puis à committer : le test de contrat ContratOpenApiTests compare le document
# servi par chaque hôte en mémoire à son instantané et signale les ruptures (opération, code de réponse, paramètre ou
# propriété supprimés) avant les ajouts. Sans Docker : les hôtes démarrent sans base de données.
#   usage : bash tools/regenerer-openapi.sh [Service ...]   (par ex. Referentiels Bff/Interne ; tous par défaut)
set -euo pipefail
cd "$(dirname "$0")/.."

if [ $# -gt 0 ]; then
  cibles=("$@")
else
  cibles=()
  for dir in src/Services/*/ src/Bff/*/; do
    cibles+=("${dir#src/}")
  done
fi

export SEPP_OPENAPI_REGENERER=1
for cible in "${cibles[@]}"; do
  cible=${cible%/}
  projet=$(find "src/$cible/tests" -maxdepth 1 -type d -name '*Integration.Tests' | head -n 1)
  [ -n "$projet" ] || { echo "Aucun projet de tests d'intégration pour $cible" >&2; exit 1; }
  echo "== $cible"
  dotnet test --project "$projet" -c Release --filter-trait "Categorie=ContratOpenApi"
done
git status --short docs/api/openapi
