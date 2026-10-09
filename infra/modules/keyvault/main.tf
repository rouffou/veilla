# -----------------------------------------------------------------------------
# Module secrets et clés d'une zone de sensibilité.
#   ARC-04 : clés de chiffrement et droits distincts par zone.
#   ARC-44 : secrets dans un coffre ; clés des zones médicale et psychosociale
#            protégées par HSM (Managed HSM dédié, ou Key Vault Premium à défaut).
#   CTR-11 : accès uniquement par point de terminaison privé.
#   CTR-16 : autorisation RBAC (aucune stratégie d'accès, aucun secret en code).
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

resource "azurerm_key_vault" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  tenant_id                     = var.tenant_id
  sku_name                      = var.sku_name
  rbac_authorization_enabled    = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = false
  tags                          = var.tags

  network_acls {
    bypass         = "None"
    default_action = "Deny"
  }
}

resource "azurerm_private_endpoint" "key_vault" {
  name                = "pe-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.name}"
    private_connection_resource_id = azurerm_key_vault.this.id
    subresource_names              = ["vault"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.key_vault_private_dns_zone_id]
  }
}

resource "azurerm_monitor_diagnostic_setting" "key_vault" {
  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_key_vault.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "audit"
  }
}

# --- Managed HSM (ARC-44), zones médicale et psychosociale -----------------------
# Après création, le HSM doit être activé (téléchargement du domaine de sécurité
# avec un quorum de clés d'administrateurs) : voir infra/README.md.
resource "azurerm_key_vault_managed_hardware_security_module" "this" {
  count = var.managed_hsm_enabled ? 1 : 0

  name                          = var.managed_hsm_name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  tenant_id                     = var.tenant_id
  sku_name                      = "Standard_B1"
  admin_object_ids              = var.managed_hsm_admin_object_ids
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = false
  tags                          = var.tags

  network_acls {
    bypass         = "None"
    default_action = "Deny"
  }
}

resource "azurerm_private_endpoint" "managed_hsm" {
  count = var.managed_hsm_enabled ? 1 : 0

  name                = "pe-${var.managed_hsm_name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.managed_hsm_name}"
    private_connection_resource_id = azurerm_key_vault_managed_hardware_security_module.this[0].id
    subresource_names              = ["managedhsm"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.managed_hsm_private_dns_zone_id]
  }
}

resource "azurerm_monitor_diagnostic_setting" "managed_hsm" {
  count = var.managed_hsm_enabled ? 1 : 0

  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_key_vault_managed_hardware_security_module.this[0].id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "audit"
  }
}
