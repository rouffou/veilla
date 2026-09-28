# -----------------------------------------------------------------------------
# Module registre : Azure Container Registry Premium privé.
#   CTR-07 : registre privé Premium, point de terminaison privé, rétention.
#   CTR-11 : aucun accès réseau public.
#   ARC-52 : géo-réplication optionnelle vers la région UE de reprise.
# L'immuabilité des étiquettes (CTR-07) se configure par dépôt
# (`az acr repository update --write-enabled false`) depuis la chaîne CI/CD.
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

resource "azurerm_container_registry" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  sku                           = "Premium"
  admin_enabled                 = false
  anonymous_pull_enabled        = false
  public_network_access_enabled = false
  network_rule_bypass_option    = "None"
  export_policy_enabled         = false
  data_endpoint_enabled         = true
  zone_redundancy_enabled       = var.zone_redundancy_enabled
  retention_policy_in_days      = var.untagged_retention_days
  tags                          = var.tags

  dynamic "georeplications" {
    for_each = var.replication_location == null ? [] : [var.replication_location]
    content {
      location                = georeplications.value
      zone_redundancy_enabled = var.zone_redundancy_enabled
      tags                    = var.tags
    }
  }
}

resource "azurerm_private_endpoint" "this" {
  name                = "pe-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.name}"
    private_connection_resource_id = azurerm_container_registry.this.id
    subresource_names              = ["registry"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.private_dns_zone_id]
  }
}

resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_container_registry.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }
}
