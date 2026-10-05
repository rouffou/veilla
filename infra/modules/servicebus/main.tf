# -----------------------------------------------------------------------------
# Module bus d'événements : Azure Service Bus Premium.
#   ARC-05 : communication asynchrone par événements (publication / abonnement).
#   ARC-31 : livraison au moins une fois, détection des doublons, lettres mortes.
#   CTR-11 : point de terminaison privé, aucun accès public.
#   CTR-16 : authentification Entra ID uniquement (SAS désactivé), rôles RBAC
#            au plus près : émetteur sur son topic, récepteur sur sa subscription.
# Un topic par service publieur ; une subscription par service abonné.
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

resource "azurerm_servicebus_namespace" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  sku                           = "Premium"
  capacity                      = var.capacity
  premium_messaging_partitions  = 1
  local_auth_enabled            = false
  public_network_access_enabled = false
  minimum_tls_version           = "1.2"
  tags                          = var.tags

  network_rule_set {
    default_action                = "Deny"
    public_network_access_enabled = false
    trusted_services_allowed      = false
  }
}

resource "azurerm_servicebus_topic" "this" {
  for_each = toset(var.topics)

  name                                    = each.value
  namespace_id                            = azurerm_servicebus_namespace.this.id
  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = "PT10M"
  default_message_ttl                     = "P14D"
  max_size_in_megabytes                   = 5120
}

resource "azurerm_servicebus_subscription" "this" {
  for_each = var.subscriptions

  name                                 = each.value.subscriber
  topic_id                             = azurerm_servicebus_topic.this[each.value.topic].id
  max_delivery_count                   = 10
  lock_duration                        = "PT1M"
  dead_lettering_on_message_expiration = true
}

# --- RBAC (CTR-16) ---------------------------------------------------------------
resource "azurerm_role_assignment" "sender" {
  for_each = toset(var.topics)

  scope                = azurerm_servicebus_topic.this[each.value].id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = var.principal_ids[each.value]
  principal_type       = "ServicePrincipal"
}

# Émetteurs d'un topic partagé : chaque service publie ses traces d'accès sur « audit » (NF-04).
resource "azurerm_role_assignment" "shared_sender" {
  for_each = var.shared_topic_senders

  scope                = azurerm_servicebus_topic.this[each.value.topic].id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = var.principal_ids[each.value.principal]
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "receiver" {
  for_each = var.subscriptions

  scope                = azurerm_servicebus_subscription.this[each.key].id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = var.principal_ids[each.value.subscriber]
  principal_type       = "ServicePrincipal"
}

# Le scaler KEDA « azure-servicebus » lit le nombre de messages en attente, ce qui
# exige le droit Manage : rôle Data Owner limité à la seule subscription du service (CTR-13).
resource "azurerm_role_assignment" "scaler" {
  for_each = { for k, s in var.subscriptions : k => s if contains(var.queue_scaled_services, s.subscriber) }

  scope                = azurerm_servicebus_subscription.this[each.key].id
  role_definition_name = "Azure Service Bus Data Owner"
  principal_id         = var.principal_ids[each.value.subscriber]
  principal_type       = "ServicePrincipal"
}

resource "azurerm_private_endpoint" "this" {
  name                = "pe-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id
  tags                = var.tags

  private_service_connection {
    name                           = "psc-${var.name}"
    private_connection_resource_id = azurerm_servicebus_namespace.this.id
    subresource_names              = ["namespace"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.private_dns_zone_id]
  }
}

resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_servicebus_namespace.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }
}
