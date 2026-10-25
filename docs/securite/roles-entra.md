# Provisionnement des rôles d'application Entra ID (NF-02)

Les services valident un jeton dont le claim `roles` porte les noms de rôles de `Roles` (voir
[ADR 0005](../adr/0005-identite-autorisation.md)) ; les permissions en sont déduites par `RolePermissions`. Entra ID est
l'annuaire des internes (MFA par accès conditionnel, NF-01) ; Keycloak joue le même rôle en local
(`deploy/local/keycloak/veilla-realm.json`).

## Déclaration des rôles

Les rôles d'application sont déclarés dans [`entra-app-roles.json`](entra-app-roles.json) (propriété `appRoles` du manifeste de
l'inscription d'application `sepp-api`, l'audience des jetons) :

- **16 rôles `User`** : les 15 profils internes du §3.1 et le rôle `exploitation` (ADR 0011), attribués à des personnes ou à des
  groupes de sécurité.
- **5 rôles `Application`** : les comptes de service `integrations`, `documents`, `documents-medicale`, `communications` et
  `affilies`, attribués aux identités des services (client credentials), jamais à une personne.
- **Non déclarés dans Entra** : `employeur`, `sipp` et `travailleur`, émis par la fédération CSAM (itsme, eID) et non par
  l'annuaire interne ; leur correspondance avec le claim `roles` relève du paramétrage de la fédération (POR-01, POR-10).
- Les identifiants (`id`) sont des UUID v5 dérivés du nom du rôle : stables, reproductibles, sans valeur secrète. Un identifiant
  publié ne change jamais, sinon les attributions existantes seraient perdues.
- Un test (`MatriceDroitsTests`) vérifie que ce manifeste couvre exactement les rôles du code (hors externes) et que le type
  d'attribution (`User` ou `Application`) est cohérent.

## Application au tenant

Le dépôt n'utilise pas le fournisseur Terraform `azuread` (seuls `azurerm` et `storage_use_azuread` sont présents) et les droits
sur le tenant Entra ne sont pas ceux du pipeline d'infrastructure. Le provisionnement est donc **manuel et documenté**, un
ticket de suite est prévu pour l'automatiser :

1. Portail Entra > **Inscriptions d'applications** > `sepp-api` > **Manifeste** : coller le tableau `appRoles` (ou, par la CLI,
   `az ad app update --id <appId> --app-roles @docs/securite/entra-app-roles.json` ; la propriété à transmettre est le tableau seul).
   Pour retirer ou modifier un rôle existant, le désactiver d'abord (`isEnabled: false`), enregistrer, puis le modifier.
2. **Applications d'entreprise** > `sepp-api` > **Propriétés** : « Attribution requise » = oui, afin que seuls les utilisateurs
   et groupes affectés obtiennent un jeton.
3. **Utilisateurs et groupes** : affecter chaque rôle à un **groupe de sécurité** par rôle (par exemple `sepp-role-cpmt`) plutôt
   qu'aux personnes ; l'appartenance au groupe est ce que la revue trimestrielle contrôle ([procédure](revue-des-droits.md)).
4. **Comptes de service** : affecter les rôles `Application` à l'identité managée de chaque service (autorisation d'application
   Graph `appRoleAssignedTo`), jamais de secret client en dépôt.
5. Dans le jeton, le claim `roles` doit contenir les valeurs (`value`) des rôles ; l'audience est `sepp-api` (ARC-40).

## Ticket de suite proposé

Automatiser ce provisionnement par Terraform (`hashicorp/azuread`, ressource `azuread_application` avec blocs `app_role`,
`azuread_group` par rôle, `azuread_app_role_assignment` pour les comptes de service), en générant les blocs depuis
`entra-app-roles.json` ; prérequis : un principal de pipeline distinct avec le droit `Application.ReadWrite.OwnedBy` sur le
tenant, et une décision sur la séparation du state Entra et du state Azure.
