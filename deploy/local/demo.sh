#!/usr/bin/env bash
# Démo locale en une commande (#393) : repart d'un environnement vierge, attend les services, puis amorce les données fictives.
#
#   deploy/local/demo.sh [--oui] [--projet <nom>] [--seulement-amorcage] [--delai <secondes>]
#
#   1. docker compose down -v      EFFACE les volumes du projet : bases PostgreSQL, Keycloak, documents (confirmation demandée)
#   2. docker compose up -d --build
#   3. attente de tous les /health/ready
#   4. dotnet run tools/donnees-demo : comptes, affiliés, travailleurs, rendez-vous, décisions, manifeste et résumé
#
#   --oui                   saute la confirmation (scripts, intégration continue)
#   --projet <nom>          nom du projet compose (défaut : « veilla », celui de compose.yaml) ; permet un essai sur un projet distinct
#   --seulement-amorcage    n'efface ni ne redémarre rien : relance seulement l'outil de données (idempotent)
#   --delai <secondes>      délai d'attente maximal des services (défaut : 900)
#
# Poste de développement uniquement : l'outil utilise le flux mot de passe de veilla-dev-cli et l'API d'administration de Keycloak.
set -euo pipefail

racine="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$racine"

oui=0
projet=""
seulement=0
delai=900
while [ $# -gt 0 ]; do
  case "$1" in
    --oui) oui=1 ;;
    --projet) projet="${2:?--projet attend un nom}"; shift ;;
    --seulement-amorcage) seulement=1 ;;
    --delai) delai="${2:?--delai attend un nombre de secondes}"; shift ;;
    -h|--help) sed -n '2,16p' "$0"; exit 0 ;;
    *) echo "Option inconnue : $1 (voir --help)" >&2; exit 64 ;;
  esac
  shift
done

compose=(docker compose -f deploy/local/compose.yaml)
nom_projet="veilla"
if [ -n "$projet" ]; then
  compose+=(-p "$projet")
  nom_projet="$projet"
fi

if [ "$seulement" -eq 0 ]; then
  if [ "$oui" -ne 1 ]; then
    echo "ATTENTION : cette commande exécute « docker compose down -v » sur le projet « $nom_projet »."
    echo "Toutes les données locales de ce projet seront EFFACÉES : bases PostgreSQL, utilisateurs Keycloak, documents."
    if [ ! -t 0 ]; then
      echo "Pas de terminal interactif : relancez avec --oui pour confirmer." >&2
      exit 1
    fi
    read -r -p "Tapez OUI pour continuer : " reponse
    if [ "$reponse" != "OUI" ]; then
      echo "Abandon, rien n'a été modifié."
      exit 1
    fi
  fi

  echo "== Arrêt et effacement du projet « $nom_projet »"
  "${compose[@]}" down -v --remove-orphans
  echo "== Construction et démarrage"
  "${compose[@]}" --progress quiet up -d --build

  echo "== Attente des services (délai maximal ${delai} s)"
  urls=(
    "Keycloak|http://localhost:8180/realms/veilla/.well-known/openid-configuration"
    "Référentiels|http://localhost:5110/health/ready"
    "Affiliés|http://localhost:5111/health/ready"
    "Personnes|http://localhost:5112/health/ready"
    "Postes et risques|http://localhost:5113/health/ready"
    "Audit|http://localhost:5114/health/ready"
    "Intégrations|http://localhost:5115/health/ready"
    "Obligations|http://localhost:5116/health/ready"
    "Planification|http://localhost:5117/health/ready"
    "Surveillance médicale|http://localhost:5118/health/ready"
    "Documents|http://localhost:5119/health/ready"
    "Communications|http://localhost:5120/health/ready"
    "Réintégration|http://localhost:5121/health/ready"
    "BFF employeur|http://localhost:5200/health/ready"
    "BFF travailleur|http://localhost:5201/health/ready"
  )
  if "${compose[@]}" config --services | grep -qx 'bff-interne'; then
    urls+=("BFF interne|http://localhost:5100/health/ready")
  fi
  limite=$(( $(date +%s) + delai ))
  for entree in "${urls[@]}"; do
    nom="${entree%%|*}"
    url="${entree#*|}"
    until curl -fsS -o /dev/null --max-time 5 "$url" 2>/dev/null; do
      if [ "$(date +%s)" -ge "$limite" ]; then
        echo "Délai dépassé : $nom ne répond pas sur $url." >&2
        echo "Voir « ${compose[*]} logs » pour le service concerné." >&2
        exit 2
      fi
      sleep 3
    done
    echo "   prêt : $nom"
  done
fi

echo "== Amorçage des données de démonstration"
exec dotnet run --project tools/donnees-demo/Sepp.Outils.DonneesDemo -c Release -- --racine "$racine"
