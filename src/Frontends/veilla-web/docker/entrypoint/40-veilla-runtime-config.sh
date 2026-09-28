#!/bin/sh
# Génère la configuration d'exécution du front au démarrage du conteneur (CTR-05) :
#   1. assets/config.json : fichier monté (VEILLA_CONFIG_FILE) ou généré depuis les variables
#      VEILLA_API_BASE_URL, VEILLA_OIDC_AUTHORITY, VEILLA_OIDC_CLIENT_ID, VEILLA_OIDC_SCOPE ;
#   2. en-têtes de sécurité nginx, dont la CSP connect-src (CSP_CONNECT_SRC ou origines
#      déduites de la configuration).
# Tout est écrit dans VEILLA_RUNTIME_DIR (/tmp/veilla) : compatible avec un système de
# fichiers racine en lecture seule (CTR-07) moyennant un volume temporaire sur /tmp.
set -eu

RUNTIME_DIR="${VEILLA_RUNTIME_DIR:-/tmp/veilla}"
CONFIG_FILE="${VEILLA_CONFIG_FILE:-/etc/veilla/runtime/config.json}"
TARGET="$RUNTIME_DIR/config.json"
TEMPLATE_DIR="${VEILLA_TEMPLATE_DIR:-/etc/veilla}"

log() { echo "veilla-config: $*"; }
fail() { echo "veilla-config: ERREUR: $*" >&2; exit 1; }

mkdir -p "$RUNTIME_DIR"

if [ -f "$CONFIG_FILE" ]; then
  cp "$CONFIG_FILE" "$TARGET"
  log "configuration montée utilisée ($CONFIG_FILE)"
else
  [ -n "${VEILLA_API_BASE_URL:-}" ] || fail "VEILLA_API_BASE_URL est obligatoire"
  [ -n "${VEILLA_OIDC_AUTHORITY:-}" ] || fail "VEILLA_OIDC_AUTHORITY est obligatoire"
  [ -n "${VEILLA_OIDC_CLIENT_ID:-}" ] || fail "VEILLA_OIDC_CLIENT_ID est obligatoire"
  VEILLA_OIDC_SCOPE="${VEILLA_OIDC_SCOPE:-openid profile email offline_access}"
  export VEILLA_API_BASE_URL VEILLA_OIDC_AUTHORITY VEILLA_OIDC_CLIENT_ID VEILLA_OIDC_SCOPE

  for value in "$VEILLA_API_BASE_URL" "$VEILLA_OIDC_AUTHORITY" "$VEILLA_OIDC_CLIENT_ID" "$VEILLA_OIDC_SCOPE"; do
    case "$value" in
      *'"'* | *'\'* ) fail "caractère interdit (guillemet ou barre oblique inverse) dans une variable VEILLA_*" ;;
    esac
  done

  # shellcheck disable=SC2016 # liste de variables pour envsubst, non développée par le shell
  envsubst '${VEILLA_API_BASE_URL} ${VEILLA_OIDC_AUTHORITY} ${VEILLA_OIDC_CLIENT_ID} ${VEILLA_OIDC_SCOPE}' \
    < "$TEMPLATE_DIR/config.template.json" > "$TARGET"
  log "configuration générée depuis l'environnement"
fi

# Origine (schéma://hôte[:port]) d'une URL absolue.
origin() { printf '%s' "$1" | sed -E 's|^([A-Za-z][A-Za-z0-9+.-]*://[^/?#]+).*|\1|'; }
# Valeur d'une clé texte du fichier JSON (une clé par ligne).
json_value() { sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$TARGET" | head -n 1; }

if [ -n "${CSP_CONNECT_SRC:-}" ]; then
  connect_src=" $CSP_CONNECT_SRC"
else
  connect_src=""
  for url in "$(json_value apiBaseUrl)" "$(json_value authority)"; do
    case "$url" in
      http://* | https://* ) connect_src="$connect_src $(origin "$url")" ;;
    esac
  done
fi
case "$connect_src" in
  *'"'* | *';'* ) fail "CSP_CONNECT_SRC ne peut contenir ni guillemet ni point-virgule" ;;
esac
CSP_CONNECT_SRC="$connect_src"
export CSP_CONNECT_SRC

# shellcheck disable=SC2016
envsubst '${CSP_CONNECT_SRC}' < "$TEMPLATE_DIR/security-headers.conf.template" > "$RUNTIME_DIR/security-headers.conf"
log "CSP connect-src : 'self'$CSP_CONNECT_SRC"
