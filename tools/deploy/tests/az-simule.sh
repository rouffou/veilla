#!/usr/bin/env bash
# CLI « az » simulée pour les tests de deployer-service.sh. Chaque appel est consigné dans $AZ_JOURNAL ;
# les réponses dépendent des variables FAKE_* (voir deployer-service.test.sh).
printf '%s\n' "$*" >>"${AZ_JOURNAL:?}"
args="$*"
case "$args" in
  "containerapp list"*) echo "${FAKE_GROUPE-rg-sepp-dev-standard}" ;;
  "containerapp revision list"*) printf '%s' "${FAKE_COURANTE-}" ;;
  "containerapp ingress traffic set"*) ;;
  "containerapp job list"*) echo "${FAKE_JOB-mig-personnes}" ;;
  "containerapp job update"*) ;;
  "containerapp job start"*) echo "mig-personnes-exec1" ;;
  "containerapp job execution show"*) echo "${FAKE_JOB_STATUT-Succeeded}" ;;
  "containerapp update"*) ;;
  "containerapp revision show"*) echo "${FAKE_SANTE-Running Healthy}" ;;
  "containerapp revision deactivate"*) ;;
  "monitor log-analytics workspace show"*) echo "00000000-0000-0000-0000-000000000000" ;;
  "monitor log-analytics query"*) printf '%s\n' "${FAKE_MESURE-100	0}" ;;
  *) echo "az simulé : commande inattendue : $args" >&2; exit 2 ;;
esac
