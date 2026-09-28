# ADR 0007 — Infrastructure as Code en Terraform

## Contexte
ARC-51 impose une infrastructure décrite en code et versionnée ; CTR-21 cite Bicep ou Terraform.

## Décision
**Terraform** (provider `azurerm` 4.x, `azapi` pour les ressources non couvertes), état distant dans un compte de stockage Azure, un répertoire par environnement (dev, test, recette, préproduction, production — CTR-22) appelant des modules partagés.

## Justification
Choix de l'équipe ; outil multi-fournisseurs, écosystème de vérification mature (`terraform validate`, tflint, analyse de sécurité), plan d'exécution explicite avant chaque changement.

## Conséquences
Le workflow CI exécute `terraform fmt -check`, `terraform validate` et tflint sur chaque modification d'`infra/`. Aucune modification manuelle en production.
