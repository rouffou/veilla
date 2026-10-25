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
      version = "~> 4.80"
    }
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.13"
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

# --- Clé racine de Documents (documents-kek), décision #356, ADR 0009 --------------
# Clé asymétrique RSA 3072 qui enveloppe (wrapKey) la clé de données AES-256 de chaque document : une clé par zone qui
# produit des documents, dans le coffre de la zone.
#   - Zones sensibles : RSA-HSM (Key Vault Premium, ou Managed HSM si managed_hsm_enabled, ARC-44) ; zone standard : RSA.
#   - Rotation automatique tous les 12 mois, sans expiration des versions : les objets archivés portent l'identifiant
#     versionné de la clé (kid) et les anciennes versions restent utilisables pour déballer (unwrapKey).
#   - Création par l'API ARM (azapi), pas par azurerm_key_vault_key : celui-ci passerait par le plan de données du coffre,
#     fermé au réseau public (CTR-11). Le principal de déploiement a besoin du droit ARM
#     Microsoft.KeyVault/vaults/keys/write (managedHSMs/keys/write avec un Managed HSM) : voir infra/README.md et #282.
#   - Un PUT ARM sur une clé existante crée une nouvelle version : le corps n'est donc modifié qu'à dessein.
#   - La perte de la clé rend illisibles les archives de la zone : protection contre la purge active, sauvegarde décrite
#     dans infra/README.md (« Clés racines de Documents »).
locals {
  documents_kek_kty    = (var.sku_name == "premium" || var.managed_hsm_enabled) ? "RSA-HSM" : "RSA"
  documents_kek_parent = var.managed_hsm_enabled ? one(azurerm_key_vault_managed_hardware_security_module.this[*].id) : azurerm_key_vault.this.id
}

resource "azapi_resource" "documents_kek" {
  count = var.documents_kek_enabled ? 1 : 0

  type      = var.managed_hsm_enabled ? "Microsoft.KeyVault/managedHSMs/keys@2023-07-01" : "Microsoft.KeyVault/vaults/keys@2023-07-01"
  name      = var.documents_kek_name
  parent_id = local.documents_kek_parent

  body = {
    properties = {
      kty     = local.documents_kek_kty
      keySize = 3072
      keyOps  = ["wrapKey", "unwrapKey"]
      attributes = {
        enabled = true
      }
      rotationPolicy = {
        lifetimeActions = [
          {
            trigger = { timeAfterCreate = var.documents_kek_rotation_period }
            action  = { type = "rotate" }
          }
        ]
      }
    }
  }
}

# --- Clés racines du chiffrement des champs (<service>-fields-kek), ADR 0012 ---------
# Une clé par service qui chiffre des champs (attribut encryption du catalogue), dans le coffre de la zone du service :
# elle enveloppe (wrapKey) les clés de données (DEK) du service, déballées au démarrage (unwrapKey) et gardées en mémoire.
# Même modèle que documents-kek : RSA-HSM 3072 en zone sensible (Key Vault Premium ou Managed HSM), RSA 3072 en zone
# standard, rotation automatique sans expiration des versions (les DEK enveloppées portent le kid versionné), création
# par l'API ARM. La perte de la clé rend illisibles les champs chiffrés du service : protection contre la purge active.
resource "azapi_resource" "field_kek" {
  for_each = toset(var.field_kek_names)

  type      = var.managed_hsm_enabled ? "Microsoft.KeyVault/managedHSMs/keys@2023-07-01" : "Microsoft.KeyVault/vaults/keys@2023-07-01"
  name      = each.value
  parent_id = local.documents_kek_parent

  body = {
    properties = {
      kty     = local.documents_kek_kty
      keySize = 3072
      keyOps  = ["wrapKey", "unwrapKey"]
      attributes = {
        enabled = true
      }
      rotationPolicy = {
        lifetimeActions = [
          {
            trigger = { timeAfterCreate = var.field_kek_rotation_period }
            action  = { type = "rotate" }
          }
        ]
      }
    }
  }
}
