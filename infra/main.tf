# -----------------------------------------------------------------------------
# Module racine SEPP : compose l'ensemble de l'infrastructure d'un environnement.
# Il est appelé par infra/environments/<env>/main.tf (un état distant par
# environnement, CTR-22). Toute l'infrastructure est décrite en code (ARC-51,
# CTR-21).
#
# Découpage en groupes de ressources, un par zone de sensibilité (ARC-04,
# CTR-10) afin de distinguer les droits d'exploitation :
#   rg-sepp-<env>-platform      : réseau, observabilité, ACR, Service Bus,
#                                 passerelle (APIM + WAF), BFF et identité
#   rg-sepp-<env>-standard      : services standard/transverses, PostgreSQL,
#                                 Key Vault, stockage documentaire WORM
#   rg-sepp-<env>-medicale      : services médicaux, PostgreSQL, Key Vault, HSM,
#                                 stockage WORM et clé racine de documents-medicale
#   rg-sepp-<env>-psychosociale : service psychosocial, PostgreSQL, Key Vault, HSM
# Documents est déployé une fois par zone qui produit des documents (décision #356).
# -----------------------------------------------------------------------------

data "azurerm_client_config" "current" {}

locals {
  zones           = ["platform", "standard", "medicale", "psychosociale"]
  zone_short      = { platform = "plt", standard = "std", medicale = "med", psychosociale = "psy" }
  sensitive_zones = ["medicale", "psychosociale"]
  data_zones      = ["standard", "medicale", "psychosociale"]

  is_prod     = var.environment == "prod"
  name_prefix = "sepp-${var.environment}"

  # Suffixe déterministe pour les noms globalement uniques (ACR, Key Vault, stockage...).
  suffix   = substr(sha1("${data.azurerm_client_config.current.subscription_id}-${var.environment}"), 0, 6)
  acr_name = "crsepp${var.environment}${local.suffix}"

  tags = merge(var.tags, {
    application   = "sepp"
    environnement = var.environment
    gere_par      = "terraform"
  })

  # --- Bases de données (ARC-02) : un serveur par zone, une base par service ---
  db_zones = toset([for s in values(var.services) : s.zone if s.database])
  zone_databases = {
    for z in local.db_zones : z => sort([for n, s in var.services : replace(n, "-", "_") if s.database && s.zone == z])
  }

  # --- Événements (ARC-05) : un topic par publieur, une subscription par abonné ---
  topics = sort([for n, s in var.services : n if s.publishes_events])
  subscriptions = merge([
    for n, s in var.services : { for t in s.subscribes_to : "${t}.${n}" => { topic = t, subscriber = n, subjects = lookup(s.subject_filters, t, []) } }
  ]...)
  queue_scaled_services = [for n, s in var.services : n if s.queue_scaling && length(s.subscribes_to) > 0]

  # Instances du service Documents (une par zone qui produit des documents, décision #356) : elles seules lisent et
  # écrivent les documents (stockage WORM de leur zone) et portent une clé racine documents-kek dans le coffre de leur zone.
  documents_instances = { for n, s in var.services : n => s if coalesce(s.instance_de, n) == "documents" }
  documents_zones     = toset([for s in values(local.documents_instances) : s.zone])
  # Émetteurs d'un topic partagé : audit (toute instance dotée d'une base) et documents (instances supplémentaires,
  # qui publient sur le topic du service d'origine).
  shared_topic_senders = merge(
    { for n, s in var.services : "audit.${n}" => { topic = "audit", principal = n } if s.database && n != "audit" },
    { for n, s in var.services : "${s.instance_de}.${n}" => { topic = s.instance_de, principal = n } if s.instance_de != null },
  )

  # --- Entrée des applications (ARC-43, ARC-46) ---
  # Plateforme : API Management et les autres applications de la plateforme.
  # Zones métier : les BFF (environnement plateforme) et la zone elle-même.
  platform_callers = [module.network.subnet_prefixes["apim"], module.network.subnet_prefixes["cae-platform"]]
  allowed_source_prefixes = {
    for z in local.zones : z => z == "platform" ? local.platform_callers : [module.network.subnet_prefixes["cae-platform"], module.network.subnet_prefixes["cae-${z}"]]
  }

  # URL internes des services (ARC-30), communiquées aux BFF (SEPP__SERVICES__*) et aux services appelants
  # (internal_services du catalogue).
  service_url_by_name = {
    for n, s in var.services :
    n => "https://ca-${n}.${module.container_apps_env[s.zone].default_domain}"
    if s.zone != "platform"
  }
  service_urls = { for n, url in local.service_url_by_name : "SEPP__SERVICES__${upper(replace(n, "-", "_"))}" => url }

  # --- Authentification des jetons (ARC-40, ADR 0005) : chaque service valide émetteur et audience ---
  authentication_authority = "https://login.microsoftonline.com/${var.entra_tenant_id}/v2.0"
  technical_token_endpoint = "https://login.microsoftonline.com/${var.entra_tenant_id}/oauth2/v2.0/token"
  technical_scope          = "${var.authentication_audience}/.default"
  authentication_environment = {
    Authentication__Authority = local.authentication_authority
    Authentication__Audience  = var.authentication_audience
  }

  # --- Appels entre services : adresses internes et comptes techniques (secret par référence Key Vault) ---
  internal_services_environment = {
    for n, s in var.services : n => { for cle, cible in s.internal_services : cle => local.service_url_by_name[cible] }
  }
  technical_account_environment = {
    for n, s in var.services : n => {
      "${s.compte_technique}__TokenEndpoint" = local.technical_token_endpoint
      "${s.compte_technique}__ClientId"      = lookup(var.technical_account_client_ids, n, "")
      "${s.compte_technique}__Scope"         = local.technical_scope
    } if s.compte_technique != null
  }

  # --- Adaptateurs externes : Simulateur partout hors production (ARC-05, NF-14) ---
  adapters_environment = {
    for n, s in var.services : n => { for cle in s.adapters : cle => var.adapter_mode }
  }

  # --- Chiffrement applicatif (ARC-45) : clé courante en clair (identifiant), clés en secrets Key Vault ---
  encryption_environment = {
    for n, s in var.services : n => { for prefixe in keys(s.encryption) : "${prefixe}__CurrentKeyId" => var.encryption_key_ids[0] }
  }

  # --- Secrets Key Vault (références, jamais de valeur dans Terraform ni dans l'état) : la clé de chaque entrée est le
  # nom du secret de l'application, dont la variable d'environnement est le nom en majuscules, « - » devenant « _ »
  # (module containerapp). Le secret Key Vault d'une clé de chiffrement est préfixé par le nom du service (une clé par
  # service, même quand deux services lisent la même section Encryption). Les secrets sont créés hors du dépôt
  # (infra/README.md, « Secrets applicatifs »). ---
  secret_name_prefix = {
    for n, s in var.services : n => { for prefixe in keys(s.encryption) : prefixe => lower(replace(prefixe, "_", "-")) }
  }
  service_key_vault_secrets = {
    for n, s in var.services : n => merge(
      { for k, v in s.key_vault_secrets : k => "${module.keyvault[s.zone].key_vault_uri}secrets/${v}" },
      s.compte_technique == null ? {} : {
        "${lower(replace(s.compte_technique, "_", "-"))}--clientsecret" = "${module.keyvault[s.zone].key_vault_uri}secrets/${lower(replace(s.compte_technique, "_", "-"))}--clientsecret"
      },
      merge({}, [
        for prefixe, zone in s.encryption : {
          for id in var.encryption_key_ids :
          "${local.secret_name_prefix[n][prefixe]}--keys--${lower(id)}" => "${module.keyvault[zone].key_vault_uri}secrets/${startswith(local.secret_name_prefix[n][prefixe], "${n}--") ? "" : "${n}--"}${local.secret_name_prefix[n][prefixe]}--keys--${lower(id)}"
        }
      ]...),
    )
  }

  # Variables d'environnement injectées dans chaque conteneur (CTR-05 : rien dans l'image).
  common_environment = {
    for n, s in var.services : n => {
      SEPP__SERVICE                       = n
      SEPP__ZONE                          = s.zone
      SEPP__ENVIRONNEMENT                 = var.environment
      AZURE_CLIENT_ID                     = module.identity.identities[n].client_id
      OTEL_SERVICE_NAME                   = n
      SERVICEBUS__FULLYQUALIFIEDNAMESPACE = module.servicebus.fully_qualified_namespace
      SERVICEBUS__SUBSCRIPTIONNAME        = n # la subscription porte le nom du service (ou de l'instance)
      KEYVAULT__URI                       = module.keyvault[s.zone].key_vault_uri
    }
  }
  postgres_environment = {
    for n, s in var.services : n => {
      POSTGRES__HOST     = module.postgres[s.zone].fqdn
      POSTGRES__DATABASE = replace(n, "-", "_")
      POSTGRES__USER     = "id-${local.name_prefix}-${n}"
    } if s.database
  }
  # Documents : une instance par zone. Chaque instance ne sert que sa zone (DOCUMENTS__ZONES, aussi en liste indexée pour
  # la liaison de tableau .NET), écrit dans le compte de stockage de sa zone et chiffre par enveloppe avec sa clé racine
  # (URI de la clé sans version : l'objet archivé porte la version utilisée).
  # Source linguistique (NF-41, ADR 0009, #366) : l'instance standard appelle Affiliés et Personnes (Http) ; une instance
  # d'une zone sensible, que le NSG empêche de les appeler, lit la projection alimentée par événements (Projection).
  documents_environment = {
    for n, s in local.documents_instances : n => {
      DOCUMENTS__BLOBENDPOINT       = module.storage[s.zone].blob_endpoint
      DOCUMENTS__ZONES              = s.zone
      DOCUMENTS__ZONES__0           = s.zone
      DOCUMENTS__ENVELOPPE__TYPE    = "KeyVault"
      DOCUMENTS__ENVELOPPE__CLEKEK  = module.keyvault[s.zone].documents_kek_uri
      DOCUMENTS__SOURCELINGUISTIQUE = s.zone == "standard" ? "Http" : "Projection"
    }
  }
  bff_environment = { for n, s in var.services : n => local.service_urls if s.zone == "platform" }
  # CORS des BFF de portail (#358) : une variable CORS__ORIGINESAUTORISEES__<i> par origine autorisée.
  cors_environment = {
    for n, origines in var.portal_origins : n => { for i, o in origines : "CORS__ORIGINESAUTORISEES__${i}" => o }
  }

  app_environment = {
    for n in keys(var.services) : n => merge(
      local.common_environment[n],
      try(local.postgres_environment[n], {}),
      try(local.documents_environment[n], {}),
      try(local.bff_environment[n], {}),
      try(local.cors_environment[n], {}),
      local.authentication_environment,
      local.internal_services_environment[n],
      try(local.technical_account_environment[n], {}),
      local.adapters_environment[n],
      local.encryption_environment[n],
    )
  }

  # Job de migration (ADR 0010) : configuration non secrète du service, avec l'identité de migration (DDL) à la place de
  # l'identité d'exécution, le mode « migrer puis quitter » et le rôle d'exécution qui reçoit les droits DML. Aucun secret.
  migration_environment = {
    for n, s in var.services : n => merge(local.app_environment[n], {
      AZURE_CLIENT_ID          = module.identity.migration_identities[n].client_id
      POSTGRES__USER           = module.identity.migration_identities[n].name
      OTEL_SERVICE_NAME        = "${n}-migration"
      DATABASE__MIGRATEANDEXIT = "true"
      DATABASE__RUNTIMEROLE    = local.postgres_environment[n].POSTGRES__USER
    }) if s.database
  }
}

# Avertissement (non bloquant, pour permettre l'amorçage du Key Vault plateforme) :
# un certificat TLS est attendu en préproduction et en production (ARC-42).
check "certificat_tls" {
  assert {
    condition     = contains(["dev", "test", "recette"], var.environment) || var.appgw_ssl_certificate_secret_id != null
    error_message = "Aucun certificat TLS configuré : l'Application Gateway n'expose qu'un écouteur HTTP d'amorçage (ARC-42)."
  }
}

# -----------------------------------------------------------------------------
# Groupes de ressources (CTR-10, CTR-22)
# -----------------------------------------------------------------------------
resource "azurerm_resource_group" "zone" {
  for_each = toset(local.zones)

  name     = "rg-${local.name_prefix}-${each.value}"
  location = var.location
  tags     = merge(local.tags, { zone = each.value })
}

# -----------------------------------------------------------------------------
# Socle plateforme
# -----------------------------------------------------------------------------
module "network" {
  source = "./modules/network"

  name_prefix         = local.name_prefix
  resource_group_name = azurerm_resource_group.zone["platform"].name
  location            = var.location
  address_space       = var.address_space
  zones               = local.zones
  tags                = local.tags
}

module "monitoring" {
  source = "./modules/monitoring"

  name_prefix         = local.name_prefix
  resource_group_name = azurerm_resource_group.zone["platform"].name
  location            = var.location
  retention_days      = var.log_retention_days
  alert_email         = var.alert_email
  tags                = local.tags
}

module "registry" {
  source = "./modules/registry"

  name                       = local.acr_name
  resource_group_name        = azurerm_resource_group.zone["platform"].name
  location                   = var.location
  zone_redundancy_enabled    = var.zone_redundancy_enabled
  replication_location       = var.enable_dr_replication ? var.dr_location : null
  private_endpoint_subnet_id = module.network.subnet_ids["pe-platform"]
  private_dns_zone_id        = module.network.private_dns_zone_ids["acr"]
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tags                       = local.tags
}

# -----------------------------------------------------------------------------
# Secrets et clés par zone (ARC-04, ARC-44)
# -----------------------------------------------------------------------------
module "keyvault" {
  source   = "./modules/keyvault"
  for_each = toset(local.zones)

  name                            = "kv-${var.environment}-${local.zone_short[each.value]}-${local.suffix}"
  resource_group_name             = azurerm_resource_group.zone[each.value].name
  location                        = var.location
  tenant_id                       = data.azurerm_client_config.current.tenant_id
  sku_name                        = contains(local.sensitive_zones, each.value) ? "premium" : "standard"
  private_endpoint_subnet_id      = module.network.subnet_ids["pe-${each.value}"]
  key_vault_private_dns_zone_id   = module.network.private_dns_zone_ids["keyvault"]
  managed_hsm_enabled             = var.managed_hsm_enabled && contains(local.sensitive_zones, each.value)
  managed_hsm_name                = "hsm-${var.environment}-${local.zone_short[each.value]}-${local.suffix}"
  managed_hsm_admin_object_ids    = var.managed_hsm_admin_object_ids
  managed_hsm_private_dns_zone_id = module.network.private_dns_zone_ids["managedhsm"]
  documents_kek_enabled           = contains(local.documents_zones, each.value)
  log_analytics_workspace_id      = module.monitoring.log_analytics_workspace_id
  tags                            = merge(local.tags, { zone = each.value })
}

# -----------------------------------------------------------------------------
# Données (ARC-02, CTR-15)
# -----------------------------------------------------------------------------
module "postgres" {
  source   = "./modules/postgres"
  for_each = local.db_zones

  name                         = "psql-${local.name_prefix}-${each.value}-${local.suffix}"
  resource_group_name          = azurerm_resource_group.zone[each.value].name
  location                     = var.location
  tenant_id                    = data.azurerm_client_config.current.tenant_id
  postgres_version             = var.postgres_version
  sku_name                     = var.postgres_sku_name
  storage_mb                   = var.postgres_storage_mb
  backup_retention_days        = var.postgres_backup_retention_days
  geo_redundant_backup_enabled = var.postgres_geo_redundant_backup_enabled
  high_availability_enabled    = var.postgres_high_availability_enabled
  entra_admin                  = var.postgres_entra_admin
  databases                    = local.zone_databases[each.value]
  private_endpoint_subnet_id   = module.network.subnet_ids["pe-${each.value}"]
  private_dns_zone_id          = module.network.private_dns_zone_ids["postgres"]
  log_analytics_workspace_id   = module.monitoring.log_analytics_workspace_id
  tags                         = merge(local.tags, { zone = each.value })
}

module "identity" {
  source = "./modules/identity"

  name_prefix = local.name_prefix
  location    = var.location
  services = {
    for n, s in var.services : n => {
      zone                = s.zone
      resource_group_name = azurerm_resource_group.zone[s.zone].name
    }
  }
  container_registry_id = module.registry.id
  key_vault_ids         = { for z, kv in module.keyvault : z => kv.key_vault_id }
  # Chiffrement applicatif (ARC-45) : rôle Key Vault Crypto User au niveau du coffre pour les seuls services qui déclarent
  # un jeu de clés (encryption), jamais pour les instances Documents (rôle porté sur la clé racine de leur zone).
  crypto_services = [for n, s in var.services : n if length(s.encryption) > 0 && contains(local.data_zones, s.zone)]
  # Clé racine documents-kek de la zone de chaque instance Documents (wrapKey/unwrapKey, rôle porté sur la clé).
  documents_kek_assignments = {
    for n, s in local.documents_instances : n => {
      key_id         = module.keyvault[s.zone].documents_kek_id
      managed_hsm    = var.managed_hsm_enabled && contains(local.sensitive_zones, s.zone)
      managed_hsm_id = module.keyvault[s.zone].documents_kek_managed_hsm_id
      key_name       = module.keyvault[s.zone].documents_kek_name
    }
  }
  # Identité de migration distincte (droits DDL) pour chaque service doté d'une base (ADR 0010).
  migration_services = [for n, s in var.services : n if s.database]
  tags               = local.tags
}

module "servicebus" {
  source = "./modules/servicebus"

  name                       = "sb-${local.name_prefix}-${local.suffix}"
  resource_group_name        = azurerm_resource_group.zone["platform"].name
  location                   = var.location
  capacity                   = var.servicebus_capacity
  topics                     = local.topics
  subscriptions              = local.subscriptions
  principal_ids              = { for n, i in module.identity.identities : n => i.principal_id }
  shared_topic_senders       = local.shared_topic_senders
  queue_scaled_services      = local.queue_scaled_services
  private_endpoint_subnet_id = module.network.subnet_ids["pe-platform"]
  private_dns_zone_id        = module.network.private_dns_zone_ids["servicebus"]
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tags                       = local.tags
}

# Stockage documentaire WORM : un compte par zone dotée d'une instance Documents (décision #356), dans le groupe de
# ressources de la zone, derrière le point privé pe-<zone>. Nom : stsepp<env><std|med|psy><suffixe> (22 caractères au plus).
# Seule l'instance Documents de la zone reçoit le rôle Storage Blob Data Contributor.
module "storage" {
  source   = "./modules/storage"
  for_each = local.documents_zones

  name                           = "stsepp${var.environment}${local.zone_short[each.value]}${local.suffix}"
  resource_group_name            = azurerm_resource_group.zone[each.value].name
  location                       = var.location
  replication_type               = var.storage_replication_type
  containers                     = ["documents-${each.value}"]
  immutability_period_in_days    = var.document_retention_days
  immutability_locked            = var.document_immutability_locked
  blob_contributor_principal_ids = { for n, s in local.documents_instances : n => module.identity.identities[n].principal_id if s.zone == each.value }
  private_endpoint_subnet_id     = module.network.subnet_ids["pe-${each.value}"]
  private_dns_zone_id            = module.network.private_dns_zone_ids["blob"]
  log_analytics_workspace_id     = module.monitoring.log_analytics_workspace_id
  tags                           = merge(local.tags, { zone = each.value })
}

# -----------------------------------------------------------------------------
# Exécution : Azure Container Apps (CTR-00, CTR-10 à CTR-16)
# -----------------------------------------------------------------------------
module "container_apps_env" {
  source   = "./modules/containerapps-env"
  for_each = toset(local.zones)

  name                       = "cae-${local.name_prefix}-${each.value}"
  resource_group_name        = azurerm_resource_group.zone[each.value].name
  location                   = var.location
  infrastructure_subnet_id   = module.network.subnet_ids["cae-${each.value}"]
  virtual_network_id         = module.network.vnet_id
  zone_redundancy_enabled    = var.zone_redundancy_enabled
  dedicated_workload_profile = var.dedicated_workload_profile
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tags                       = merge(local.tags, { zone = each.value })
}

module "container_app" {
  source   = "./modules/containerapp"
  for_each = var.deploy_container_apps ? var.services : {}

  name                           = "ca-${each.key}"
  resource_group_name            = azurerm_resource_group.zone[each.value.zone].name
  container_app_environment_id   = module.container_apps_env[each.value.zone].id
  workload_profile_name          = module.container_apps_env[each.value.zone].workload_profile_name
  identity_id                    = module.identity.identities[each.key].id
  registry_server                = module.registry.login_server
  image                          = coalesce(each.value.image, "${module.registry.login_server}/sepp/${coalesce(each.value.instance_de, each.key)}:${var.image_tag}")
  cpu                            = each.value.cpu
  memory                         = each.value.memory
  min_replicas                   = max(coalesce(each.value.min_replicas, var.default_min_replicas), local.is_prod ? 2 : 0)
  max_replicas                   = each.value.max_replicas
  http_concurrent_requests       = each.value.http_concurrency
  servicebus_namespace_name      = module.servicebus.namespace_name
  servicebus_scale_rules         = each.value.queue_scaling ? [for t in each.value.subscribes_to : { topic = t, subscription = each.key }] : []
  servicebus_message_count       = each.value.message_count
  allowed_source_prefixes        = local.allowed_source_prefixes[each.value.zone]
  environment_variables          = local.app_environment[each.key]
  key_vault_secrets              = local.service_key_vault_secrets[each.key]
  app_insights_connection_string = module.monitoring.application_insights_connection_string
  tags                           = merge(local.tags, { zone = each.value.zone, service = each.key })

  # Les rôles Service Bus (émission, réception, scaler KEDA) doivent exister au démarrage.
  depends_on = [module.servicebus]
}

# Jobs de migration de schéma (ADR 0010, ARC-50) : un par service doté d'une base, dans l'environnement de sa zone, lancé
# par la chaîne de déploiement avant la nouvelle révision. Même image que le service, identité de migration (DDL).
module "migration_job" {
  source   = "./modules/containerapp-migration-job"
  for_each = var.deploy_container_apps ? { for n, s in var.services : n => s if s.database } : {}

  name                         = "mig-${each.key}"
  resource_group_name          = azurerm_resource_group.zone[each.value.zone].name
  location                     = var.location
  container_app_environment_id = module.container_apps_env[each.value.zone].id
  workload_profile_name        = module.container_apps_env[each.value.zone].workload_profile_name
  identity_id                  = module.identity.migration_identities[each.key].id
  registry_server              = module.registry.login_server
  image                        = coalesce(each.value.image, "${module.registry.login_server}/sepp/${coalesce(each.value.instance_de, each.key)}:${var.image_tag}")
  environment_variables        = local.migration_environment[each.key]
  tags                         = merge(local.tags, { zone = each.value.zone, service = each.key, usage = "migration" })
}

# -----------------------------------------------------------------------------
# Passerelle : Application Gateway WAF + API Management (ARC-42)
# -----------------------------------------------------------------------------
module "gateway" {
  source = "./modules/apim"

  apim_name                  = "apim-${local.name_prefix}-${local.suffix}"
  appgw_name                 = "agw-${local.name_prefix}"
  resource_group_name        = azurerm_resource_group.zone["platform"].name
  location                   = var.location
  sku_name                   = var.apim_sku_name
  zones                      = var.apim_zones
  publisher_name             = var.apim_publisher_name
  publisher_email            = var.apim_publisher_email
  apim_subnet_id             = module.network.subnet_ids["apim"]
  appgw_subnet_id            = module.network.subnet_ids["appgw"]
  virtual_network_id         = module.network.vnet_id
  key_vault_id               = module.keyvault["platform"].key_vault_id
  ssl_certificate_secret_id  = var.appgw_ssl_certificate_secret_id
  appgw_min_capacity         = var.appgw_min_capacity
  appgw_max_capacity         = var.appgw_max_capacity
  waf_mode                   = var.waf_mode
  rate_limit_per_minute      = var.waf_rate_limit_per_minute
  bff_backends               = { for n, app in module.container_app : n => app.fqdn if startswith(n, "bff-") }
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tags                       = local.tags
}

# -----------------------------------------------------------------------------
# Gouvernance : Azure Policy (CTR-17, NF-13)
# -----------------------------------------------------------------------------
# Alertes et tableau de bord techniques (NF-62) : notifient le groupe d'actions du module monitoring.
module "alerting" {
  source = "./modules/alerting"

  name_prefix                = local.name_prefix
  resource_group_name        = azurerm_resource_group.zone["platform"].name
  location                   = var.location
  action_group_id            = module.monitoring.action_group_id
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  application_insights_id    = module.monitoring.application_insights_id
  servicebus_namespace_id    = module.servicebus.namespace_id
  postgres_server_ids        = { for z, p in module.postgres : z => p.server_id }
  container_app_ids          = { for n, app in module.container_app : n => app.id }
  tags                       = local.tags
}

module "policy" {
  source = "./modules/policy"

  name_prefix        = local.name_prefix
  environment        = var.environment
  resource_group_ids = { for z, rg in azurerm_resource_group.zone : z => rg.id }
  allowed_registries = concat(["${local.acr_name}.azurecr.io"], var.additional_allowed_registries)
  allowed_locations  = distinct([var.location, var.dr_location])
  effect             = var.policy_effect
}

# Chaque service doté d'un compte technique doit avoir son identifiant client (#352).
check "comptes_techniques_identifiants" {
  assert {
    condition = alltrue([
      for n, s in var.services : s.compte_technique == null || lookup(var.technical_account_client_ids, n, "") != ""
    ])
    error_message = "Identifiant client manquant dans technical_account_client_ids pour un service doté d'un compte technique."
  }
}
