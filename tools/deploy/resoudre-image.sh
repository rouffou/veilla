#!/usr/bin/env bash
# Résout une étiquette ou une empreinte en référence d'image « par empreinte » (ARC-50).
# La vérification de signature et le déploiement portent toujours sur l'empreinte, jamais sur une étiquette.
#
# Entrées : ACR_LOGIN_SERVER, IMAGE_PREFIX (défaut sepp), IMAGE (nom d'image), REFERENCE (étiquette ou sha256:…).
# Sorties (GITHUB_OUTPUT) : image (registre/depot), digest, reference (image@digest).
set -euo pipefail

: "${ACR_LOGIN_SERVER:?ACR_LOGIN_SERVER est obligatoire}"
: "${IMAGE:?IMAGE est obligatoire}"
: "${REFERENCE:?REFERENCE est obligatoire}"
prefixe="${IMAGE_PREFIX:-sepp}"
sortie="${GITHUB_OUTPUT:-/dev/stdout}"

depot="${prefixe}/${IMAGE}"
image="${ACR_LOGIN_SERVER}/${depot}"

case "$REFERENCE" in
  sha256:*) digest="$REFERENCE" ;;
  *)
    digest=$(az acr repository show --name "${ACR_LOGIN_SERVER%%.*}" --image "${depot}:${REFERENCE}" \
      --query digest --output tsv)
    ;;
esac

if ! [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]]; then
  echo "::error::Empreinte introuvable ou invalide pour ${depot} (${REFERENCE})."
  exit 1
fi

{
  echo "image=${image}"
  echo "digest=${digest}"
  echo "reference=${image}@${digest}"
} >>"$sortie"
echo "Image résolue : ${image}@${digest}"
