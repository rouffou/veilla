# -----------------------------------------------------------------------------
# Module observabilité : Log Analytics, Application Insights et groupe d'actions.
#   ARC-47 : traces distribuées OpenTelemetry, journaux structurés, métriques.
#   NF-62  : supervision, alertes et tableaux de bord mis à disposition du SEPP.
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

resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "PerGB2018"
  retention_in_days   = var.retention_days
  tags                = var.tags
}

resource "azurerm_application_insights" "this" {
  name                = "appi-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  location            = var.location
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"
  retention_in_days   = var.retention_days
  tags                = var.tags
}

# Groupe d'actions d'exploitation (NF-62) : créé seulement si une adresse est fournie.
resource "azurerm_monitor_action_group" "ops" {
  count = var.alert_email == null ? 0 : 1

  name                = "ag-${var.name_prefix}-exploitation"
  resource_group_name = var.resource_group_name
  short_name          = "sepp-ops"
  tags                = var.tags

  email_receiver {
    name                    = "exploitation"
    email_address           = var.alert_email
    use_common_alert_schema = true
  }
}
