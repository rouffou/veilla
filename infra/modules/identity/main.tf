# -----------------------------------------------------------------------------
# Module identités : une identité managée user-assigned par service.
#   CTR-16 : accès aux ressources Azure par identité managée, aucun secret.
#   ARC-04 : droits distincts par zone (chaque service n'accède qu'au Key Vault
#            de sa propre zone).
# Rôles attribués ici : AcrPull (registre) et Key Vault Secrets User (coffre de
# la zone) ; Key Vault Crypto User pour le chiffrement applicatif (ARC-45) dans
# les zones concernées. Les rôles Service Bus et Blob sont attribués par les
# modules servicebus et storage, au plus près des ressources.
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

resource "azurerm_user_assigned_identity" "this" {
  for_each = var.services

  name                = "id-${var.name_prefix}-${each.key}"
  resource_group_name = each.value.resource_group_name
  location            = var.location
  tags                = merge(var.tags, { service = each.key, zone = each.value.zone })
}

resource "azurerm_role_assignment" "acr_pull" {
  for_each = var.services

  scope                = var.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.this[each.key].principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "key_vault_secrets_user" {
  for_each = var.services

  scope                = var.key_vault_ids[each.value.zone]
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.this[each.key].principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "key_vault_crypto_user" {
  for_each = { for k, s in var.services : k => s if contains(var.crypto_zones, s.zone) }

  scope                = var.key_vault_ids[each.value.zone]
  role_definition_name = "Key Vault Crypto User"
  principal_id         = azurerm_user_assigned_identity.this[each.key].principal_id
  principal_type       = "ServicePrincipal"
}
