#!/usr/bin/env bash
# Génère les solutions .NET du monodépôt (ADR 0002) — à relancer après l'ajout d'un projet ou une fusion :
#   - une solution par microservice ou BFF : src/{Services,Bff}/<X>/<X>.slnx
#     (projets du service + socle partagé + contrats + règles d'architecture) ;
#   - src/BuildingBlocks/Socle.slnx : socle, contrats et leurs tests ;
#   - src/Sagas/Sagas.slnx : test de bout en bout des sagas (tests/Sepp.Sagas.Tests) avec le code des services qu'il héberge ;
#   - Sepp.slnx à la racine : tous les projets (vue d'ensemble dans l'IDE).
# La CI vérifie que les fichiers committés sont à jour.
set -euo pipefail
cd "$(dirname "$0")/.."

shared=$(find src/BuildingBlocks src/Contracts -name '*.csproj' | LC_ALL=C sort)
shared_tests="tests/Sepp.Testing.Architecture/Sepp.Testing.Architecture.csproj"

generate() { # $1 = fichier .slnx, reste = projets
  local file=$1; shift
  rm -f "$file"
  dotnet new sln -n "$(basename "$file" .slnx)" --format slnx -o "$(dirname "$file")" >/dev/null
  printf '%s\n' "$@" | xargs dotnet sln "$file" add >/dev/null
  echo "$file : $(grep -c '<Project ' "$file") projets"
}

for dir in src/Services/*/ src/Bff/*/; do
  [ -d "$dir" ] || continue
  name=$(basename "$dir")
  # shellcheck disable=SC2086
  generate "${dir}${name}.slnx" $(find "$dir" -name '*.csproj' | LC_ALL=C sort) $shared $shared_tests
done

# Le test de bout en bout des sagas héberge plusieurs services : il a sa propre solution (et son propre job de CI) plutôt que
# d'alourdir celle du socle.
sagas_tests="tests/Sepp.Sagas.Tests/Sepp.Sagas.Tests.csproj"

# shellcheck disable=SC2086
generate src/BuildingBlocks/Socle.slnx $shared $(find tests -name '*.csproj' -not -path 'tests/Sepp.Sagas.Tests/*' | LC_ALL=C sort)

# shellcheck disable=SC2046
generate src/Sagas/Sagas.slnx $sagas_tests $shared $shared_tests $(find src/Services/{Obligations,Planification,SurveillanceMedicale,Communications,Documents}/src -name '*.csproj' | LC_ALL=C sort)

# shellcheck disable=SC2046
generate Sepp.slnx $(find src tests -name '*.csproj' -not -path '*/node_modules/*' | LC_ALL=C sort)
