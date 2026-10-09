# ADR 0007 — Infrastructure as Code en Terraform

## Contexte
ARC-51 impose une infrastructure décrite en code et versionnée ; CTR-21 cite Bicep ou Terraform.

## Décision
**Terraform** (provider `azurerm` 4.x, `azapi` pour les ressources non couvertes), état distant dans un compte de stockage Azure, un répertoire par environnement (dev, test, recette, préproduction, production — CTR-22) appelant des modules partagés.

## Justification
Choix de l'équipe ; outil multi-fournisseurs, écosystème de vérification mature (`terraform validate`, tflint, analyse de sécurité), plan d'exécution explicite avant chaque changement.

## Amendement (#295) : règle `$Default` des subscriptions Service Bus
**Contexte.** Azure crée une règle `$Default` (filtre `1=1`) avec chaque subscription ; les règles s'additionnent en OU, donc un filtre par sujet ajouté à côté reste sans effet. `azurerm` 4.x ne peut ni la supprimer ni la remplacer.

**Décision.** Fournisseur `Azure/azapi` (`~> 2.13`, majeures ignorées par Dependabot comme `azurerm`) et une ressource `azapi_update_resource` sur `Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01` nommée `$Default`, avec un filtre SQL `sys.Label IN (...)`, pour les seules subscriptions ayant des `subject_filters`. Le PUT « Rules - Create Or Update » de l'API ARM met à jour une règle existante : `$Default` est écrasée, sans suppression ni étape manuelle.

**Alternatives écartées.**
- Subscription créée par azapi avec une `defaultRuleDescription` : cette propriété n'existe pas dans le modèle ARM de la subscription (API 2024-01-01 et 2026-01-01) ; elle appartient à l'API de données (SDK).
- `azapi_resource` nommée `$Default` : pensée pour créer la ressource ; la règle existant déjà à la création de la subscription, il faudrait l'importer (impossible avant le premier déploiement), et sa destruction supprimerait la règle. `azapi_update_resource`, documenté pour modifier un sous-ensemble des propriétés d'une ressource existante, est le type adapté.
- `azapi_resource_action` de suppression : impératif, sans état, la suppression ne serait pas rejouée ni détectée en cas de dérive ; une subscription sans règle ne reçoit aucun message tant que la règle de filtre n'est pas créée (fenêtre incohérente).
- Étape manuelle `az servicebus topic subscription rule delete` : écartée (CTR-21).

**Conséquences.** `azapi_update_resource` détecte et corrige la dérive du filtre. À la destruction ou si un abonné quitte `subject_filters`, la règle n'est pas rétablie à `1=1` (comportement du type) ; revenir à « tout accepter » exige un `terraform apply` avec une règle explicite ou la recréation de la subscription. L'ancienne règle `filtre-sujets` (azurerm) est détruite au premier déploiement. Vérification après déploiement : `infra/README.md`.

## Conséquences
Le workflow CI exécute `terraform fmt -check`, `terraform validate` et tflint sur chaque modification d'`infra/`. Aucune modification manuelle en production.
