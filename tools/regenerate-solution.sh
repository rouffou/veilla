#!/usr/bin/env bash
# Régénère Sepp.slnx à partir de tous les projets du dépôt (utile après une fusion de services).
set -euo pipefail
cd "$(dirname "$0")/.."
rm -f Sepp.slnx
dotnet new sln -n Sepp --format slnx -o . >/dev/null
find src tests -name '*.csproj' -not -path '*/node_modules/*' | sort | xargs dotnet sln Sepp.slnx add >/dev/null
echo "Sepp.slnx : $(grep -c '<Project ' Sepp.slnx) projets"
