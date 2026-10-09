# -----------------------------------------------------------------------------
# Racine Terraform d'un environnement SEPP (CTR-22 : environnements séparés).
# Fichier identique dans chaque dossier environments/<env> ; seules les valeurs
# de terraform.tfvars diffèrent. L'état est stocké dans un compte de stockage
# Azure dont les coordonnées sont fournies à l'initialisation :
#   terraform init -backend-config=... (voir infra/README.md).
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.80"
    }
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.13"
    }
  }

  # Backend distant : aucune valeur en dur, tout est passé par -backend-config.
  backend "azurerm" {}
}

# Ressources ARM sans équivalent azurerm (règle $Default des subscriptions Service Bus, ADR 0007).
provider "azapi" {
  subscription_id = var.subscription_id
}

provider "azurerm" {
  # null = lecture de ARM_SUBSCRIPTION_ID (CI/CD, poste d'exploitation).
  subscription_id = var.subscription_id

  # Les comptes de stockage n'acceptent pas les clés partagées (CTR-16).
  storage_use_azuread = true

  features {
    key_vault {
      # Protection contre la purge : jamais de purge automatique à la destruction.
      purge_soft_delete_on_destroy    = false
      recover_soft_deleted_key_vaults = true
    }

    resource_group {
      prevent_deletion_if_contains_resources = true
    }
  }
}

module "sepp" {
  source = "../.."

  environment                           = var.environment
  address_space                         = var.address_space
  zone_redundancy_enabled               = var.zone_redundancy_enabled
  enable_dr_replication                 = var.enable_dr_replication
  dr_location                           = var.dr_location
  default_min_replicas                  = var.default_min_replicas
  deploy_container_apps                 = var.deploy_container_apps
  image_tag                             = var.image_tag
  dedicated_workload_profile            = var.dedicated_workload_profile
  postgres_sku_name                     = var.postgres_sku_name
  postgres_backup_retention_days        = var.postgres_backup_retention_days
  postgres_high_availability_enabled    = var.postgres_high_availability_enabled
  postgres_geo_redundant_backup_enabled = var.postgres_geo_redundant_backup_enabled
  postgres_entra_admin                  = var.postgres_entra_admin
  servicebus_capacity                   = var.servicebus_capacity
  storage_replication_type              = var.storage_replication_type
  document_retention_days               = var.document_retention_days
  document_immutability_locked          = var.document_immutability_locked
  managed_hsm_enabled                   = var.managed_hsm_enabled
  managed_hsm_admin_object_ids          = var.managed_hsm_admin_object_ids
  log_retention_days                    = var.log_retention_days
  alert_email                           = var.alert_email
  apim_sku_name                         = var.apim_sku_name
  apim_zones                            = var.apim_zones
  apim_publisher_email                  = var.apim_publisher_email
  appgw_min_capacity                    = var.appgw_min_capacity
  appgw_max_capacity                    = var.appgw_max_capacity
  appgw_ssl_certificate_secret_id       = var.appgw_ssl_certificate_secret_id
  waf_mode                              = var.waf_mode
  policy_effect                         = var.policy_effect
  additional_allowed_registries         = var.additional_allowed_registries
  tags                                  = var.tags
}

output "resource_group_names" {
  description = "Groupes de ressources par zone."
  value       = module.sepp.resource_group_names
}

output "container_registry_login_server" {
  description = "Registre privé des images."
  value       = module.sepp.container_registry_login_server
}

output "gateway_public_ip" {
  description = "IP publique de l'Application Gateway WAF."
  value       = module.sepp.gateway_public_ip
}

output "key_vault_uris" {
  description = "URI des Key Vault par zone."
  value       = module.sepp.key_vault_uris
}

output "managed_hsm_uris" {
  description = "URI des Managed HSM à activer."
  value       = module.sepp.managed_hsm_uris
}

output "postgres_fqdns" {
  description = "Serveurs PostgreSQL par zone."
  value       = module.sepp.postgres_fqdns
}

output "service_identities" {
  description = "Identités managées des services."
  value       = module.sepp.service_identities
}
