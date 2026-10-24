# ADR 0010 — Migrations de schéma sur Azure : un job de migration par service, une identité de migration dédiée

## Statut
Proposée le 2026-10-24 (à valider par l'architecte). Références : rouffou/veilla#357, #352, #355.

## Contexte
Chaque service applique ses migrations EF Core au démarrage quand `Database:MigrateOnStartup` vaut `true`. C'est le cas en
local (`compose.yaml`) et dans les tests d'intégration. En Azure, la valeur est `false` et rien d'autre n'applique les
migrations : une nouvelle révision démarrerait sur un schéma périmé.

Migrer au démarrage de chaque réplique n'est pas acceptable en Azure :
- l'identité d'exécution devrait détenir les droits DDL. Une fuite de jeton ou une injection permettrait alors de modifier
  le schéma, ou de désactiver les déclencheurs d'ajout seul du journal d'audit (NF-04) ;
- plusieurs répliques migreraient en même temps (CTR-14 : au moins deux en production) ;
- une migration en échec ferait échouer la révision au lieu d'arrêter le déploiement avant elle (ARC-50).

État existant :
- PostgreSQL Flexible n'accepte que l'authentification Entra (`password_auth_enabled = false`).
- `AddSeppPersistence` se connecte avec un jeton `DefaultAzureCredential` renouvelé (`UsePeriodicPasswordProvider`). Le
  rôle PostgreSQL porte le nom de l'identité managée (`POSTGRES__USER`), et `AZURE_CLIENT_ID` désigne cette identité.
- Aucun workflow de déploiement applicatif n'existe : `service-images.yml` publie les images, et Terraform ignore l'image et
  la répartition du trafic des Container Apps (`ignore_changes`).

## Décision
1. **Un job Container Apps de migration par service doté d'une base** (`mig-<service>`, module
   `containerapp-migration-job`) :
   - il tourne dans l'environnement de la zone du service, donc avec accès au point privé PostgreSQL de la zone ;
   - il exécute la même image que le service, avec `Database__MigrateAndExit=true` ;
   - il est déclenché manuellement (`az containerapp job start`) par la chaîne de déploiement, une exécution à la fois, sans
     nouvelle tentative automatique.

   La CI met à jour l'image du job avec la même étiquette que la révision (Terraform ignore l'image).
2. **Mode commun « migrer puis quitter »** (`MigrationSchema.MigrerSchemaAsync`, BuildingBlocks), appelé dans chaque
   `Program.cs` juste après `builder.Build()`, avant toute vérification de clé :
   - les migrations sont appliquées, puis les données de départ idempotentes (paramètres légaux, protocoles, modèles) ;
   - les droits DML sont accordés au rôle d'exécution (`Database:RuntimeRole`) ;
   - le processus s'arrête avec le code 0, ou 1 en cas d'échec ;
   - le journal ne contient que le type et le message de l'erreur. La connexion par jeton ne porte aucun mot de passe.

   `Database:MigrateOnStartup` reste le mode local (migrer puis servir). Sans l'un ni l'autre, le service ne migre pas.
3. **Identité de migration distincte, une par service** (`id-sepp-<env>-<service>-migration`) :
   - son seul rôle Azure est AcrPull : aucun accès Key Vault, Service Bus ni stockage, et le job ne reçoit aucun secret. Les
     clés de chiffrement sont résolues paresseusement, après le point de migration ;
   - son rôle PostgreSQL porte le DDL sur la seule base de son service : `CREATE` sur la base et sur le schéma `public`. Il
     possède les objets qu'il crée ;
   - l'identité d'exécution garde `CONNECT` et `USAGE` sur le schéma. Chaque exécution du job lui accorde `SELECT, INSERT,
     UPDATE, DELETE` sur les tables et `USAGE, SELECT` sur les séquences, avec les droits par défaut pour les objets
     futurs. Elle ne reçoit ni DDL, ni `TRUNCATE`, ni propriété ;
   - une identité par service plutôt que par zone : une identité de migration compromise ne touche qu'une base (ARC-02),
     pour le coût de quelques identités de plus.
4. **Rôles PostgreSQL hors Terraform.** Ni `azurerm` ni la CI, sans accès au réseau privé, ne peuvent exécuter de SQL.
   L'amorçage (`pgaadauth_create_principal_with_oid` et `GRANT`) est fait une fois par base par l'administrateur Entra,
   depuis un poste relié au VNet. Les commandes sont dans `infra/README.md` et sont éprouvées par un test Testcontainers
   (`MigrationSchemaPostgresTests`).
5. **Ordre migration → révision → trafic** (ARC-50) :
   1. figer le trafic sur la révision courante ;
   2. mettre à jour l'image du job et l'exécuter ; attendre `Succeeded`, sinon arrêter le déploiement ;
   3. créer la nouvelle révision (image) et attendre `/health/ready` ;
   4. basculer le trafic, progressivement si besoin ;
   5. désactiver l'ancienne révision.

   Tant qu'aucun workflow de déploiement n'existe, la procédure est décrite dans `infra/README.md`.
6. **Retour arrière sans `Down` automatique** :
   - les migrations sont rétrocompatibles (expand/contract) : une migration n'ajoute que ce que l'ancienne révision ignore.
     On ajoute une colonne nullable ou une table ; on ne renomme ni ne supprime rien dans la même livraison. Le « contract »
     (suppression, contrainte stricte) passe dans une livraison ultérieure, quand plus aucune révision ne lit l'ancien
     schéma ;
   - revenir en arrière consiste à rebasculer le trafic sur l'ancienne révision, qui fonctionne sur le nouveau schéma ;
   - en cas de corruption, on restaure la base à un instant donné (PITR, 7 à 35 jours de rétention, NF-33) sur un nouveau
     serveur, puis on bascule ;
   - aucune migration `Down` n'est exécutée automatiquement : elle peut détruire des données écrites depuis.

## Alternatives
- **Étape du pipeline de déploiement (`dotnet ef database update` ou bundle de migrations depuis l'agent CI).** L'agent
  devrait joindre le point privé PostgreSQL de chaque zone (runner relié au VNet, ouverture des zones sensibles), détenir
  l'identité DDL et embarquer un outil différent de l'image déployée. Écartée. Le pipeline reste l'orchestrateur : il lance
  le job.
- **Migration au démarrage (`MigrateOnStartup=true` en Azure).** Elle donne le DDL à l'identité d'exécution, provoque des
  migrations concurrentes entre répliques et fait échouer la révision plutôt que le déploiement. Écartée.
- **Conteneur d'initialisation (init container) de la Container App.** Il partage l'identité de la révision, donc le DDL,
  et s'exécute à chaque réplique et à chaque mise à l'échelle. Écartée.
- **Identité de migration par zone.** Moins d'identités, mais elle détiendrait le DDL sur toutes les bases de la zone, ce
  qui est contraire à ARC-02. Écartée.
- **Rôles PostgreSQL par Terraform (fournisseur `cyrilgdn/postgresql`).** La CI n'a aucun accès réseau au serveur, et
  l'état Terraform contiendrait des jetons d'administration. Écartée pour l'instant. À reconsidérer avec un runner relié au
  VNet.

## Conséquences
- Chaque service doté d'un `DbContext` appelle `MigrerSchemaAsync` avant toute vérification de clé. Un test
  (`ConfigurationTerraformTests`) le vérifie, et vérifie aussi que le service a un job de migration.
- Terraform crée une identité de migration par service doté d'une base (rôle AcrPull) et un job par service. Il autorise
  `btree_gist` sur chaque serveur (`azure.extensions`), dont la migration de Planification a besoin. Sorties :
  `migration_identities` et `migration_jobs`.
- L'identité d'exécution ne peut plus modifier le schéma ni désactiver les déclencheurs d'ajout seul du journal d'audit.
- Les migrations doivent être rétrocompatibles avec la révision précédente. La revue de code le vérifie (modèle de PR,
  rubrique « Migrations »).
- Le premier déploiement exige l'amorçage des rôles avant la première exécution des jobs (`infra/README.md`, étape 3).
- Le code de sortie du job est le seul signal de réussite. Les journaux du job vont dans Log Analytics, comme ceux de
  l'environnement.
