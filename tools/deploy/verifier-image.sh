#!/usr/bin/env bash
# Vérifie la signature cosign et l'attestation SBOM d'une image par son empreinte (ARC-50, CTR-20).
# Signature « sans clé » (Sigstore) : l'identité attendue est celle du workflow d'images
# (.github/workflows/service-images.yml, branche main) ; toute autre identité fait échouer la vérification.
#
# Entrées (variables d'environnement) :
#   IMAGE_PAR_EMPREINTE  <registre>/<depot>@sha256:<empreinte> (obligatoire)
#   GITHUB_REPOSITORY    <propriétaire>/<dépôt> (fourni par GitHub Actions)
#   IDENTITE_WORKFLOW    facultatif : surcharge de l'identité de certificat attendue
#   EMETTEUR_OIDC        facultatif : émetteur OIDC attendu
set -euo pipefail

: "${IMAGE_PAR_EMPREINTE:?IMAGE_PAR_EMPREINTE est obligatoire}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY est obligatoire}"

if ! [[ "$IMAGE_PAR_EMPREINTE" =~ @sha256:[0-9a-f]{64}$ ]]; then
  echo "::error::La référence doit être exprimée par empreinte (…@sha256:<64 hexadécimaux>)."
  exit 1
fi

identite="${IDENTITE_WORKFLOW:-https://github.com/${GITHUB_REPOSITORY}/.github/workflows/service-images.yml@refs/heads/main}"
emetteur="${EMETTEUR_OIDC:-https://token.actions.githubusercontent.com}"
resume="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

echo "Vérification de la signature de ${IMAGE_PAR_EMPREINTE}"
if ! cosign verify \
  --certificate-identity "$identite" \
  --certificate-oidc-issuer "$emetteur" \
  "$IMAGE_PAR_EMPREINTE" >/dev/null; then
  echo "::error::Signature cosign absente ou invalide : déploiement refusé."
  echo "- Signature cosign : **invalide**, déploiement refusé" >>"$resume"
  exit 1
fi

echo "Vérification de l'attestation SBOM (spdxjson)"
if ! cosign verify-attestation \
  --type spdxjson \
  --certificate-identity "$identite" \
  --certificate-oidc-issuer "$emetteur" \
  "$IMAGE_PAR_EMPREINTE" >/dev/null; then
  echo "::error::Attestation SBOM absente ou invalide : déploiement refusé."
  echo "- Attestation SBOM : **invalide**, déploiement refusé" >>"$resume"
  exit 1
fi

echo "- Signature cosign et attestation SBOM vérifiées (identité \`${identite}\`)" >>"$resume"
