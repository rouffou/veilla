# ADR 0015 — Services prévus sans code : abandon d'`identite`, activation des autres à leur lot, abonnements alignés sur le code

## Statut
Proposée le 2026-11-14 par l'architecte solution, à valider par le responsable produit. Référence :
[état des lieux de l'architecture](../architecture/etat-des-lieux-architecture.md), §3.3 et §3.4. Complète l'ADR 0007.

## Contexte
Le catalogue `var.services` (`infra/variables.tf`) déclare cinq services sans code : `identite`, `prevention`,
`prestations`, `psychosocial` et `reporting` (voir `ServicesSansCode` dans
`tests/Sepp.BuildingBlocks.Tests/ConfigurationTerraformTests.cs`). Pour chacun, Terraform crée :
- une Container App ;
- une base, un job et une identité de migration ;
- une rubrique Service Bus et ses abonnements.

Deux de ces services obligent en plus à créer un serveur PostgreSQL entier : `identite` dans la zone plateforme et
`psychosocial` dans la zone psychosociale.

Par ailleurs, Personnes est abonné aux rubriques `integrations` et `affilies` sans aucun gestionnaire. Les messages s'y
accumuleraient, et la règle KEDA risquerait de maintenir le service à son maximum de répliques (hypothèse H3 de l'état
des lieux).

L'ADR 0005 confie l'identité à Entra ID, CSAM et Keycloak, et l'autorisation au socle. L'authentification forte (NF-01)
y est déjà implémentée. Aucun besoin connu n'exige un service `identite`.

## Décision
1. **`identite` est retiré du catalogue.** Le §14.4 (« Identité et autorisation, NF-01, NF-02 ») est couvert par le
   fournisseur d'identité (ADR 0005) et par le socle (`Security.cs`, matrice des droits). Un service ne sera recréé
   que si la délégation des mandats employeur (POR-01) ne peut pas être obtenue de CSAM et de la gestion des accès de
   la sécurité sociale.
2. **Un attribut d'activation est ajouté au catalogue** (par exemple `actif`, `true` par défaut). Un service inactif ne
   crée rien : ni Container App, ni base, ni job, ni identité, ni rubrique, ni abonnement, ni règle KEDA.
   - Les abonnements *vers* un service inactif sont ignorés.
   - Un serveur PostgreSQL n'est créé dans une zone que si un service actif y a une base.
   - Les éléments propres à une zone (environnement Container Apps, coffre, sous-réseau) sont conservés. Ils coûtent
     peu à vide, et ARC-04 doit rester démontrable.
3. **`prevention`, `prestations`, `psychosocial` et `reporting` sont inactifs** jusqu'à la PR qui livre leur premier
   code, qui les active. L'activation suit une procédure courte :
   1. passer l'attribut à `true` ;
   2. amorcer le rôle PostgreSQL (ADR 0010) ;
   3. lancer le rattrapage des projections (ADR 0011).
4. **Les abonnements sont alignés sur les gestionnaires** dans les deux sens, pour les services actifs.
   - Le test existant vérifie que les abonnements du code sont inclus dans ceux de Terraform.
   - Un nouveau test vérifie l'inverse : un abonnement Terraform sans gestionnaire n'est permis que vers un producteur
     inactif.
   - Première application : retirer `integrations` et `affilies` des abonnements de Personnes.
5. **Les rubriques des BFF sont supprimées** (`publishes_events = false` pour `bff-employeur` et `bff-travailleur`). Le
   contrat `bff-employeur.reprise-annoncee.v1` reste dans `src/Contracts` pour la rétrocompatibilité (ADR 0008), mais
   n'a plus de rubrique.
   - Cela suppose de retirer le gestionnaire `RepriseAnnonceeHandler` et l'abonnement d'Obligations, ce que l'ADR 0008
     doit confirmer.
   - **Point à valider par l'architecte, à défaut de quoi on garde la rubrique `bff-employeur`.**

## Alternatives
- **Statu quo.** Il coûte 5 Container Apps, 5 bases, 2 serveurs PostgreSQL et environ 22 abonnements par
  environnement, sans aucun usage. Le risque KEDA sur Personnes reste entier, et `apply` peut échouer faute d'image
  (hypothèse H6). Écarté.
- **Retirer aussi les quatre services de lot du catalogue.** Plus simple, mais on perd la trace de leur zone et de
  leurs abonnements prévus, qui ont été réfléchis. Écarté au profit de l'attribut d'activation.
- **Image « placeholder » pour les services vides.** On paierait l'infrastructure pour rien et on afficherait un service
  vide qui répond. Écarté.

## Conséquences
- **Terraform** :
  - nouvel attribut dans `var.services`, avec ses filtres dans `infra/main.tf` (`db_zones`, `topics`, `subscriptions`,
    `container_app`, `migration_job`, `identity`) ;
  - mise à jour des validations ;
  - aucune ressource de production n'existe encore à supprimer (hypothèse : aucun environnement Azure n'est déployé
    avec ces services). Sinon, le `plan` doit être relu avant tout `apply`, avec confirmation humaine.
- **Tests** : `ServicesSansCode` devient la liste des services inactifs ; nouveau test sur l'inclusion inverse des
  abonnements.
- **Local** : `compose.yaml` ne contient déjà aucun de ces services. Rien ne change pour la démo.
- **Responsables** : `cloud-architect` (conception Terraform), puis `devops-engineer` (réalisation et `plan` sur dev).
