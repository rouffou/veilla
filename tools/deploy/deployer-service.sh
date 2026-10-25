#!/usr/bin/env bash
# Déploiement progressif d'un service sur Azure Container Apps (ARC-50, ADR 0010 et 0011).
#
# Déroulé :
#   1. localisation de la Container App « ca-<service> » et de la révision courante ;
#   2. migration de schéma par le job « mig-<service> » s'il existe (attente de « Succeeded », sinon arrêt) ;
#   3. nouvelle révision à 0 % de trafic ;
#   4. sondes de santé de la révision (état « Healthy ») ;
#   5. paliers de trafic (10, 50, 100 % par défaut) ; à chaque palier : attente, puis contrôle du taux de 5xx
#      de la révision (Log Analytics) contre le seuil ;
#   6. échec à partir de la création de la révision : retour arrière automatique (100 % sur l'ancienne
#      révision, puis désactivation de la nouvelle) ;
#   7. succès : désactivation de l'ancienne révision (réactivable : « az containerapp revision activate »).
#
# Aucune migration « Down » n'est jouée : les migrations sont rétrocompatibles (expand/contract, ADR 0010).
#
# Entrées (variables d'environnement) :
#   ENVIRONNEMENT        dev | test | recette | preprod | prod (obligatoire)
#   SERVICE              nom du service, ex. « personnes » (obligatoire)
#   IMAGE_PAR_EMPREINTE  <registre>/<depot>@sha256:<empreinte> (obligatoire)
#   SEUIL_5XX_POURCENT   taux maximal de réponses 5xx de la révision, en % (défaut 1)
#   MIN_REQUETES         nombre minimal de requêtes pour juger un palier (défaut 20) ; en dessous, avertissement
#   PALIERS              pourcentages successifs (défaut « 10 50 100 ») ; le dernier doit valoir 100
#   ATTENTE_PALIER_S     attente avant chaque contrôle, en secondes (défaut 180, latence d'ingestion comprise)
#   DELAI_SONDES_S       délai maximal pour que la révision soit saine (défaut 600)
#   DELAI_MIGRATION_S    délai maximal d'une migration (défaut 2100, soit plus que le délai du job)
#   INTERVALLE_S         période d'interrogation (défaut 10)
#   SUFFIXE_REVISION     suffixe de la révision (défaut r<aammjjhhmmss>)
#   WORKSPACE_ID         identifiant (customerId) de l'espace Log Analytics ; défaut : « log-sepp-<env> » du groupe
#                        de ressources « rg-sepp-<env>-platform »
#   DESACTIVER_ANCIENNE  true (défaut) | false : désactiver l'ancienne révision après succès
#   GITHUB_STEP_SUMMARY  fichier du résumé (défaut : sortie standard)
set -euo pipefail

: "${ENVIRONNEMENT:?ENVIRONNEMENT est obligatoire}"
: "${SERVICE:?SERVICE est obligatoire}"
: "${IMAGE_PAR_EMPREINTE:?IMAGE_PAR_EMPREINTE est obligatoire}"

SEUIL_5XX_POURCENT="${SEUIL_5XX_POURCENT:-1}"
MIN_REQUETES="${MIN_REQUETES:-20}"
PALIERS="${PALIERS:-10 50 100}"
ATTENTE_PALIER_S="${ATTENTE_PALIER_S:-180}"
DELAI_SONDES_S="${DELAI_SONDES_S:-600}"
DELAI_MIGRATION_S="${DELAI_MIGRATION_S:-2100}"
INTERVALLE_S="${INTERVALLE_S:-10}"
SUFFIXE_REVISION="${SUFFIXE_REVISION:-r$(date -u +%y%m%d%H%M%S)}"
DESACTIVER_ANCIENNE="${DESACTIVER_ANCIENNE:-true}"
RESUME="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

APP="ca-${SERVICE}"
JOB="mig-${SERVICE}"
NOUVELLE="${APP}--${SUFFIXE_REVISION}"

# --- Validation des entrées -------------------------------------------------------------------------------------
[[ "$ENVIRONNEMENT" =~ ^(dev|test|recette|preprod|prod)$ ]] || { echo "::error::Environnement inconnu : $ENVIRONNEMENT"; exit 1; }
[[ "$SERVICE" =~ ^[a-z0-9]([a-z0-9-]*[a-z0-9])?$ ]] || { echo "::error::Nom de service invalide : $SERVICE"; exit 1; }
[[ "$SUFFIXE_REVISION" =~ ^[a-z0-9]([a-z0-9-]*[a-z0-9])?$ ]] || { echo "::error::Suffixe de révision invalide : $SUFFIXE_REVISION"; exit 1; }
[[ "$IMAGE_PAR_EMPREINTE" =~ @sha256:[0-9a-f]{64}$ ]] || { echo "::error::L'image doit être référencée par empreinte."; exit 1; }
[[ "$SEUIL_5XX_POURCENT" =~ ^[0-9]+([.][0-9]+)?$ ]] || { echo "::error::Seuil de 5xx invalide : $SEUIL_5XX_POURCENT"; exit 1; }
derniers_palier="${PALIERS##* }"
[ "$derniers_palier" = "100" ] || { echo "::error::Le dernier palier doit valoir 100 (reçu : $PALIERS)."; exit 1; }
for p in $PALIERS; do
  [[ "$p" =~ ^[0-9]+$ ]] && [ "$p" -ge 1 ] && [ "$p" -le 100 ] || { echo "::error::Palier invalide : $p"; exit 1; }
done

# --- État partagé avec le retour arrière ------------------------------------------------------------------------
GROUPE=""
COURANTE=""
REVISION_TENTEE=false
TERMINE=false
RAISON=""
declare -a JOURNAL=()

journaliser() { JOURNAL+=("$1"); echo "$1"; }

ecrire_resume() {
  {
    echo "### Déploiement de \`${SERVICE}\` en \`${ENVIRONNEMENT}\`"
    echo
    echo "| Élément | Valeur |"
    echo "| --- | --- |"
    echo "| Image | \`${IMAGE_PAR_EMPREINTE}\` |"
    echo "| Révision précédente | \`${COURANTE:-aucune}\` |"
    echo "| Nouvelle révision | \`${NOUVELLE}\` |"
    echo "| Paliers | ${PALIERS} (seuil 5xx : ${SEUIL_5XX_POURCENT} %) |"
    echo "| Résultat | $1 |"
    echo
    local ligne
    for ligne in "${JOURNAL[@]}"; do echo "- ${ligne}"; done
  } >>"$RESUME"
}

# Le retour arrière ne masque jamais la cause de l'échec : ses propres erreurs sont seulement journalisées.
retour_arriere() {
  set +e
  echo "::warning::Retour arrière en cours (${RAISON})."
  if [ -n "$COURANTE" ]; then
    if az containerapp ingress traffic set -g "$GROUPE" -n "$APP" --revision-weight "${COURANTE}=100" >/dev/null; then
      journaliser "Retour arrière : trafic remis à 100 % sur \`${COURANTE}\`."
    else
      journaliser "ERREUR du retour arrière : trafic NON remis sur \`${COURANTE}\` (intervention manuelle requise)."
    fi
  else
    journaliser "Aucune révision précédente : pas de trafic à restaurer."
  fi
  if az containerapp revision deactivate -g "$GROUPE" -n "$APP" --revision "$NOUVELLE" >/dev/null 2>&1; then
    journaliser "Révision \`${NOUVELLE}\` désactivée."
  else
    journaliser "Révision \`${NOUVELLE}\` non désactivée (absente ou erreur)."
  fi
}

a_la_sortie() {
  local code=$?
  trap - EXIT
  set +e
  if [ "$TERMINE" = true ]; then
    ecrire_resume "succès"
    exit 0
  fi
  [ -n "$RAISON" ] || RAISON="erreur inattendue (code ${code})"
  if [ "$REVISION_TENTEE" = true ] && [ -n "$GROUPE" ]; then
    retour_arriere
    ecrire_resume "**échec, retour arrière** : ${RAISON}"
  else
    journaliser "Échec avant la création de la révision : ${RAISON} (aucun trafic modifié)."
    ecrire_resume "**échec** : ${RAISON}"
  fi
  echo "::error::${RAISON}"
  exit 1
}
trap a_la_sortie EXIT

echec() { RAISON="$1"; exit 1; }

# --- 1. Localisation --------------------------------------------------------------------------------------------
GROUPE=$(az containerapp list --query "[?name=='${APP}' && starts_with(resourceGroup, 'rg-sepp-${ENVIRONNEMENT}-')].resourceGroup | [0]" -o tsv)
[ -n "$GROUPE" ] || echec "Container App ${APP} introuvable pour l'environnement ${ENVIRONNEMENT}."
journaliser "Container App \`${APP}\` dans \`${GROUPE}\`."

actives=$(az containerapp revision list -g "$GROUPE" -n "$APP" --query "[?properties.trafficWeight>\`0\`].name" -o tsv)
nb_actives=$(printf '%s' "$actives" | grep -c . || true)
if [ "$nb_actives" -gt 1 ]; then
  echec "Plusieurs révisions reçoivent du trafic ($(echo "$actives" | tr '\n' ' ')) : un déploiement précédent est inachevé."
fi
COURANTE="$actives"
journaliser "Révision courante : \`${COURANTE:-aucune (premier déploiement)}\`."

# Figer le trafic : la nouvelle révision doit démarrer sans trafic (mode de révision multiple).
if [ -n "$COURANTE" ]; then
  az containerapp ingress traffic set -g "$GROUPE" -n "$APP" --revision-weight "${COURANTE}=100" >/dev/null
fi

# --- 2. Migration (ADR 0010) ------------------------------------------------------------------------------------
existe_job=$(az containerapp job list -g "$GROUPE" --query "[?name=='${JOB}'].name | [0]" -o tsv)
if [ -n "$existe_job" ]; then
  az containerapp job update -g "$GROUPE" -n "$JOB" --image "$IMAGE_PAR_EMPREINTE" >/dev/null
  execution=$(az containerapp job start -g "$GROUPE" -n "$JOB" --query name -o tsv)
  [ -n "$execution" ] || echec "Le job ${JOB} n'a pas démarré."
  journaliser "Migration \`${JOB}\` lancée (exécution \`${execution}\`)."
  ecoule=0
  while :; do
    statut=$(az containerapp job execution show -g "$GROUPE" -n "$JOB" --job-execution-name "$execution" --query properties.status -o tsv)
    case "$statut" in
      Succeeded) break ;;
      Running | Processing | "") ;;
      *) echec "Migration en échec (état ${statut}) : déploiement arrêté, aucune révision créée." ;;
    esac
    [ "$ecoule" -lt "$DELAI_MIGRATION_S" ] || echec "Migration sans réponse après ${DELAI_MIGRATION_S} s : déploiement arrêté."
    sleep "$INTERVALLE_S"
    ecoule=$((ecoule + INTERVALLE_S))
  done
  journaliser "Migration terminée (Succeeded)."
else
  journaliser "Aucun job \`${JOB}\` : pas de migration."
fi

# --- 3. Nouvelle révision à 0 % ---------------------------------------------------------------------------------
REVISION_TENTEE=true
az containerapp update -g "$GROUPE" -n "$APP" --image "$IMAGE_PAR_EMPREINTE" --revision-suffix "$SUFFIXE_REVISION" >/dev/null
journaliser "Révision \`${NOUVELLE}\` créée."

# --- 4. Sondes de santé -----------------------------------------------------------------------------------------
ecoule=0
while :; do
  etat=$(az containerapp revision show -g "$GROUPE" -n "$APP" --revision "$NOUVELLE" \
    --query "join(' ', [properties.runningState, properties.healthState])" -o tsv)
  case "$etat" in
    "Running Healthy") break ;;
    *Failed* | *Unhealthy* | *Degraded*) echec "Sondes de santé en échec pour ${NOUVELLE} (${etat})." ;;
  esac
  [ "$ecoule" -lt "$DELAI_SONDES_S" ] || echec "Révision ${NOUVELLE} non saine après ${DELAI_SONDES_S} s (${etat:-inconnu})."
  sleep "$INTERVALLE_S"
  ecoule=$((ecoule + INTERVALLE_S))
done
journaliser "Sondes de santé : \`${NOUVELLE}\` saine."

# --- 5. Paliers de trafic et contrôle du taux de 5xx -------------------------------------------------------------
espace="${WORKSPACE_ID:-}"
if [ -z "$espace" ]; then
  espace=$(az monitor log-analytics workspace show -g "rg-sepp-${ENVIRONNEMENT}-platform" -n "log-sepp-${ENVIRONNEMENT}" --query customerId -o tsv)
fi
[ -n "$espace" ] || echec "Espace Log Analytics introuvable : contrôle du taux d'erreur impossible."

# Affiche « total erreurs » des requêtes de la révision depuis $1 secondes.
# Hypothèse à valider (#212) : les requêtes sont dans AppRequests et AppRoleInstance commence par le nom de la révision.
compter_requetes() {
  local fenetre="$1" requete
  requete="AppRequests | where TimeGenerated > ago(${fenetre}s) | where AppRoleInstance startswith '${NOUVELLE}' | summarize total=count(), erreurs=countif(toint(ResultCode) >= 500) | project total, erreurs"
  az monitor log-analytics query -w "$espace" --analytics-query "$requete" --query "[0].[total, erreurs]" -o tsv
}

for palier in $PALIERS; do
  if [ -n "$COURANTE" ] && [ "$palier" -lt 100 ]; then
    az containerapp ingress traffic set -g "$GROUPE" -n "$APP" \
      --revision-weight "${COURANTE}=$((100 - palier))" "${NOUVELLE}=${palier}" >/dev/null
  else
    az containerapp ingress traffic set -g "$GROUPE" -n "$APP" --revision-weight "${NOUVELLE}=100" >/dev/null
  fi
  journaliser "Palier ${palier} % : trafic réparti, attente de ${ATTENTE_PALIER_S} s."
  sleep "$ATTENTE_PALIER_S"

  mesure=$(compter_requetes "$((ATTENTE_PALIER_S + 60))") || echec "Requête Log Analytics en échec au palier ${palier} %."
  total=$(printf '%s' "$mesure" | awk '{print $1+0}')
  erreurs=$(printf '%s' "$mesure" | awk '{print $2+0}')
  if [ "$total" -lt "$MIN_REQUETES" ]; then
    journaliser "Palier ${palier} % : ${total} requête(s) seulement (minimum ${MIN_REQUETES}), contrôle peu significatif."
  fi
  taux=$(awk -v e="$erreurs" -v t="$total" 'BEGIN { if (t == 0) print "0.00"; else printf "%.2f", e * 100 / t }')
  if awk -v taux="$taux" -v seuil="$SEUIL_5XX_POURCENT" 'BEGIN { exit !(taux > seuil) }'; then
    echec "Palier ${palier} % : taux de 5xx ${taux} % (${erreurs}/${total}) supérieur au seuil ${SEUIL_5XX_POURCENT} %."
  fi
  journaliser "Palier ${palier} % validé : ${erreurs} erreur(s) 5xx sur ${total} requête(s) (${taux} %)."
done

# --- 7. Fin : désactivation de l'ancienne révision --------------------------------------------------------------
TERMINE=true
if [ -n "$COURANTE" ] && [ "$DESACTIVER_ANCIENNE" = "true" ]; then
  if az containerapp revision deactivate -g "$GROUPE" -n "$APP" --revision "$COURANTE" >/dev/null; then
    journaliser "Ancienne révision \`${COURANTE}\` désactivée."
  else
    journaliser "Avertissement : l'ancienne révision \`${COURANTE}\` n'a pas pu être désactivée."
  fi
fi
