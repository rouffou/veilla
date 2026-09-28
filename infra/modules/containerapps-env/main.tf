# -----------------------------------------------------------------------------
# Module environnement Container Apps d'une zone de sensibilité.
#   CTR-00 : plateforme d'exécution Azure Container Apps (décision d'architecture).
#   CTR-10 : un environnement dédié par zone, dans son propre sous-réseau.
#   CTR-11 : environnement interne (aucune IP publique), chiffrement entre services.
#   CTR-14 : redondance de zone lorsque la région le permet.
#   ARC-46 : mTLS entre services de l'environnement.
#   ARC-47 : journaux envoyés à Azure Monitor (paramètre de diagnostic).
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.7"
    }
  }
}

resource "azurerm_container_app_environment" "this" {
  name                               = var.name
  resource_group_name                = var.resource_group_name
  location                           = var.location
  infrastructure_subnet_id           = var.infrastructure_subnet_id
  infrastructure_resource_group_name = "${var.resource_group_name}-cae-infra"
  internal_load_balancer_enabled     = true
  zone_redundancy_enabled            = var.zone_redundancy_enabled
  mutual_tls_enabled                 = true
  logs_destination                   = "azure-monitor"
  tags                               = var.tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  dynamic "workload_profile" {
    for_each = var.dedicated_workload_profile == null ? [] : [var.dedicated_workload_profile]
    content {
      name                  = "dedie"
      workload_profile_type = workload_profile.value.workload_profile_type
      minimum_count         = workload_profile.value.minimum_count
      maximum_count         = workload_profile.value.maximum_count
    }
  }
}

resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_container_app_environment.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }
}

# Résolution DNS privée du domaine de l'environnement interne, pour que la
# passerelle et les autres zones joignent les applications exposées dans le VNet.
resource "azurerm_private_dns_zone" "this" {
  name                = azurerm_container_app_environment.this.default_domain
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "this" {
  name                  = "link-${var.name}"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.this.name
  virtual_network_id    = var.virtual_network_id
  registration_enabled  = false
  tags                  = var.tags
}

resource "azurerm_private_dns_a_record" "wildcard" {
  name                = "*"
  zone_name           = azurerm_private_dns_zone.this.name
  resource_group_name = var.resource_group_name
  ttl                 = 300
  records             = [azurerm_container_app_environment.this.static_ip_address]
  tags                = var.tags
}

resource "azurerm_private_dns_a_record" "apex" {
  name                = "@"
  zone_name           = azurerm_private_dns_zone.this.name
  resource_group_name = var.resource_group_name
  ttl                 = 300
  records             = [azurerm_container_app_environment.this.static_ip_address]
  tags                = var.tags
}
