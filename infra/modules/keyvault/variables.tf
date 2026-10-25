variable "name" {
  description = "Nom globalement unique du Key Vault (3 à 24 caractères)."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$", var.name))
    error_message = "Le nom du Key Vault doit compter 3 à 24 caractères alphanumériques ou tirets."
  }
}

variable "resource_group_name" {
  description = "Groupe de ressources de la zone."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "tenant_id" {
  description = "Locataire Entra ID."
  type        = string
}

variable "sku_name" {
  description = "SKU du Key Vault : standard, ou premium (clés protégées par HSM) pour les zones sensibles sans Managed HSM."
  type        = string
  default     = "standard"

  validation {
    condition     = contains(["standard", "premium"], var.sku_name)
    error_message = "sku_name doit valoir standard ou premium."
  }
}

variable "private_endpoint_subnet_id" {
  description = "Sous-réseau des points de terminaison privés de la zone."
  type        = string
}

variable "key_vault_private_dns_zone_id" {
  description = "Zone DNS privée privatelink.vaultcore.azure.net."
  type        = string
}

variable "managed_hsm_enabled" {
  description = "Crée un Managed HSM dédié à la zone (ARC-44)."
  type        = bool
  default     = false
}

variable "managed_hsm_name" {
  description = "Nom globalement unique du Managed HSM (utilisé si managed_hsm_enabled)."
  type        = string
  default     = null
}

variable "managed_hsm_admin_object_ids" {
  description = "Object IDs Entra ID des administrateurs initiaux du Managed HSM."
  type        = list(string)
  default     = []
}

variable "managed_hsm_private_dns_zone_id" {
  description = "Zone DNS privée privatelink.managedhsm.azure.net."
  type        = string
  default     = null
}

variable "documents_kek_enabled" {
  description = "Crée la clé racine de Documents (RSA 3072, wrapKey/unwrapKey) dans le coffre ou le Managed HSM de la zone : à activer pour les zones qui hébergent une instance Documents."
  type        = bool
  default     = false
}

variable "documents_kek_name" {
  description = "Nom de la clé racine de Documents."
  type        = string
  default     = "documents-kek"
}

variable "documents_kek_rotation_period" {
  description = "Délai (ISO 8601) après lequel Key Vault crée automatiquement une nouvelle version de la clé racine (12 mois). Les versions n'expirent jamais."
  type        = string
  default     = "P12M"
}

variable "field_kek_names" {
  description = "Noms des clés racines du chiffrement des champs (<service>-fields-kek, RSA 3072, wrapKey/unwrapKey) à créer dans le coffre ou le Managed HSM de la zone : une par service de la zone qui chiffre des champs (ADR 0012)."
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for n in var.field_kek_names : can(regex("^[a-z0-9-]{1,127}$", n)) && n != var.documents_kek_name])
    error_message = "Nom de clé racine invalide (minuscules, chiffres et tirets) ou confondu avec documents-kek."
  }
}

variable "field_kek_rotation_period" {
  description = "Délai (ISO 8601) après lequel Key Vault crée automatiquement une nouvelle version des clés racines du chiffrement des champs (12 mois). Les versions n'expirent jamais."
  type        = string
  default     = "P12M"
}

variable "log_analytics_workspace_id" {
  description = "Espace Log Analytics destinataire des journaux d'audit."
  type        = string
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
