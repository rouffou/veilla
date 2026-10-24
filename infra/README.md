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
│   ├── registry/           Azure Container Registry Premium privé
│   ├── keyvault/           Key Vault par zone (+ Managed HSM zones sensibles)
│   ├── postgres/           PostgreSQL Flexible Server par zone, une base par service
│   ├── servicebus/         Service Bus Premium, topics, subscriptions, RBAC
│   ├── storage/            Blob Storage documentaire WORM
│   ├── identity/           identités managées par service + RBAC ACR / Key Vault
│   ├── containerapps-env/  environnement Container Apps par zone
│   ├── containerapp/       un microservice (sondes, KEDA, identité, secrets)
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
   stockage documentaire WORM     (+ Managed HSM)                   (+ Managed HSM)
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
| `<Section>__ServicesInternes__*` | attribut `internal_services` du catalogue | affilies, communications, documents, integrations |
| `<Préfixe>__TokenEndpoint`, `__ClientId`, `__Scope` ; secret `__ClientSecret` | attribut `compte_technique`, `var.technical_account_client_ids`, Key Vault de la zone | affilies, communications, documents, integrations |
| `<Préfixe>__CurrentKeyId` ; secrets `<Préfixe>__Keys__<id>` | attribut `encryption`, `var.encryption_key_ids`, Key Vault de la zone | personnes, integrations, documents, surveillance-medicale |
| `BLINDINDEX__KEY` | `key_vault_secrets` du catalogue (secret `blindindex--key`) | personnes |
| `<Section>__Adaptateurs__<Port>` | attribut `adapters` du catalogue, `var.adapter_mode` | communications, documents, integrations, planification |

Un secret est exposé au conteneur sous le nom du secret en majuscules, `-` devenant `_` : le secret
`documents--chiffrement--medicale--encryption--keys--k1` devient `DOCUMENTS__CHIFFREMENT__MEDICALE__ENCRYPTION__KEYS__K1`,
soit la clé `Documents:Chiffrement:medicale:Encryption:Keys:K1` de la configuration .NET.

### PostgreSQL par identité managée

Le serveur n'accepte aucun mot de passe (`password_auth_enabled = false`). Au démarrage, `AddSeppPersistence` lit
`ConnectionStrings:<service>` (développement local, tests) ou, à défaut, `POSTGRES__HOST`, `POSTGRES__DATABASE` et
`POSTGRES__USER` ; le mot de passe est un jeton Entra (portée `https://ossrdbms-aad.database.windows.net/.default`) obtenu
par `DefaultAzureCredential` (identité de `AZURE_CLIENT_ID`) et renouvelé toutes les 25 minutes. TLS est obligatoire
(`SslMode=Require`). `POSTGRES__USER` est le nom de l'identité managée (`id-sepp-<env>-<service>`), qui doit exister comme rôle
PostgreSQL (étape 3 de l'amorçage).

### Secrets applicatifs (créés hors du dépôt)

Terraform ne contient **aucune valeur secrète** et ne crée pas les secrets : il les référence (`key_vault_secret_id` sans
version, lus avec l'identité du service). Les secrets doivent exister **avant** le déploiement des Container Apps (une
référence vers un secret absent fait échouer l'`apply`). Les Key Vault n'ayant aucun accès public, les créer depuis un
agent relié au VNet, avec le rôle *Key Vault Secrets Officer* sur le coffre de la zone. Valeurs :

- clé de chiffrement : 256 bits aléatoires en base64 (`openssl rand -base64 32`), **distincte par zone** (Documents refuse
  de démarrer si deux zones partagent une clé) ;
- clé de l'index aveugle du NISS : idem, 256 bits en base64 ;
- secret d'un compte technique : le secret client de l'application Entra du service.

```bash
# <id> = identifiant de var.encryption_key_ids en minuscules (par défaut k1)
az keyvault secret set --vault-name <kv-standard> --name personnes--encryption--keys--<id> --value "$(openssl rand -base64 32)" >/dev/null
```

| Secret | Coffre | Service |
|---|---|---|
| `personnes--encryption--keys--<id>` | standard | personnes |
| `integrations--encryption--keys--<id>` | standard | integrations |
| `blindindex--key` | standard | personnes |
| `surveillance-medicale--zonemedicale--encryption--keys--<id>` | médicale | surveillance-medicale |
| `documents--chiffrement--<zone>--encryption--keys--<id>` | coffre de chaque zone (`standard`, `medicale`, `psychosociale`) | documents |
| `affilies--comptetechnique--clientsecret`, `communications--comptetechnique--clientsecret`, `documents--comptetechnique--clientsecret`, `integrations--personnes--clientsecret` | standard | affilies, communications, documents, integrations |

Le secret Key Vault d'une clé de chiffrement est préfixé par le nom du service (une clé par service) ; la variable
d'environnement injectée reste `ENCRYPTION__KEYS__<ID>` (ou le préfixe propre au service).

Rotation : ajouter le nouvel identifiant **en tête** de `encryption_key_ids` (la clé courante) en conservant les anciens
(déchiffrement), après avoir créé les nouveaux secrets ; retirer un identifiant seulement quand plus aucune donnée ne
l'utilise (`FieldEncryptor.NeedsRotation`). Les identifiants sont en majuscules et chiffres (`K1`, `K2`), les variables
d'environnement étant en majuscules.

Documents lit les clés des trois zones (clés distinctes, §14.3) : son identité reçoit donc *Key Vault Secrets User* sur les
coffres des zones médicale et psychosociale (`additional_key_vault_zones`). C'est un élargissement volontaire de l'accès
d'un service standard (ARC-04) **à valider** ; il suppose aussi que l'environnement Container Apps standard puisse joindre ces
coffres (NSG, voir ci-dessous).

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
- le service Documents lit les secrets des coffres médical et psychosocial : rôle RBAC propagé et flux réseau (point privé de la zone cible depuis l'environnement standard, aujourd'hui refusé par la segmentation NSG des zones) ;
- les adresses internes `https://ca-<service>.<domaine>` résolvent depuis les environnements appelants et les restrictions d'IP d'entrée les autorisent ;
- la migration des schémas (`Database:MigrateOnStartup` vaut `false` par défaut) est assurée par la chaîne de déploiement ;
- les valeurs d'exemple des `terraform.tfvars` (`entra_tenant_id`, `technical_account_client_ids`, `authentication_audience`) sont remplacées ;
- les origines CORS des BFF (`Cors__Origines__*`) et l'authentification des portails externes (fédération CSAM, ADR 0005) ne sont pas encore injectées.

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
| ARC-45 | Rôle Key Vault Crypto User pour le chiffrement applicatif dans les zones de données. |
| ARC-46 | mTLS Container Apps, segmentation NSG par zone, restrictions IP d'entrée. |
| ARC-47 / NF-62 | Log Analytics, Application Insights (OpenTelemetry), paramètres de diagnostic sur toutes les ressources, groupe d'actions. |
| ARC-50 | Révisions multiples ; image et répartition du trafic pilotées par la CI/CD (`ignore_changes`). |
| ARC-52 | `dr_location` (région UE) ; géo-réplication ACR activable (`enable_dr_replication`). |
| ARC-53 | Voir « Vérifications de disponibilité » ci-dessous. |
| NF-13 | Stratégie « localisations autorisées » limitée à la région principale et à la région de reprise UE. |
| NF-21 | Conteneurs Blob `documents-<zone>` avec stratégie WORM à rétention temporelle (verrouillée en prod). |
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
3. Créer les rôles PostgreSQL des identités managées (une fois par base, avec le groupe
   administrateur Entra) : `select * from pgaadauth_create_principal('id-sepp-<env>-<service>', false, false);`
   puis accorder les droits sur la base du service uniquement (ARC-02).
4. Créer les secrets applicatifs dans les Key Vault de zone (clés de chiffrement, clé de l'index aveugle, secrets des
   comptes techniques : voir « Secrets applicatifs ») et renseigner dans `terraform.tfvars` le tenant Entra,
   l'audience et les identifiants clients des comptes techniques.
5. Importer le certificat TLS public dans le Key Vault plateforme puis renseigner
   `appgw_ssl_certificate_secret_id` (obligatoire en préproduction et production : un
   avertissement `check` le signale).
6. `terraform apply` (avec `deploy_container_apps = true`).

Les déploiements applicatifs suivants (nouvelle image, répartition du trafic entre
révisions) sont faits par la CI/CD (`az containerapp update` / `revision`), Terraform
ignorant ces attributs.

### Managed HSM (ARC-44)

Un Managed HSM créé par Terraform reste inactif tant que son domaine de sécurité n'est
pas téléchargé (quorum de clés RSA des administrateurs) :

```bash
az keyvault security-domain download --hsm-name <hsm> \
  --sd-wrapping-keys cle1.cer cle2.cer cle3.cer --sd-quorum 2 --security-domain-file <hsm>-sd.json
```

Les rôles locaux du HSM (*Managed HSM Crypto User* pour les identités des services de
la zone) et les clés (rotation automatique) sont ensuite attribués ; ils ne sont pas
gérés par ce code tant que le HSM n'est pas activé.

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
