#!/usr/bin/env bash
# Détecte les environnements dont les clés de chiffrement changent entre deux révisions (#474, ADR 0012).
#   Usage : detecter-modification-cles.sh <base> <tête>
#   Sortie : un nom d'environnement par ligne (dev, test, recette, preprod, prod), vide si rien ne change.
# Sont comparées, dans infra/environments/<env>/*.tfvars et *.tf (main.tf transmet les variables au module racine), les
# déclarations de encryption_key_ids, encryption_legacy_key_ids et blind_index_previous_key_enabled (la passer à false retire
# la clé précédente de l'index aveugle, #510). La détection ne dépend pas de la présence de cette dernière : absente des deux
# révisions, elle ne change rien. Valeurs normalisées : commentaires et espaces ignorés, listes sur plusieurs lignes comprises.
# Ajouter, retirer, réordonner ou supprimer une valeur, créer un fichier ou en supprimer un sont des modifications.
# Échoue (code 1) pour un dossier d'environnement inconnu : le contrôle ne passe jamais « par défaut ».
set -euo pipefail

base="${1:?Usage : $0 <base> <tête>}"
tete="${2:?Usage : $0 <base> <tête>}"

# Déclarations normalisées des deux variables dans un fichier d'une révision (vide si le fichier n'existe pas).
declarations() {
  local revision="$1" fichier="$2"
  git show "${revision}:${fichier}" 2>/dev/null | awk '
    { sub(/#.*/, ""); sub(/\/\/.*/, "") }
    capture == 0 && /^[[:space:]]*(encryption(_legacy)?_key_ids|blind_index_previous_key_enabled)[[:space:]]*=/ { capture = 1; texte = "" ; profondeur = 0 }
    capture == 1 {
      ligne = $0
      gsub(/[[:space:]]/, "", ligne)
      texte = texte ligne
      profondeur += gsub(/\[/, "[", ligne) - gsub(/\]/, "]", ligne)
      if (profondeur <= 0) { gsub(/,]/, "]", texte); print texte; capture = 0 }
    }
  ' | sort
}

modifies=()
while IFS= read -r fichier; do
  [ -n "$fichier" ] || continue
  dossier="$(basename "$(dirname "$fichier")")"
  if ! [[ "$dossier" =~ ^(dev|test|recette|preprod|prod)$ ]]; then
    echo "::error::Dossier d'environnement inconnu : ${fichier}" >&2
    exit 1
  fi
  if [ "$(declarations "$base" "$fichier")" != "$(declarations "$tete" "$fichier")" ]; then
    modifies+=("$dossier")
  fi
done < <(git diff --name-only "$base" "$tete" -- 'infra/environments/*.tfvars' 'infra/environments/*.tf')

if [ "${#modifies[@]}" -gt 0 ]; then
  printf '%s\n' "${modifies[@]}" | sort -u
fi
