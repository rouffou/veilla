# -----------------------------------------------------------------------------
# Module stockage documentaire : Azure Blob Storage avec immutabilité WORM.
#   NF-21  : archivage électronique à valeur probante (non modifiable, non effaçable).
#   ARC-04 : un conteneur par zone de sensibilité (contenu chiffré par zone).
#   CTR-11 : accès uniquement par point de terminaison privé.
#   CTR-16 : clés d'accès partagé désactivées, accès par identité managée (RBAC).
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.80"
    }
  }
}

resource "azurerm_storage_account" "this" {
  name                              = var.name
  resource_group_name               = var.resource_group_name
  location                          = var.location
  account_kind                      = "StorageV2"
  account_tier                      = "Standard"
  account_replication_type          = var.replication_type
  access_tier                       = "Hot"
  min_tls_version                   = "TLS1_2"
  https_traffic_only_enabled        = true
  shared_access_key_enabled         = false
  default_to_oauth_authentication   = true
  allow_nested_items_to_be_public   = false
  public_network_access_enabled     = false
  infrastructure_encryption_enabled = true
  tags                              = var.tags

  network_rules {
    default_action = "Deny"
    bypass         = ["None"]
  }

  blob_properties {
    # Le versionnage n'est pas activé : l'immutabilité est portée par la stratégie
    # WORM de niveau conteneur ci-dessous.
    delete_retention_policy {
      days = 30
    }

    container_delete_retention_policy {
      days = 30
    }
  }
}

resource "azurerm_storage_container" "this" {
  for_each = toset(var.containers)

  name                  = each.value
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}

# Stratégie WORM à rétention temporelle (NF-21). Une fois verrouillée
# (immutability_locked = true), elle ne peut plus être raccourcie ni supprimée.
resource "azurerm_storage_container_immutability_policy" "this" {
  for_each = azurerm_storage_container.this

  storage_container_resource_manager_id = each.value.id
  immutability_period_in_days           = var.immutability_period_in_days
  locked                                = var.immutability_locked
  protected_append_writes_enabled       = false
}

resource "azurerm_private_endpoint" "this" {
  name                = "pe-${var.name}-blob"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.name}-blob"
    private_connection_resource_id = azurerm_storage_account.this.id
    subresource_names              = ["blob"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.private_dns_zone_id]
  }
}

# Accès aux documents : rôle Storage Blob Data Contributor pour les services autorisés.
resource "azurerm_role_assignment" "blob_contributor" {
  for_each = var.blob_contributor_principal_ids

  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = each.value
  principal_type       = "ServicePrincipal"
}

resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "diag-log-analytics"
  target_resource_id         = "${azurerm_storage_account.this.id}/blobServices/default"
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "audit"
  }
}
