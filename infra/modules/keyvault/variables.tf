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

variable "log_analytics_workspace_id" {
  description = "Espace Log Analytics destinataire des journaux d'audit."
  type        = string
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
