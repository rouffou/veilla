# Infrastructure Azure du logiciel SEPP (Terraform)

Ce dossier décrit en code l'ensemble de l'infrastructure Azure du logiciel métier SEPP
(ARC-51, CTR-21), conformément au §14 du cahier des charges
([docs/cahier-des-charges-sepp.pdf](../docs/cahier-des-charges-sepp.pdf)).

- Plateforme d'exécution : **Azure Container Apps** (CTR-00, décision d'architecture).
- Outil : **Terraform** avec le provider `hashicorp/azurerm` 4.x (épinglé `~> 4.80`).
  Le provider `Azure/azapi` (`~> 2.13`) complète `azurerm` pour la seule règle `$Default`
  des subscriptions Service Bus (ADR 0007, amendement #295) ; tout le reste est en `azurerm`.
- Région principale : **Belgium Central** (`belgiumcentral`), région de reprise UE
  paramétrable (ARC-52).

## Arborescence

```
infra/
├── versions.tf, main.tf, variables.tf, outputs.tf   module racine (composition)
├── modules/
│   ├── network/            VNet, sous-réseaux par zone, NSG, zones DNS privées
│   ├── monitoring/         Log Analytics, Application Insights, groupe d'actions
│   ├── alerting/           règles d'alerte et workbook de supervision (NF-62)
│   ├── registry/           Azure Container Registry Premium privé
│   ├── keyvault/           Key Vault par zone (+ Managed HSM zones sensibles)
│   ├── postgres/           PostgreSQL Flexible Server par zone, une base par service
│   ├── servicebus/         Service Bus Premium, topics, subscriptions, RBAC
│   ├── storage/            Blob Storage documentaire WORM
│   ├── identity/           identités managées par service + RBAC ACR / Key Vault
│   ├── containerapps-env/  environnement Container Apps par zone
│   ├── containerapp/       un microservice (sondes, KEDA, identité, secrets)
│   ├── containerapp-migration-job/  job de migration de schéma d'un service (ADR 0010)
│   ├── apim/               Application Gateway WAF_v2 + API Management interne
│   └── policy/             Azure Policy (registres, accès public, localisation)
└── environments/
    ├── dev/  test/  recette/  preprod/  prod/
    │   ├── main.tf           backend azurerm, provider, appel du module racine
    │   ├── variables.tf      variables de l'environnement
    │   └── terraform.tfvars  valeurs propres à l'environnement
```

Chaque dossier `environments/<env>` est une racine Terraform indépendante, avec son
propre état distant (CTR-22). Les fichiers `main.tf` et `variables.tf` y sont
identiques ; seules les valeurs de `terraform.tfvars` diffèrent.

## Architecture

```
Internet ──► Application Gateway WAF_v2 (IP publique, TLS, OWASP/DRS 2.1, limitation de débit)
                │  HTTPS
                ▼
           API Management (VNet interne) ──► BFF interne / employeur / travailleur, identité
                                              [Container Apps env « platform »]
                                                 │ REST/gRPC (VNet uniquement)
                 ┌───────────────────────────────┼──────────────────────────────┐
                 ▼                               ▼                              ▼
      env « standard »                 env « medicale »              env « psychosociale »
   affiliés, personnes, ...        surveillance-medicale,              psychosocial
   audit, reporting               reintegration
   PostgreSQL std, KV std,        PostgreSQL méd., KV Premium       PostgreSQL psy., KV Premium
   documents, stockage WORM       documents-medicale, stockage      (+ Managed HSM)
   + clé racine documents-kek     WORM + clé racine (+ Managed HSM)
                 └─────────────── Service Bus Premium (point privé, plateforme) ──────┘
```

- **Groupes de ressources** : un par zone de sensibilité (`rg-sepp-<env>-platform`,
  `-standard`, `-medicale`, `-psychosociale`) pour distinguer les droits (ARC-04).
- **Réseau** (VNet /16 par environnement) : un /23 délégué par environnement Container
  Apps, un /26 de points de terminaison privés par zone, un sous-réseau APIM et un
  sous-réseau Application Gateway. NSG sur chaque sous-réseau avec refus explicite de
  tout flux entrant non autorisé ; les zones médicale et psychosociale n'ont aucun
  accès Internet sortant.
- **Flux autorisés** : Internet → Application Gateway → API Management → BFF
  (plateforme) → services des zones ; les points privés d'une zone de données ne sont
  joignables que depuis l'environnement Container Apps de cette zone ; les points
  privés de la plateforme (Service Bus, ACR, Key Vault plateforme) depuis toutes les
  zones. Chaque Container App restreint en plus ses appelants par plage IP.
- **Microservices** : décrits par la variable `services` (catalogue par défaut dans
  `variables.tf`) : zone, CPU/mémoire, bornes d'instances, base de données, topic
  publié, topics souscrits, seuils KEDA, secrets Key Vault. Port 8080, sondes
  `/health/startup`, `/health/live`, `/health/ready`.
- **Événements** : un topic par service publieur (nom du topic = nom du service), une
  subscription par service abonné (nom = service abonné). La matrice d'abonnements par
  défaut suit les gestionnaires d'événements réellement enregistrés dans le code
  (saga de reprise du travail, ARC-33) ; les abonnements prévus pour des services pas
  encore écrits sont conservés avec un commentaire dans `variables.tf`. Un test
  d'architecture par service (`Sepp.<Service>.Architecture.Tests`) échoue si le service
  s'abonne par `AddIntegrationEventHandler` à un topic absent de son `subscribes_to`.
- **Documents : une instance par zone** (décision #356, ADR 0009) : même code et même image
  (`sepp/documents`), une entrée du catalogue par zone qui produit des documents. `documents` (zone standard) et
  `documents-medicale` (zone médicale, `instance_de = "documents"`) ; `documents-psychosociale` suivra quand le service
  Psychosocial produira des documents. Chaque instance a, **dans sa zone**, sa base (`documents` sur le serveur standard,
  `documents_medicale` sur `psql-…-medicale`), son compte de stockage WORM (`stsepp<env><std|med|psy><suffixe>`, point privé
  `pe-<zone>`, conteneur `documents-<zone>`, rôle Blob Data Contributor pour la seule instance de la zone), sa clé racine
  `documents-kek` (module `keyvault`) et son identité managée. L'instance n'a pas de topic propre : elle publie sur le topic
  `documents` par l'émetteur partagé (`shared_topic_senders`) et sa subscription porte son nom
  (`SERVICEBUS__SUBSCRIPTIONNAME`). Aucun service ne référence le coffre d'une autre zone et aucune règle NSG n'ouvre la zone
  standard vers les zones médicale et psychosociale (tests `ConfigurationTerraformTests`).
- **Filtres par sujet** : `subject_filters` (topic -> sujets) crée une règle SQL
  `sys.Label IN (...)` par subscription filtrée. Le sujet du message est le nom versionné
  du contrat (`audit.bris-de-glace-signale.v1`, ADR 0004). Utilisé pour limiter
  Communications au bris de glace du topic `audit` et pour réserver
  `personnes.etat-particulier-declare.v1` à Obligations (une validation Terraform impose
  aux autres abonnés du topic `personnes` de déclarer leurs sujets).

### Règle `$Default` des subscriptions filtrées

Azure crée avec chaque subscription une règle `$Default` (filtre vrai) ; les règles
s'additionnent en OU, donc un filtre ajouté à côté serait sans effet. Pour chaque
subscription ayant des `subject_filters`, le module `servicebus` **remplace le contenu de
`$Default`** par le filtre SQL `sys.Label IN (...)` avec `azapi_update_resource`
(`Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01`, PUT « create or
update » de l'API ARM). Aucune étape manuelle, aucune suppression ni recréation de règle
(décision : ADR 0007, amendement). Les autres subscriptions gardent `$Default` (tous les
messages). Le provider `azapi` est configuré dans chaque environnement
(`subscription_id`, ou `ARM_SUBSCRIPTION_ID`) et exige les mêmes droits que `azurerm`.

Points d'attention :

- retirer un abonné de `subject_filters` ne rétablit pas `1=1` (la destruction de
  `azapi_update_resource` ne fait rien) : écrire alors la règle `$Default` voulue
  explicitement ou recréer la subscription ;
- la règle `filtre-sujets` (azurerm) du premier déploiement de la saga #264 est détruite
  lors du premier `apply` suivant ce changement.

Vérification après déploiement (une seule règle, `$Default`, de type `SqlFilter`) :

```bash
az servicebus topic subscription rule list --resource-group <rg> --namespace-name <ns> \
  --topic-name audit --subscription-name communications \
  --query "[].{nom:name, type:filterType, filtre:sqlFilter.sqlExpression}" -o table
```

Résultat attendu : `$Default  SqlFilter  sys.Label IN ('audit.bris-de-glace-signale.v1')`.
Idem pour `personnes` / `postes-risques` (`personnes.affectation-modifiee.v1`). Le `terraform
plan` d'un nouvel environnement n'a pu être validé que par `terraform validate` (pas
d'accès Azure en CI) : contrôler le résultat réel par la commande ci-dessus.

## Configuration injectée dans les Container Apps (CTR-05, CTR-16)

Aucune configuration ne figure dans l'image : `main.tf` compose, par service, les variables d'environnement
(`local.app_environment`) et les références Key Vault (`local.service_key_vault_secrets`). Un test
(`tests/Sepp.BuildingBlocks.Tests/ConfigurationTerraformTests.cs`) échoue si une clé obligatoire lue par le code n'est
pas fournie par Terraform.

| Clés | Source | Services |
|---|---|---|
| `SEPP__SERVICE`, `SEPP__ZONE`, `SEPP__ENVIRONNEMENT`, `AZURE_CLIENT_ID`, `KEYVAULT__URI`, `SERVICEBUS__FULLYQUALIFIEDNAMESPACE` | racine | tous |
| `POSTGRES__HOST`, `POSTGRES__DATABASE`, `POSTGRES__USER` | module `postgres` et identité du service | services avec base |
| `Authentication__Authority`, `Authentication__Audience` | `var.entra_tenant_id` (émetteur `https://login.microsoftonline.com/<tenant>/v2.0`), `var.authentication_audience` | tous |
| `SEPP__SERVICES__<SERVICE>` | adresses internes `https://ca-<service>.<domaine de la zone>` | BFF |
| `SERVICEBUS__SUBSCRIPTIONNAME` | nom du service ou de l'instance (la subscription porte ce nom) | tous |
| `<Section>__ServicesInternes__*` | attribut `internal_services` du catalogue | affilies, communications, documents, documents-medicale, integrations |
| `<Préfixe>__TokenEndpoint`, `__ClientId`, `__Scope` ; secret `__ClientSecret` | attribut `compte_technique`, `var.technical_account_client_ids`, Key Vault de la zone | affilies, communications, documents, documents-medicale, integrations |
| `<Préfixe>__Enveloppe__Type` (`KeyVault`), `<Préfixe>__Enveloppe__CleKek` (URI de `<service>-fields-kek` sans version), `<Préfixe>__CurrentKeyId` ; secrets `<Préfixe>__Deks__<id>` (DEK enveloppées) et, en lecture seule, `<Préfixe>__Keys__<id>` (format v1) | attribut `encryption`, module `keyvault` de la zone **du service**, `var.encryption_key_ids`, `var.encryption_legacy_key_ids` (ADR 0012) | personnes, integrations, surveillance-medicale, reintegration |
| `DOCUMENTS__ZONES` (et `DOCUMENTS__ZONES__0`), `DOCUMENTS__ENVELOPPE__TYPE` (`KeyVault`), `DOCUMENTS__ENVELOPPE__CLEKEK` (URI de `documents-kek` sans version), `DOCUMENTS__BLOBENDPOINT` | zone de l'instance, module `keyvault` et module `storage` de la zone | documents, documents-medicale |
| `BLINDINDEX__CLEENVELOPPEE` | `key_vault_secrets` du catalogue (secret `blindindex--cleenveloppee`, clé de l'index aveugle enveloppée par `personnes-fields-kek`) | personnes |
| `<Section>__Adaptateurs__<Port>` | attribut `adapters` du catalogue, `var.adapter_mode` | communications, documents, documents-medicale, integrations, planification |
| `DATABASE__MIGRATEANDEXIT` (`true`), `DATABASE__RUNTIMEROLE` ; `POSTGRES__USER` et `AZURE_CLIENT_ID` de l'identité de migration | `local.migration_environment` (job `mig-<service>` seulement, aucun secret) | services avec base |

Un secret est exposé au conteneur sous le nom du secret en majuscules, `-` devenant `_` : le secret
`personnes--encryption--deks--d1` devient `ENCRYPTION__DEKS__D1`, soit la clé `Encryption:Deks:D1` de la configuration .NET.

### PostgreSQL par identité managée

Le serveur n'accepte aucun mot de passe (`password_auth_enabled = false`). Au démarrage, `AddSeppPersistence` lit
`ConnectionStrings:<service>` (développement local, tests) ou, à défaut, `POSTGRES__HOST`, `POSTGRES__DATABASE` et
`POSTGRES__USER` ; le mot de passe est un jeton Entra (portée `https://ossrdbms-aad.database.windows.net/.default`) obtenu
par `DefaultAzureCredential` (identité de `AZURE_CLIENT_ID`) et renouvelé toutes les 25 minutes. TLS est obligatoire
(`SslMode=Require`). `POSTGRES__USER` est le nom de l'identité managée (`id-sepp-<env>-<service>`), qui doit exister comme rôle
PostgreSQL (étape 3 de l'amorçage).

### Migrations de schéma (ADR 0010)

La révision d'un service ne migre jamais son schéma en Azure : son identité (`id-sepp-<env>-<service>`) n'a que les droits
DML. Chaque service doté d'une base a un **job Container Apps de migration** `mig-<service>` (module
`containerapp-migration-job`) dans l'environnement de sa zone :

- même image que le service, avec la même configuration non secrète, et `Database__MigrateAndExit=true` : le service
  applique ses migrations EF Core et ses données de départ, accorde les droits DML au rôle d'exécution
  (`Database__RuntimeRole`), puis s'arrête avec le code 0, ou 1 en cas d'échec ;
- identité de migration dédiée `id-sepp-<env>-<service>-migration` (sortie `migration_identities`). Son seul rôle Azure est
  AcrPull, et elle ne reçoit aucun secret Key Vault ;
- déclenchement manuel (`az containerapp job start`), une exécution à la fois, sans nouvelle tentative automatique, délai
  maximal de 30 minutes ;
- Terraform ignore l'image du job comme celle de la Container App : la chaîne de déploiement la met à jour.

Le paramètre serveur `azure.extensions` autorise `BTREE_GIST` (variable `allowed_extensions` du module `postgres`). La
migration de Planification crée cette extension de confiance avec l'identité de migration.

#### Rôles PostgreSQL (amorçage, une fois par base)

Terraform ne peut pas créer de rôle PostgreSQL : `azurerm` n'exécute pas de SQL, et la CI n'a aucun accès au réseau privé.

- **Qui** : un membre du groupe administrateur Entra du serveur (`postgres_entra_admin`).
- **Où** : un poste relié au VNet, connecté par jeton Entra
  (`PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)`, `sslmode=require`).
- **Valeurs** : `principal_id` des sorties `service_identities` et `migration_identities` ; `<base>` est le nom du service
  avec `_` au lieu de `-`.

```sql
-- 1. Base « postgres » du serveur de la zone : rôles des deux identités du service.
select * from pgaadauth_create_principal_with_oid('id-sepp-<env>-<service>', '<principal_id exécution>', 'service', false, false);
select * from pgaadauth_create_principal_with_oid('id-sepp-<env>-<service>-migration', '<principal_id migration>', 'service', false, false);

-- 2. Base « postgres » : accès à la seule base du service (ARC-02).
REVOKE CONNECT ON DATABASE <base> FROM PUBLIC;
GRANT CONNECT, TEMPORARY, CREATE ON DATABASE <base> TO "id-sepp-<env>-<service>-migration";
GRANT CONNECT ON DATABASE <base> TO "id-sepp-<env>-<service>";

-- 3. Dans la base du service (\c <base>) : schéma public.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO "id-sepp-<env>-<service>-migration";
GRANT USAGE ON SCHEMA public TO "id-sepp-<env>-<service>";
```

Effets de ces commandes :

- **Identité de migration** : elle possède les objets qu'elle crée.
- **Identité d'exécution** : elle reçoit `SELECT, INSERT, UPDATE, DELETE` sur les tables et `USAGE, SELECT` sur les
  séquences, avec les droits par défaut des objets futurs. Le job lui accorde ces droits à chaque exécution : elle ne perd
  donc pas son accès DML quand une migration ajoute une table. Elle n'a ni DDL, ni `TRUNCATE`, ni propriété.
- **Test** : ces commandes, aux noms des rôles près, sont rejouées par `MigrationSchemaPostgresTests` (PostgreSQL 17).

#### Déploiement d'une nouvelle version (ARC-50)

Le workflow `deploy.yml` automatise cet enchaînement (voir « Déploiement progressif »). Procédure manuelle équivalente,
pour l'exploitant depuis un poste relié au VNet : **migration → révision → trafic** pour chaque service touché :

```bash
rg=rg-sepp-<env>-<zone>; svc=<service>; suffixe=<suffixe-de-revision>   # minuscules, chiffres et tirets
image=<acr>.azurecr.io/sepp/<image>:<version>   # <image> = service d'origine pour une instance (documents-medicale -> documents)
courante=$(az containerapp revision list -g "$rg" -n "ca-$svc" --query "[?properties.trafficWeight>\`0\`].name | [0]" -o tsv)

# 1. Figer le trafic sur la révision courante (la nouvelle révision démarre sans trafic).
az containerapp ingress traffic set -g "$rg" -n "ca-$svc" --revision-weight "$courante=100"

# 2. Migrer : même image que la future révision ; arrêt du déploiement si l'exécution échoue.
az containerapp job update -g "$rg" -n "mig-$svc" --image "$image"
execution=$(az containerapp job start -g "$rg" -n "mig-$svc" --query name -o tsv)
until statut=$(az containerapp job execution show -g "$rg" -n "mig-$svc" --job-execution-name "$execution" --query properties.status -o tsv) \
      && [ "$statut" != "Running" ] && [ "$statut" != "Processing" ]; do sleep 10; done
[ "$statut" = "Succeeded" ] || { echo "Migration en échec ($statut) : déploiement arrêté"; exit 1; }

# 3. Nouvelle révision, puis contrôle de /health/ready (état « Healthy » de la révision).
az containerapp update -g "$rg" -n "ca-$svc" --image "$image" --revision-suffix "$suffixe"

# 4. Bascule du trafic (progressive si besoin), puis désactivation de l'ancienne révision.
az containerapp ingress traffic set -g "$rg" -n "ca-$svc" --revision-weight "ca-$svc--$suffixe=100"
az containerapp revision deactivate -g "$rg" -n "ca-$svc" --revision "$courante"
```

Journaux d'une exécution : `az containerapp job logs show -g "$rg" -n "mig-$svc" --execution "$execution" --container migration`,
ou table `ContainerAppConsoleLogs_CL` de Log Analytics.

#### Retour arrière

- **Expand/contract** : chaque migration est rétrocompatible avec la révision précédente. Elle ajoute une colonne nullable
  ou une table, et ne renomme ni ne supprime rien dans la même livraison. Les suppressions viennent dans une livraison
  ultérieure.
- **Revenir en arrière** consiste à rebasculer le trafic sur l'ancienne révision, réactivée si besoin
  (`az containerapp revision activate`). Le schéma reste en place.
- **Aucune migration `Down` n'est exécutée automatiquement.**
- **Schéma ou données corrompus** : restaurer la base à un instant donné avant la migration (PITR, rétention
  `postgres_backup_retention_days` : 7 jours hors préproduction et production, 35 jours en préproduction et production) sur
  un nouveau serveur (`az postgres flexible-server restore --restore-time …`). Ensuite, rediriger les services
  (`POSTGRES__HOST`) ou recopier la base. Relever l'heure de début de chaque exécution du job pour pouvoir choisir l'instant
  de restauration.

### Secrets applicatifs (créés hors du dépôt)

Terraform ne contient **aucune valeur secrète** et ne crée pas les secrets : il les référence (`key_vault_secret_id` sans
version, lus avec l'identité du service). Les secrets doivent exister **avant** le déploiement des Container Apps (une
référence vers un secret absent fait échouer l'`apply`). Les Key Vault n'ayant aucun accès public, les créer depuis un
agent relié au VNet, avec le rôle *Key Vault Secrets Officer* sur le coffre de la zone. Valeurs :

- clé de données (DEK) du chiffrement des champs : **enveloppée** par la clé racine du service (voir « Clés racines du
  chiffrement des champs »), jamais en clair ;
- clé de l'index aveugle du NISS : idem, enveloppée par `personnes-fields-kek` ;
- secret d'un compte technique : le secret client de l'application Entra du service.

| Secret | Coffre | Service |
|---|---|---|
| `personnes--encryption--deks--<id>` | standard | personnes |
| `integrations--encryption--deks--<id>` | standard | integrations |
| `blindindex--cleenveloppee` | standard | personnes |
| `surveillance-medicale--zonemedicale--encryption--deks--<id>` | médicale | surveillance-medicale |
| `reintegration--zonemedicale--encryption--deks--<id>` | médicale | reintegration |
| `<mêmes préfixes>--keys--<id>` (anciennes clés AES du format v1, lecture seule, seulement pour les identifiants de `encryption_legacy_key_ids`) | zone du service | idem |
| `affilies--comptetechnique--clientsecret`, `communications--comptetechnique--clientsecret`, `documents--comptetechnique--clientsecret`, `integrations--personnes--clientsecret` | standard | affilies, communications, documents, integrations |
| `documents-medicale--comptetechnique--clientsecret` | médicale | documents-medicale |

Les anciens secrets `documents--chiffrement--<zone>--encryption--keys--<id>` n'existent plus : les clés AES de Documents,
exportables, sont remplacées par une clé racine non exportable par zone (voir « Clés racines de Documents »). Ne pas les
créer ; ceux déjà créés dans un coffre peuvent être supprimés.

Le secret Key Vault d'une clé de chiffrement est préfixé par le nom du service (une clé par service) ; la variable
d'environnement injectée reste `ENCRYPTION__DEKS__<ID>` (ou le préfixe propre au service).

### Clés racines du chiffrement des champs (ADR 0012)

Personnes, Intégrations, Surveillance médicale et Réintégration chiffrent leurs champs sensibles (`FieldEncryptor`,
AES-256-GCM, format `v2`) avec une clé de données (DEK) **enveloppée** par la clé racine `<service>-fields-kek` du
service. Terraform crée cette clé dans le coffre de la zone du service (module `keyvault`, `field_kek_names`), sur le
modèle de `documents-kek` : RSA-HSM 3072 en zone sensible (Key Vault Premium, ou Managed HSM si `managed_hsm_enabled`),
RSA 3072 en zone standard, `wrapKey`/`unwrapKey`, rotation automatique tous les 12 mois (`field_kek_rotation_period`),
sans expiration. L'identité du service reçoit *Key Vault Crypto Service Encryption User* **sur sa clé** (variante Managed
HSM : rôle local sur `/keys/<service>-fields-kek`) ; aucun rôle cryptographique sur le coffre. Le service reçoit l'URI de
la clé sans version (`<Préfixe>__Enveloppe__CleKek`, sorties `field_kek_uris`) et déballe ses DEK **au démarrage** (un
appel par DEK, aucun par valeur) ; il refuse de démarrer si une DEK ne se déballe pas.

Création d'une DEK (ou de la clé enveloppée de l'index aveugle), depuis un agent relié au VNet, avec la lecture de la clé
(*Key Vault Crypto Officer* ou *Reader* sur la clé) et *Key Vault Secrets Officer* sur le coffre. La DEK est tirée et
enveloppée avec la **clé publique** dans un même tube : elle n'est jamais écrite ni affichée en clair.

```bash
KV=<kv-zone> ; SERVICE=personnes ; PREFIXE=encryption ; ID=d1   # zonemedicale--encryption pour la zone médicale
KEK="$SERVICE-fields-kek"
KID=$(az keyvault key show --vault-name "$KV" --name "$KEK" --query key.kid -o tsv)      # version courante (kid versionné)
az keyvault key download --vault-name "$KV" --name "$KEK" --encoding PEM --file kek.pub.pem   # clé publique seulement
ENVELOPPEE=$(openssl rand 32 | openssl pkeyutl -encrypt -pubin -inkey kek.pub.pem \
  -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256 -pkeyopt rsa_mgf1_md:sha256 | base64 -w0)
az keyvault secret set --vault-name "$KV" --name "$SERVICE--$PREFIXE--deks--$ID" --value "$KID|$ENVELOPPEE" >/dev/null
```

Avec un Managed HSM, lire la clé par `--hsm-name <hsm>` au lieu de `--vault-name` (le secret reste dans le Key Vault).
Le `kid` doit être celui de la version dont la clé publique a été téléchargée (ne pas lancer ces commandes pendant une
rotation).

Index aveugle du NISS : la clé **existante** est enveloppée telle quelle, pour que l'index et la recherche par NISS ne
changent pas ; dans un environnement neuf, remplacer la première commande du tube par `openssl rand 32`.

```bash
az keyvault secret show --vault-name "$KV" --name blindindex--key --query value -o tsv | base64 -d \
  | openssl pkeyutl -encrypt -pubin -inkey kek.pub.pem -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256 -pkeyopt rsa_mgf1_md:sha256 \
  | base64 -w0 | { read -r E; az keyvault secret set --vault-name "$KV" --name blindindex--cleenveloppee --value "$KID|$E" >/dev/null; }
```

Rotation :

- **clé racine** : automatique ; une DEK enveloppée garde son kid versionné et reste déballable. Comme pour
  `documents-kek`, ne jamais désactiver ni supprimer une version tant qu'une DEK l'utilise : le service ne démarrerait
  plus. Rien à faire côté DEK (une nouvelle DEK utilisera la nouvelle version) ;
- **DEK** : créer `--deks--<nouvel id>`, puis placer l'identifiant **en tête** de `encryption_key_ids` en conservant les
  anciens (déchiffrement) ; retirer un identifiant seulement quand plus aucune donnée ne l'utilise
  (`FieldEncryptor.NeedsRotation`). Identifiants en majuscules et chiffres (`D1`, `D2`) ;
- **clés AES historiques (format v1)** : `encryption_legacy_key_ids` les injecte en lecture seule (`--keys--<id>`) ; les
  vider, puis supprimer les secrets `--keys--` et `blindindex--key` (dernières copies exportables), une fois toutes les
  valeurs v1 rechiffrées (une valeur v1 est réécrite en v2 lorsque le champ est modifié ; aucun travail de rechiffrement
  global n'existe encore).

Sauvegarde : comme pour `documents-kek` (ci-dessous), **toutes versions incluses**, avant la mise en service puis après
chaque rotation. La perte de `<service>-fields-kek` rend illisibles les champs chiffrés du service.

### Clés racines de Documents (décision #356, ADR 0009)

Chaque instance de Documents chiffre chaque document par enveloppe : une clé AES-256 aléatoire par document, enveloppée
(`wrapKey`, RSA-OAEP-256) par la clé racine `documents-kek` de **sa zone**. Terraform crée cette clé (module `keyvault`,
ressource ARM `Microsoft.KeyVault/vaults/keys@2023-07-01` par azapi) :

- RSA-HSM 3072 dans le Key Vault Premium des zones sensibles, ou dans le Managed HSM si `managed_hsm_enabled`
  (`Microsoft.KeyVault/managedHSMs/keys@2023-07-01`) ; RSA 3072 dans le coffre standard ; opérations `wrapKey` et `unwrapKey` ;
- rotation automatique tous les 12 mois (`documents_kek_rotation_period`, `P12M`), **sans expiration des versions** : l'objet
  archivé porte l'identifiant versionné de la clé et les anciennes versions restent utilisables pour déballer ;
- l'identité de l'instance reçoit *Key Vault Crypto Service Encryption User* **sur la clé** (pas sur le coffre) ; avec un
  Managed HSM, le rôle local *Managed HSM Crypto Service Encryption User* (`33413926-3206-4cdd-b39a-83574fe37a17`) sur
  `/keys/documents-kek` (le HSM doit être activé, voir ci-dessous) ;
- `DOCUMENTS__ENVELOPPE__CLEKEK` vaut l'URI de la clé sans version ; sorties `documents_kek_uris`.

Un `apply` qui modifie le corps de la clé crée une **nouvelle version** (comportement de l'API ARM) : les anciennes
versions restent valides.

**Rotation : ne jamais désactiver ni supprimer une version de `documents-kek` tant que des archives l'utilisent** (#377).
Chaque archive porte le kid de la version qui a enveloppé sa clé de données ; la rotation ajoute une version, elle n'en
retire aucune. Ne pas poser de date d'expiration, ne pas désactiver (`az keyvault key set-attributes --enabled false`) ni
supprimer une ancienne version, et ne pas retirer à l'identité de l'instance son rôle sur la clé. Documents traite ces cas
comme une erreur d'exploitation, pas comme une altération : l'API répond `503 document.cle-racine-indisponible` et journalise
en erreur le kid concerné (404 du coffre : version supprimée ou purgée ; 403 : accès refusé ou version désactivée). Pour
rétablir la lecture : récupérer la clé supprimée (`az keyvault key recover`, dans les 90 jours), réactiver la version, ou
rétablir le rôle ; après purge, restaurer la sauvegarde ci-dessous.

**La perte d'une clé racine rend illisibles toutes les archives de la zone** (les clés de données ne sont récupérables que
par elle). Les coffres ont la protection contre la purge (90 jours de suppression récupérable) ; la sauvegarde ci-dessous
est en plus **obligatoire** avant toute mise en service, puis après chaque rotation.

Sauvegarde, par zone :

1. **Key Vault** (standard, ou Premium des zones sensibles sans Managed HSM) : sauvegarde de la clé, **toutes versions
   incluses**, par le plan de données (depuis un agent relié au VNet, rôle *Key Vault Administrator* ou *Crypto Officer*) :

   ```bash
   az keyvault key backup --vault-name <kv-zone> --name documents-kek --file documents-kek-<zone>-<date>.keybackup
   ```

   Le fichier est chiffré, ne peut être restauré que dans un coffre **de la même géographie Azure et de la même
   souscription** (`az keyvault key restore`) : le stocker dans un compte de stockage ou un coffre d'archivage de cette
   géographie (Belgique/UE), jamais hors de l'UE, avec accès restreint et journalisé. Répéter après chaque rotation
   (tous les 12 mois, ou après un `apply` qui crée une version). La restauration d'une clé supprimée et purgée se fait dans
   le même coffre ou un coffre de la même géographie ; la clé redevient utilisable avec les mêmes versions.
2. **Managed HSM** : la clé est couverte par la sauvegarde complète du HSM et par son **domaine de sécurité** :
   - télécharger le domaine de sécurité à l'activation (voir « Managed HSM ») et le conserver chiffré, hors ligne, avec ses
     détenteurs de clés de quorum répartis (au moins deux personnes ; ne jamais conserver le quorum au même endroit) ;
   - sauvegarde complète du HSM vers un compte de stockage de la même géographie (`az keyvault backup start --hsm-name
     <hsm> --storage-account-name … --blob-container-name …`) après la création de la clé et après chaque rotation ;
   - sans le domaine de sécurité, une sauvegarde de HSM ne peut pas être restaurée : tester la restauration dans un HSM
     de recette avant la production.
3. Documenter dans le registre d'exploitation : date de la dernière sauvegarde de chaque clé, emplacement, détenteurs du
   quorum. Une restauration de la clé d'une zone doit être répétée une fois par an (relecture d'une archive de test).

La rotation du coffre standard s'applique de la même façon (clé logicielle) ; ce n'est pas un substitut à la sauvegarde.

Le service Documents d'une zone ne lit jamais le coffre d'une autre zone (ARC-04, ARC-06) : les anciennes références
inter-zones (`additional_key_vault_zones`) sont supprimées et la validation de `services` refuse `encryption` vers une
autre zone.

### CORS des BFF de portail (#358)

`var.portal_origins` (obligatoire, par environnement dans `terraform.tfvars`) liste les origines des portails autorisées sur
chaque BFF : `bff-employeur` et `bff-travailleur`. Chaque origine est de la forme `https://hôte[:port]` (domaine public servi
par l'Application Gateway ou APIM, ou domaine du portail), sans chemin, sans barre finale ni joker. La validation Terraform
refuse `*`, toute origine non https, une liste vide et un nom de BFF inconnu. Les origines sont injectées en
`CORS__ORIGINESAUTORISEES__0`, `__1`… sur le BFF concerné seulement (un BFF n'accepte pas le portail de l'autre). Au démarrage,
hors Development et Test, le BFF refuse une liste vide ou une origine invalide (`PortailCors` dans
`Sepp.BuildingBlocks.Web`). La politique n'autorise ni credentials (jeton porteur, pas de cookie) ni méthode autre que GET et
POST. Les valeurs des `terraform.tfvars` sont des exemples (`*.sepp.example.be`) à remplacer.

### Adaptateurs externes : simulateurs hors production

`var.adapter_mode` (`Simulateur` par défaut) fixe le choix `Reel` ou `Simulateur` de chaque adaptateur déclaré dans
`adapters` ; `Reel` n'est accepté par la validation Terraform qu'en production. Les `appsettings.json` choisissent
`Simulateur` par défaut. Au démarrage, un adaptateur `Reel` encore à l'état de squelette fait échouer le service avec un
message clair hors production (`SEPP__ENVIRONNEMENT` différent de `prod` ou absent) : `GardeAdaptateurs` dans
`Sepp.BuildingBlocks.Infrastructure`. En production, `Reel` reste à implémenter avant le premier déploiement (README des
services Intégrations, Documents, Planification, Communications). Surveillance médicale n'a pas d'adaptateur réel : son choix
`Simulateur` est fixe.

### À vérifier sur Azure (#282)

Ce câblage n'a été validé que par `terraform fmt` / `validate`, un `terraform apply` des seules expressions locales (sans
ressource) et les tests .NET : aucun `plan` ni `apply` réel n'a été exécuté.

- les secrets de chaque zone sont créés avant l'`apply` et les noms correspondent (`az keyvault secret list`) ;
- le rôle PostgreSQL de chaque identité existe et la connexion par jeton aboutit (`/health/ready` vert) ;
- l'émetteur et l'audience des jetons correspondent aux applications Entra (v2.0 : `iss` = `Authority`, `aud` = `Audience`) ;
- les comptes techniques obtiennent un jeton (`<audience>/.default`) portant le rôle attendu (`affilies`, `communications`, `documents`, `integrations`) ;
- chaque instance de Documents (`documents`, `documents-medicale`) ne lit que le coffre, le stockage et la base de sa zone : le rôle *Key Vault Crypto Service Encryption User* sur `documents-kek` est propagé, `wrapKey`/`unwrapKey` aboutissent depuis l'environnement de la zone par le point privé `pe-<zone>`, et l'écriture Blob dans `documents-<zone>` aussi ;
- **le principal de déploiement peut créer les clés par ARM** : droit `Microsoft.KeyVault/vaults/keys/write` (rôle *Key Vault Contributor* ou équivalent) sur chaque coffre, et `Microsoft.KeyVault/managedHSMs/keys/write` plus le rôle local *Managed HSM Crypto User* ou *Crypto Officer* avec un Managed HSM activé ; la création de la clé et de sa politique de rotation (`P12M`, sans expiration) par l'API ARM `2023-07-01` n'a pas été exécutée sur Azure ;
- la variante Managed HSM : l'`apply` des clés et des rôles locaux échoue tant que le HSM n'est pas activé (domaine de sécurité) ; l'ordre « apply sans clé → activation → apply » est à répéter en recette ;
- le compte de stockage renommé `stsepp<env><std|med|psy><suffixe>` remplace l'ancien `stsepp<env><suffixe>` (remplacement de ressource : à ne pas appliquer sur un environnement qui contient déjà des archives, surtout WORM verrouillé) ;
- `Documents:Zones` est injecté sous forme scalaire (`DOCUMENTS__ZONES`) et indexée (`DOCUMENTS__ZONES__0`) pour la liaison de tableau .NET : à confirmer avec le code de l'instance ;
- les sauvegardes des clés racines de chaque zone sont réalisées et leur restauration testée (voir « Clés racines de Documents ») ;
- les adresses internes `https://ca-<service>.<domaine>` résolvent depuis les environnements appelants et les restrictions d'IP d'entrée les autorisent ;
- les jobs de migration `mig-<service>` (ADR 0010), à vérifier sur chaque environnement :
  - leur création ;
  - l'amorçage des rôles PostgreSQL (`pgaadauth_create_principal_with_oid` avec le `principal_id`), ainsi que les `GRANT`
    sur une base créée par ARM, exécutés par l'administrateur Entra ;
  - le paramètre `azure.extensions` (`BTREE_GIST`) et la création de l'extension par l'identité de migration ;
  - une première exécution `Succeeded` ;
  - l'accès DML de l'identité d'exécution qui s'ensuit (`/health/ready` vert) ;
- les valeurs d'exemple des `terraform.tfvars` (`entra_tenant_id`, `technical_account_client_ids`, `authentication_audience`) sont remplacées ;
- l'authentification des portails externes (fédération CSAM, ADR 0005) n'est pas encore injectée ;
- les origines de `portal_origins` correspondent aux domaines réellement exposés (Application Gateway, APIM ou domaine du portail), et un préflight `OPTIONS` depuis chaque portail reçoit `Access-Control-Allow-Origin` à l'identique alors qu'une autre origine n'en reçoit pas.

## Correspondance avec les exigences

| Exigence | Mise en œuvre |
|---|---|
| CTR-00 | Azure Container Apps pour l'ensemble des conteneurs (module `containerapps-env`, `containerapp`). |
| CTR-05 | Configuration injectée par variables d'environnement et références Key Vault ; image identique d'un environnement à l'autre. |
| CTR-07 | ACR Premium, accès public désactivé, point privé, points de données dédiés, rétention des manifestes non étiquetés, export désactivé. |
| CTR-10 | Un environnement Container Apps par zone (platform, standard, medicale, psychosociale), chacun dans son sous-réseau délégué, profils de charge. |
| CTR-11 | NSG « refus par défaut », points de terminaison privés + zones DNS privées pour PostgreSQL, Service Bus, Blob, ACR, Key Vault, Managed HSM ; environnements internes ; mTLS entre applications. |
| CTR-12 | Sondes startup/liveness/readiness, délai d'arrêt propre (`termination_grace_period_seconds`). |
| CTR-13 | CPU/mémoire par service ; règles KEDA HTTP et `azure-servicebus` (identité managée). |
| CTR-14 | `min_replicas` ≥ 2 imposé en prod ; redondance de zone (environnements, ACR, PostgreSQL HA, APIM, Application Gateway) activable. |
| CTR-15 | Services sans état ; PostgreSQL, Service Bus et Blob managés et sauvegardés ; `prevent_destroy` sur les bases. |
| CTR-16 | Identité managée user-assigned par service ; RBAC AcrPull, Key Vault Secrets User / Crypto User, Service Bus Data Sender / Receiver, Storage Blob Data Contributor ; SAS et clés partagées désactivés ; PostgreSQL en authentification Entra seule ; aucune valeur secrète en variable ou en sortie non sensible. |
| CTR-17 | Azure Policy : registres autorisés (définition personnalisée), accès public désactivé (ACR, Key Vault, PostgreSQL, Service Bus, Storage, environnements Container Apps), localisations autorisées. |
| CTR-21 / ARC-51 | Toute l'infrastructure en Terraform versionné. |
| CTR-22 | Racines et états séparés : dev, test, recette, preprod (topologie de prod), prod. |
| ARC-02 | Un serveur PostgreSQL par zone, une base par service. |
| ARC-04 | Groupes de ressources, sous-réseaux, bases, Key Vault / HSM et droits distincts par zone. |
| ARC-05 / ARC-31 | Service Bus Premium, détection des doublons, lettres mortes, 10 tentatives. |
| ARC-42 | Application Gateway WAF_v2 (Microsoft_DefaultRuleSet 2.1, Bot Manager, limitation de débit par IP, TLS 1.2+) devant API Management interne. |
| ARC-43 | Seuls les BFF sont joignables par l'API Management (backends APIM par BFF). |
| ARC-44 | Key Vault par zone ; Managed HSM dédié aux zones médicale et psychosociale (`managed_hsm_enabled`), Key Vault Premium (clés HSM) à défaut. |
| ARC-45 | Rôle Key Vault Crypto User pour le chiffrement applicatif des seuls services qui déclarent `encryption` ; Documents : clé racine `documents-kek` par zone (RSA-HSM, rotation 12 mois), rôle *Crypto Service Encryption User* sur la clé. |
| ARC-46 | mTLS Container Apps, segmentation NSG par zone, restrictions IP d'entrée. |
| ARC-47 / NF-62 | Log Analytics, Application Insights (OpenTelemetry), paramètres de diagnostic sur toutes les ressources, groupe d'actions, règles d'alerte et workbook (section « Supervision et alertes »). |
| ARC-50 | Révisions multiples ; image et répartition du trafic pilotées par la CI/CD (`ignore_changes`) ; job de migration par service, exécuté avant la nouvelle révision (ADR 0010). |
| ARC-52 | `dr_location` (région UE) ; géo-réplication ACR activable (`enable_dr_replication`). |
| ARC-53 | Voir « Vérifications de disponibilité » ci-dessous. |
| NF-13 | Stratégie « localisations autorisées » limitée à la région principale et à la région de reprise UE. |
| NF-21 | Un compte de stockage par zone d'instance Documents, conteneur Blob `documents-<zone>` avec stratégie WORM à rétention temporelle (verrouillée en prod). |
| NF-33 | Restauration PostgreSQL à un instant donné (rétention 35 jours en prod). |

## Prérequis

1. Terraform ≥ 1.9 et Azure CLI (authentification `az login` ou identité fédérée de la CI).
2. Un compte de stockage **existant** pour les états Terraform (un conteneur par
   environnement ou une clé par environnement), accessible en Entra ID
   (rôle *Storage Blob Data Contributor* pour l'identité de déploiement).
3. Droits de l'identité de déploiement sur l'abonnement : *Contributor* +
   *Role Based Access Control Administrator* (ou *User Access Administrator*) pour les
   affectations de rôles, et *Resource Policy Contributor* pour Azure Policy.
4. Enregistrement des fournisseurs de ressources : `Microsoft.App`,
   `Microsoft.ContainerRegistry`, `Microsoft.DBforPostgreSQL`, `Microsoft.ServiceBus`,
   `Microsoft.KeyVault`, `Microsoft.ApiManagement`, `Microsoft.Network`,
   `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.Storage`.
5. Un groupe Entra ID administrateur PostgreSQL et, si `managed_hsm_enabled`, les
   administrateurs du Managed HSM : renseigner leurs *object IDs* dans
   `terraform.tfvars` (les valeurs `00000000-...` sont des exemples).
6. Un agent de déploiement ayant accès au VNet (runner auto-hébergé, ou poste relié
   par VPN/Bastion) : ACR, Key Vault, stockage et Service Bus n'ont **aucun accès
   public**, y compris pour la publication d'images et les opérations de plan de données.

## Déploiement

L'abonnement cible est fourni par la variable d'environnement `ARM_SUBSCRIPTION_ID`
(ou `-var subscription_id=...`). Les paramètres du backend ne sont jamais écrits dans
le code : ils sont passés à l'initialisation.

```bash
export ARM_SUBSCRIPTION_ID="<id-abonnement-cible>"
cd infra/environments/dev

terraform init \
  -backend-config="resource_group_name=rg-sepp-tfstate" \
  -backend-config="storage_account_name=<compte-etats>" \
  -backend-config="container_name=tfstate" \
  -backend-config="key=sepp/dev.tfstate" \
  -backend-config="use_azuread_auth=true"

terraform fmt -check -recursive ../..
terraform validate
terraform plan -out=dev.tfplan
terraform apply dev.tfplan
```

Remplacer `dev` par `test`, `recette`, `preprod` ou `prod` (dossier et clé d'état).

### Premier déploiement (amorçage)

Les Container Apps référencent des images qui doivent déjà exister dans l'ACR :

1. `terraform apply -var deploy_container_apps=false` : socle réseau, données, ACR,
   Key Vault, environnements Container Apps, passerelle, stratégies.
2. Publier les images `sepp/<service>:<image_tag>` dans l'ACR depuis la chaîne CI/CD
   (CTR-20) et verrouiller les étiquettes :
   `az acr repository update --name <acr> --image sepp/<service>:<tag> --write-enabled false`.
3. Créer les rôles PostgreSQL des identités managées, celle d'exécution et celle de migration, une fois par base, avec le
   groupe administrateur Entra. Accorder ensuite les droits sur la base du service uniquement (ARC-02). Voir « Migrations
   de schéma », section « Rôles PostgreSQL » ; les `principal_id` viennent des sorties `service_identities` et
   `migration_identities` de l'étape 1.
4. Créer les secrets applicatifs dans les Key Vault de zone (clés de chiffrement, clé de l'index aveugle, secrets des
   comptes techniques : voir « Secrets applicatifs ») et renseigner dans `terraform.tfvars` le tenant Entra,
   l'audience et les identifiants clients des comptes techniques.
5. Importer le certificat TLS public dans le Key Vault plateforme puis renseigner
   `appgw_ssl_certificate_secret_id` (obligatoire en préproduction et production : un
   avertissement `check` le signale).
6. `terraform apply` (avec `deploy_container_apps = true`).
7. Exécuter le job de migration de chaque service (`az containerapp job start -n mig-<service>`, sortie `migration_jobs`).
   À faire avant d'ouvrir le trafic : sans schéma, les révisions démarrent (`/health/ready` ne vérifie que la connexion),
   mais leurs requêtes échouent.
8. Rattraper les projections locales des consommateurs (ADR 0011) : avec le rôle `exploitation`, demander à chaque
   producteur la republication de son état courant (`POST /api/v1/admin/republications`), puis contrôler le résultat.
   Procédure, ordre et contrôles : [docs/architecture/rattrapage-projections.md](../docs/architecture/rattrapage-projections.md).
   À refaire quand un service s'abonne à un nouveau contrat ; les filtres de sujets (`subject_filters`) doivent être
   appliqués avant.

Les déploiements applicatifs suivants (nouvelle image, répartition du trafic entre
révisions) sont faits par le workflow `deploy.yml` (voir « Déploiement progressif »), Terraform
ignorant ces attributs (image, suffixe de révision, poids de trafic). Le job de migration passe toujours avant la
nouvelle révision (voir « Migrations de schéma »).

### Déploiement progressif (ARC-50)

Le workflow `.github/workflows/deploy.yml` déploie **un service dans un environnement** : il se lance à la main
(*Actions → Déploiement → Run workflow*) ou par `workflow_call` depuis un autre workflow (`secrets: inherit`). Il ne se
déclenche pas sur push vers `main` tant qu'aucun environnement n'existe. La logique est dans `tools/deploy/*.sh`
(testée par `tools/deploy/tests/deployer-service.test.sh` avec une CLI `az` simulée ; workflow `deploiement-scripts.yml`).

| Entrée | Rôle |
| --- | --- |
| `environnement` | `dev`, `test`, `recette`, `preprod` ou `prod` ; c'est aussi le nom de l'environnement GitHub |
| `service` | nom de la Container App sans `ca-` (ex. `personnes`, `documents-medicale`) |
| `reference` | étiquette de l'image (`<version>-build.<n>.<sha>`) ou empreinte `sha256:…` |
| `image` | nom de l'image si différent du service (instances : `documents-medicale` → `documents`) |
| `seuil_5xx_pourcent` | taux maximal de réponses 5xx de la nouvelle révision (défaut 1 %, **à valider**) |
| `paliers` | paliers de trafic (défaut `10 50 100`) ; `attente_palier_secondes` : attente avant chaque contrôle (défaut 180) |

#### Prérequis (actions manuelles, hors dépôt)

1. **Identité fédérée (OIDC, sans secret client)** : une identité (application Entra ou identité managée affectée par
   l'utilisateur) de déploiement, avec une *federated credential* par environnement GitHub, de sujet
   `repo:rouffou/veilla:environment:<env>` et d'émetteur `https://token.actions.githubusercontent.com`. Le workflow
   d'images (`service-images.yml`) continue d'utiliser le sujet de la branche `main`.
2. **Rôles Azure** de cette identité, au niveau des groupes de ressources `rg-sepp-<env>-*` :
   *Contributor* sur les groupes de zone (mise à jour de la Container App et du job de migration, démarrage du job),
   *Log Analytics Reader* sur l'espace `log-sepp-<env>` (contrôle du taux de 5xx), *AcrPull*/*Reader* sur l'ACR (résolution
   de l'empreinte, `az acr login`). Un rôle plus étroit que *Contributor* (`Microsoft.App/containerApps/*`,
   `Microsoft.App/jobs/*`) est possible : à valider. Les identités d'exécution et de migration des services ne changent pas.
3. **Environnements GitHub** (*Settings → Environments*) `dev`, `test`, `recette`, `preprod`, `prod` :
   - `preprod` et `prod` : **Required reviewers** (au moins un approbateur autre que l'auteur du lancement,
     *Prevent self-review*) ; `prod` : restreindre aux branches/étiquettes protégées (*Deployment branches and tags*) ;
   - variables (ou secrets) `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` propres à l'environnement,
     `ACR_LOGIN_SERVER` (déjà utilisé par le workflow d'images ; peut rester au niveau du dépôt) et, facultatif,
     `LOG_ANALYTICS_WORKSPACE_ID` (identifiant `customerId` ; à défaut, le script cherche `log-sepp-<env>` dans
     `rg-sepp-<env>-platform`).
4. **Accès réseau** : l'ACR n'a pas d'accès public ; la vérification cosign et `az acr login` exigent un runner qui
   l'atteint (runner auto-hébergé dans le VNet, comme pour la publication d'images). Le plan de contrôle
   (`az containerapp`) et Log Analytics sont joignables sans accès au VNet.
5. **Application Insights** : le contrôle du taux d'erreur lit la table `AppRequests` (espace Log Analytics) et suppose que
   `AppRoleInstance` d'une requête commence par le nom de la révision (le nom des répliques). **À vérifier** en
   préproduction ; sans télémétrie, un palier est validé avec un avertissement (0 requête).

#### Déroulé

1. **Résolution** de `reference` en empreinte (`image@sha256:…`) ; tout le reste porte sur l'empreinte.
2. **Vérification cosign** (`cosign verify` et `verify-attestation --type spdxjson`, mode sans clé) avec l'identité du
   workflow d'images (`…/.github/workflows/service-images.yml@refs/heads/main`) : échec si la signature ou l'attestation
   SBOM est absente ou invalide, avant tout changement.
3. **Migration** (ADR 0010) : trafic figé à 100 % sur la révision courante, puis mise à jour et démarrage du job
   `mig-<service>` (s'il existe) et attente de `Succeeded` ; tout autre état arrête le déploiement, sans révision créée.
4. **Nouvelle révision** `ca-<service>--r<aammjjhhmmss>` à 0 % de trafic, puis attente des sondes (état de la révision
   *Running* et *Healthy*, sondes CTR-12), 10 minutes au plus.
5. **Paliers** 10, 50, 100 % : après chaque répartition, attente, puis requête Log Analytics (requêtes de la révision sur la
   fenêtre du palier) ; échec si le taux de 5xx dépasse le seuil. Un déploiement refuse de démarrer si plusieurs
   révisions reçoivent déjà du trafic (déploiement précédent inachevé). Premier déploiement sans révision précédente :
   100 % direct après les sondes.
6. **Fin** : l'ancienne révision est désactivée (réactivable) et le résumé du déroulé est écrit dans
   `GITHUB_STEP_SUMMARY`.
7. **Retour arrière automatique** si une étape échoue après la création de la révision (sondes, requête, seuil) : trafic
   remis à 100 % sur l'ancienne révision, puis nouvelle révision désactivée. Les deux actions sont consignées dans le
   résumé ; un échec du retour arrière lui-même est signalé comme intervention manuelle. Les migrations restent en place
   (expand/contract, aucun `Down`).

#### Retour arrière manuel

Après un déploiement réussi, l'ancienne révision est inactive. Pour revenir en arrière :

```bash
rg=rg-sepp-<env>-<zone>; app=ca-<service>
az containerapp revision list -g "$rg" -n "$app" -o table                      # repérer l'ancienne révision
az containerapp revision activate -g "$rg" -n "$app" --revision <ancienne>
az containerapp ingress traffic set -g "$rg" -n "$app" --revision-weight <ancienne>=100
az containerapp revision deactivate -g "$rg" -n "$app" --revision <nouvelle>
```

`max_inactive_revisions` (10) borne l'historique disponible. Une base corrompue se restaure par PITR (voir « Retour arrière »
dans « Migrations de schéma »). Le schéma n'est jamais annulé : la révision précédente doit rester compatible avec lui.

#### Liens avec les ADR

- **ADR 0010 (migrations)** : le workflow joue le job `mig-<service>` avant toute révision et s'arrête sur échec ; Terraform
  ignore l'image du job, mise à jour par le workflow (même empreinte que la révision).
- **ADR 0011 (rattrapage des projections)** : le déploiement d'un nouveau consommateur d'événements n'alimente pas ses
  projections avec l'historique. Une fois la révision à 100 %, l'exploitation (rôle `exploitation`, permission
  `plateforme:republier`) lance les républications des producteurs concernés ; cette étape n'est pas automatisée.

### Managed HSM (ARC-44)

Un Managed HSM créé par Terraform reste inactif tant que son domaine de sécurité n'est
pas téléchargé (quorum de clés RSA des administrateurs) :

```bash
az keyvault security-domain download --hsm-name <hsm> \
  --sd-wrapping-keys cle1.cer cle2.cer cle3.cer --sd-quorum 2 --security-domain-file <hsm>-sd.json
```

Après l'activation, un nouvel `apply` crée la clé racine `documents-kek` dans le HSM (rotation automatique tous les
12 mois) et attribue à l'identité de l'instance Documents de la zone le rôle local *Managed HSM Crypto Service Encryption
User* sur cette clé. Tant que le HSM n'est pas activé, ces ressources échouent : le domaine de sécurité doit être
téléchargé et sauvegardé avant le premier `apply` qui les crée (voir « Clés racines de Documents » pour la sauvegarde).
Les autres rôles locaux (*Managed HSM Crypto User* pour d'autres services de la zone) ne sont pas gérés par ce code.

## Supervision et alertes (NF-62)

Le module `modules/alerting` crée les règles d'alerte et le workbook « SEPP <env> : supervision technique »
(disponibilité, débit, erreurs et latence par service, files Service Bus, PostgreSQL). Toutes les alertes notifient le
groupe d'actions `ag-sepp-<env>-exploitation`, dont le destinataire est `alert_email`.

`alert_email` est obligatoire dans chaque environnement (une valeur vide ou invalide est refusée par une validation
Terraform). Le dépôt est public : les `terraform.tfvars` ne portent que des adresses d'exemple en `@sepp.example.be`,
à remplacer par la liste d'exploitation réelle au déploiement (variable `TF_VAR_alert_email` ou fichier non versionné).

Seuils et sévérités sont les variables `thresholds` et `severities` du module (valeurs par défaut ci-dessous, à ajuster
après observation de la charge réelle ; elles ne sont pas encore surchargées par environnement).

| Alerte | Source | Seuil par défaut | Sév. |
|---|---|---|---|
| `taux-5xx` | `AppRequests` par service | > 5 % de 5xx sur 15 min, 2 évaluations consécutives, min. 50 requêtes | 1 |
| `latence-p95` | `AppRequests` par service | p95 > 2000 ms sur 15 min, 2 évaluations consécutives, min. 50 requêtes | 2 |
| `servicebus-file-morte` | métrique `DeadletteredMessages` par entité | > 0 | 1 |
| `redemarrages-<service>` | métrique `RestartCount` de la Container App | > 3 sur 15 min | 2 |
| `sondes-echecs` | `ContainerAppSystemLogs_CL` | > 10 échecs de sonde sur 15 min par application | 2 |
| `postgres-<zone>-cpu` / `-memoire` | métriques `cpu_percent` / `memory_percent` | > 85 % / > 90 % (moyenne 15 min) | 2 |
| `postgres-<zone>-stockage` | métrique `storage_percent` | > 85 % | 1 |
| `postgres-<zone>-connexions` | métrique `active_connections` | > 80 (à relier à `max_connections` du SKU) | 2 |
| `outbox-lot` | `AppTraces` Error « Échec du traitement d'un lot de l'outbox » | au moins 1 sur 10 min | 1 |
| `outbox-publication` | `AppTraces` Warning « Échec de publication du message » | > 5 sur 15 min par service | 2 |
| `waf-blocages` | `AzureDiagnostics` (journal du pare-feu) | > 200 requêtes bloquées sur 15 min | 3 |
| `documents-cle-racine` | `AppTraces` Error « version de clé racine inutilisable » (code `document.cle-racine-indisponible`) | au moins 1 sur 5 min | 0 |
| `migration-echec-telemetrie` / `-console` | `AppTraces` du service `<nom>-migration` / console du job `mig-*` | au moins 1 erreur sur 15 min | 1 |

Les règles sur la sonde, la console des jobs et les redémarrages ne sont créées que si `deploy_container_apps` est vrai.
Les requêtes ne lisent aucune donnée métier (ARC-06).

Réponse succincte :

1. Ouvrir l'alerte, identifier le service (dimension `AppRoleName`) et la fenêtre, puis le workbook pour la tendance.
2. 5xx / latence : examiner les traces de la période (`AppTraces`, `AppExceptions`), la dernière révision déployée
   (`az containerapp revision list`) et, au besoin, rétablir la révision précédente.
3. File morte : lire les messages de la subscription concernée (`<subscription>/$deadletterqueue`), corriger la cause
   (consommateur, contrat), puis rejouer les messages ; ne jamais les supprimer sans analyse.
4. Outbox : vérifier la connectivité Service Bus du service et ses droits ; les messages restent en base et sont
   republiés au rétablissement (publication au moins une fois, consommateurs idempotents).
5. Clé racine de Documents : ne désactiver ni supprimer aucune version utilisée par des archives ; restaurer la version
   ou rétablir l'accès au coffre / HSM de la zone (ARC-44). Sévérité 0 : escalade immédiate.
6. Migration en échec : ne pas déployer la nouvelle révision ; lire les journaux de l'exécution du job `mig-<service>`
   (ADR 0010), corriger puis relancer le job.
7. PostgreSQL : stockage, CPU ou connexions saturés, augmenter le SKU ou le stockage (`postgres_sku_name`,
   `postgres_storage_mb`) par Terraform ; repérer les requêtes en cause.
8. WAF : consulter le journal `ApplicationGatewayFirewallLog` (règle, adresse source, URI) ; distinguer une attaque
   d'une règle trop stricte avant de modifier la politique.

Les requêtes KQL et métriques n'ont pas pu être exécutées sans environnement Azure : à valider à la première mise en
service (noms de colonnes `ContainerAppSystemLogs_CL` / `ContainerAppConsoleLogs_CL`, métriques du workbook).

## Vérifications de disponibilité (ARC-53)

Avant le choix définitif, vérifier pour **Belgium Central** et pour la région de reprise :
Container Apps avec profils de charge et redondance de zone, API Management Premium
avec zones (et plateforme stv2), Application Gateway WAF_v2 zonal, PostgreSQL Flexible
Server HA redondante en zone et sauvegarde géo-redondante (Belgium Central n'étant pas
appairée, la géo-sauvegarde est désactivée par défaut), Service Bus Premium, Managed
HSM, stockage GZRS. Commandes utiles :

```bash
az provider show -n Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations"
az postgres flexible-server list-skus --location belgiumcentral -o table
az account list-locations --query "[?name=='belgiumcentral'].availabilityZoneMappings"
az provider show -n Microsoft.ApiManagement --query "resourceTypes[?resourceType=='service'].zoneMappings"
```

À défaut, documenter l'alternative UE retenue et ajuster `location` / `dr_location`.

## Reprise après sinistre (ARC-52)

Belgium Central n'étant pas appairée, la reprise est configurée explicitement vers
`dr_location` (Suède Centre par défaut) :

- images : géo-réplication ACR (`enable_dr_replication = true`) ;
- infrastructure : le module racine est paramétré par région ; une racine de reprise
  peut instancier le même module avec `location = dr_location` (topologie « pilot light »
  sans Container Apps actives) ;
- données : sauvegardes PostgreSQL restaurables dans une autre région lorsque la
  géo-sauvegarde est disponible, ou réplica en lecture inter-régions ; réplication d'objets
  Blob vers un compte de la région de reprise. Ces éléments restent à réaliser après
  validation ARC-53 (RPO 1 h / RTO 4 h, NF-33).

## Points d'attention

- Le verrouillage WORM (`document_immutability_locked = true`, prod) est
  **irréversible** : les documents ne peuvent plus être supprimés avant l'échéance, ce
  qui doit être concilié avec la purge contrôlée (NF-22) et les durées légales (NF-20).
- CTR-04 (utilisateur non root) et CTR-06 (images signées, sans vulnérabilité
  critique) ne sont pas vérifiables par Azure Policy sur Container Apps : ils sont
  contrôlés dans la chaîne CI/CD (analyse, SBOM, signature) avant publication dans l'ACR.
- Les clés gérées par le client (CMK) pour PostgreSQL, Service Bus et le stockage
  (chiffrement par zone via Key Vault / Managed HSM) ne sont pas encore activées.
- Le fichier `.terraform.lock.hcl` de chaque environnement doit être généré
  (`terraform init`) puis versionné.
