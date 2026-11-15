#!/usr/bin/env bash
# CLI « az » simulée pour les tests de deployer-service.sh et de verifier-rechiffrement.sh. Chaque appel est consigné dans
# $AZ_JOURNAL ; les réponses dépendent des variables FAKE_* (voir les scripts de test).
printf '%s\n' "$*" >>"${AZ_JOURNAL:?}"
args="$*"
case "$args" in
  "containerapp list"*) echo "${FAKE_GROUPE-rg-sepp-dev-standard}" ;;
  "containerapp revision list"*) printf '%s' "${FAKE_COURANTE-}" ;;
  "containerapp ingress traffic set"*) ;;
  # Jobs rech-/verif- : présents seulement si FAKE_JOBS_CLES liste leur nom (séparés par des espaces).
  "containerapp job list"*"'rech-"* | "containerapp job list"*"'verif-"*)
    for nom in ${FAKE_JOBS_CLES-}; do
      case "$args" in *"'${nom}'"*) echo "${FAKE_GROUPE_JOB-rg-sepp-dev-standard}" ;; esac
    done
    ;;
  "containerapp job list"*) echo "${FAKE_JOB-mig-personnes}" ;;
  "containerapp job update"*) ;;
  "containerapp job start"*) echo "mig-personnes-exec1" ;;
  "containerapp job execution list"*) printf '%s' "${FAKE_EN_COURS-}" ;;
  "containerapp job execution show"*)
    case "$args" in
      *"-n verif-"*) echo "${FAKE_VERIF_STATUT-Succeeded}" ;;
      *) echo "${FAKE_JOB_STATUT-Succeeded}" ;;
    esac
    ;;
  "containerapp update"*) ;;
  "containerapp revision show"*) echo "${FAKE_SANTE-Running Healthy}" ;;
  "containerapp revision deactivate"*) ;;
  "monitor log-analytics workspace show"*) echo "00000000-0000-0000-0000-000000000000" ;;
  "monitor log-analytics query"*) printf '%s\n' "${FAKE_MESURE-100	0}" ;;
  *) echo "az simulé : commande inattendue : $args" >&2; exit 2 ;;
esac
