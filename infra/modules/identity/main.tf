# -----------------------------------------------------------------------------
# Module identités : une identité managée user-assigned par service.
#   CTR-16 : accès aux ressources Azure par identité managée, aucun secret.
#   ARC-04 : droits distincts par zone (chaque service n'accède qu'au Key Vault
#            de sa propre zone).
# Rôles attribués ici : AcrPull (registre) et Key Vault Secrets User (coffre de
# la zone, jamais celui d'une autre zone) ; rôle de wrapKey/unwrapKey porté
# sur la clé racine documents-kek des instances Documents et sur la clé racine
# <service>-fields-kek des services qui chiffrent des champs (ARC-44, ARC-45,
# ADR 0012), jamais sur le coffre. Les rôles Service Bus et Blob sont attribués par les modules
# servicebus et storage, au plus près des ressources.
# Identité de migration distincte par service doté d'une base (ADR 0010) :
# AcrPull seulement, droits DDL portés par son rôle PostgreSQL.
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

# Aucun service ne lit le coffre d'une autre zone (ARC-04, ARC-06) : pas de rôle inter-zones.
# Aucun rôle cryptographique au niveau du coffre (l'ancien « Key Vault Crypto User » des services qui chiffrent des champs
# est retiré, ADR 0012) : chaque rôle d'enveloppement est porté sur la seule clé racine du service (voir ci-dessous).

# Clé racine de Documents (décision #356) : l'instance de la zone n'a que le droit d'enveloppement et de déballage
# (wrapKey, unwrapKey) et de lecture des métadonnées, porté sur la clé documents-kek et non sur le coffre.
# Key Vault (RBAC Azure) : rôle « Key Vault Crypto Service Encryption User » sur la clé.
resource "azurerm_role_assignment" "documents_kek" {
  for_each = { for n, a in var.documents_kek_assignments : n => a if !a.managed_hsm }

  scope                = each.value.key_id
  role_definition_name = "Key Vault Crypto Service Encryption User"
  principal_id         = azurerm_user_assigned_identity.this[each.key].principal_id
  principal_type       = "ServicePrincipal"
}

# Variante Managed HSM : RBAC local du HSM (plan de données), rôle « Managed HSM Crypto Service Encryption User »
# (33413926-3206-4cdd-b39a-83574fe37a17) sur la portée de la clé. Exige que le HSM soit activé (domaine de sécurité
# téléchargé, infra/README.md) : le fournisseur lit la définition de rôle dans le HSM.
data "azurerm_key_vault_managed_hardware_security_module_role_definition" "crypto_service_encryption" {
  for_each = { for n, a in var.documents_kek_assignments : n => a if a.managed_hsm }

  managed_hsm_id = each.value.managed_hsm_id
  name           = "33413926-3206-4cdd-b39a-83574fe37a17"
}

# La portée « /keys/<nom> » est un chemin de plan de données : l'affectation est valable avant ou après la création de la clé.
resource "azurerm_key_vault_managed_hardware_security_module_role_assignment" "documents_kek" {
  for_each = { for n, a in var.documents_kek_assignments : n => a if a.managed_hsm }

  name               = uuidv5("dns", "${each.key}/${each.value.key_name}")
  managed_hsm_id     = each.value.managed_hsm_id
  scope              = "/keys/${each.value.key_name}"
  role_definition_id = data.azurerm_key_vault_managed_hardware_security_module_role_definition.crypto_service_encryption[each.key].resource_manager_id
  principal_id       = azurerm_user_assigned_identity.this[each.key].principal_id
}

# Clé racine du chiffrement des champs (<service>-fields-kek, ADR 0012) : même modèle que documents-kek. Le service n'a
# que wrapKey/unwrapKey et la lecture des métadonnées, sur sa seule clé ; un autre service de la zone ne peut pas déballer
# ses clés de données.
resource "azurerm_role_assignment" "field_kek" {
  for_each = { for n, a in var.field_kek_assignments : n => a if !a.managed_hsm }

  scope                = each.value.key_id
  role_definition_name = "Key Vault Crypto Service Encryption User"
  principal_id         = azurerm_user_assigned_identity.this[each.key].principal_id
  principal_type       = "ServicePrincipal"
}

data "azurerm_key_vault_managed_hardware_security_module_role_definition" "field_kek" {
  for_each = { for n, a in var.field_kek_assignments : n => a if a.managed_hsm }

  managed_hsm_id = each.value.managed_hsm_id
  name           = "33413926-3206-4cdd-b39a-83574fe37a17"
}

resource "azurerm_key_vault_managed_hardware_security_module_role_assignment" "field_kek" {
  for_each = { for n, a in var.field_kek_assignments : n => a if a.managed_hsm }

  name               = uuidv5("dns", "${each.key}/${each.value.key_name}")
  managed_hsm_id     = each.value.managed_hsm_id
  scope              = "/keys/${each.value.key_name}"
  role_definition_id = data.azurerm_key_vault_managed_hardware_security_module_role_definition.field_kek[each.key].resource_manager_id
  principal_id       = azurerm_user_assigned_identity.this[each.key].principal_id
}

# -----------------------------------------------------------------------------
# Identités de migration (ADR 0010) : une par service doté d'une base, distincte de l'identité d'exécution. Elle porte les
# droits DDL sur la base du service (rôle PostgreSQL du même nom, amorcé hors Terraform : infra/README.md) et ne sert
# qu'au job de migration. Seul rôle Azure : AcrPull (même image que le service). Aucun accès Key Vault, Service Bus ni
# stockage : le job ne lit aucun secret.
# -----------------------------------------------------------------------------
resource "azurerm_user_assigned_identity" "migration" {
  for_each = toset(var.migration_services)

  name                = "id-${var.name_prefix}-${each.key}-migration"
  resource_group_name = var.services[each.key].resource_group_name
  location            = var.location
  tags                = merge(var.tags, { service = each.key, zone = var.services[each.key].zone, usage = "migration" })
}

resource "azurerm_role_assignment" "migration_acr_pull" {
  for_each = toset(var.migration_services)

  scope                = var.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.migration[each.key].principal_id
  principal_type       = "ServicePrincipal"
}
