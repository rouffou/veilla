#!/usr/bin/env bash
# Lance les jobs « verif-<service> » (vérification seule du rechiffrement, #474, #482, ADR 0012) pour chaque service qui
# chiffre des champs, un à la fois, et échoue si l'un d'eux ne se termine pas par « Succeeded » (code de sortie 0 :
# plus aucune valeur n'utilise une autre clé que la DEK courante, aucune valeur chiffrée hors du modèle).
#   Les jobs s'exécutent avec l'identité d'EXÉCUTION du service : ce script ne fait que les démarrer et lire leur état,
#   avec l'identité fédérée du pipeline (aucun rôle Key Vault ni accès aux données). Il ne lance jamais « rech-<service> ».
# Entrées (variables d'environnement) :
#   ENVIRONNEMENT  dev | test | recette | preprod | prod (obligatoire)
#   SERVICES       services à vérifier, séparés par des espaces (obligatoire)
#   DELAI_S        attente maximale par service, en secondes (défaut 15300 : délai du job 14400 s + marge)
#   INTERVALLE_S   pause entre deux lectures d'état (défaut 30)
#   HEURE_UTC      heure UTC simulée (tests)
# Heures creuses : en preprod et en prod, le balayage séquentiel de la base n'est lancé qu'entre 18 h et 5 h UTC (soit,
# en Belgique, de 19 h ou 20 h à 6 h ou 7 h) ; hors fenêtre, le script s'arrête sans rien lancer.
set -euo pipefail

ENVIRONNEMENT="${ENVIRONNEMENT:?ENVIRONNEMENT requis}"
SERVICES="${SERVICES:?SERVICES requis}"
DELAI_S="${DELAI_S:-15300}"
INTERVALLE_S="${INTERVALLE_S:-30}"
RESUME="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

[[ "$ENVIRONNEMENT" =~ ^(dev|test|recette|preprod|prod)$ ]] || { echo "::error::Environnement inconnu : $ENVIRONNEMENT"; exit 1; }
for service in $SERVICES; do
  [[ "$service" =~ ^[a-z0-9]([a-z0-9-]*[a-z0-9])?$ ]] || { echo "::error::Nom de service invalide : $service"; exit 1; }
done

journaliser() { echo "$1"; printf '%s\n' "- $1" >>"$RESUME"; }

{ echo "### Vérification des clés de chiffrement (${ENVIRONNEMENT})"; echo; } >>"$RESUME"

if [[ "$ENVIRONNEMENT" =~ ^(preprod|prod)$ ]]; then
  heure=$((10#${HEURE_UTC:-$(date -u +%H)}))
  if [ "$heure" -ge 5 ] && [ "$heure" -lt 18 ]; then
    journaliser "**Refusé** : il est ${heure} h UTC. En ${ENVIRONNEMENT}, la vérification (balayage séquentiel de toute la base) ne se lance qu'entre 18 h et 5 h UTC. Aucun job lancé."
    echo "::error::Hors des heures creuses (18 h à 5 h UTC) : relancer plus tard."
    exit 1
  fi
fi

echecs=()
for service in $SERVICES; do
  job="verif-${service}"
  groupe=$(az containerapp job list --query "[?name=='${job}' && starts_with(resourceGroup, 'rg-sepp-${ENVIRONNEMENT}-')].resourceGroup | [0]" -o tsv)
  if [ -z "$groupe" ]; then
    journaliser "\`${job}\` : job introuvable : **non vérifié**."
    echecs+=("$service")
    continue
  fi

  # Une exécution à la fois : jamais deux balayages simultanés sur la même base.
  en_cours=$(az containerapp job execution list -g "$groupe" -n "$job" --query "[?properties.status=='Running' || properties.status=='Processing'].name | [0]" -o tsv)
  if [ -n "$en_cours" ]; then
    journaliser "\`${job}\` : l'exécution \`${en_cours}\` est déjà en cours : **non lancé**."
    echecs+=("$service")
    continue
  fi

  execution=$(az containerapp job start -g "$groupe" -n "$job" --query name -o tsv)
  if [ -z "$execution" ]; then
    journaliser "\`${job}\` : démarrage impossible."
    echecs+=("$service")
    continue
  fi
  journaliser "\`${job}\` lancé (exécution \`${execution}\`)."

  ecoule=0
  statut=""
  while :; do
    statut=$(az containerapp job execution show -g "$groupe" -n "$job" --job-execution-name "$execution" --query properties.status -o tsv)
    case "$statut" in
      Running | Processing | "") ;;
      *) break ;;
    esac
    if [ "$ecoule" -ge "$DELAI_S" ]; then
      statut="délai dépassé"
      break
    fi
    sleep "$INTERVALLE_S"
    ecoule=$((ecoule + INTERVALLE_S))
  done

  if [ "$statut" = "Succeeded" ]; then
    journaliser "\`${job}\` : **code 0**, base prête pour le retrait d'une clé."
  else
    journaliser "\`${job}\` : **échec** (${statut}). Journaux : \`az containerapp job logs show -g ${groupe} -n ${job} --execution ${execution} --container rechiffrement\` (catégorie Sepp.Rechiffrement)."
    echecs+=("$service")
  fi
done

if [ "${#echecs[@]}" -gt 0 ]; then
  echo "::error::Vérification non concluante pour : ${echecs[*]}. Aucune clé ne peut être retirée ni remplacée."
  exit 1
fi
journaliser "Tous les services ont renvoyé 0."
