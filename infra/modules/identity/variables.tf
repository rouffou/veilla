variable "name_prefix" {
  description = "Préfixe de nommage (ex. sepp-dev)."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "services" {
  description = "Services pour lesquels créer une identité (clé = nom du service)."
  type = map(object({
    zone                = string
    resource_group_name = string
  }))
}

variable "container_registry_id" {
  description = "Identifiant de l'Azure Container Registry (rôle AcrPull)."
  type        = string
}

variable "key_vault_ids" {
  description = "Identifiant du Key Vault de chaque zone (clé = zone)."
  type        = map(string)
}

variable "crypto_services" {
  description = "Services qui reçoivent le rôle Key Vault Crypto User sur le coffre de leur zone (chiffrement applicatif, ARC-45). Les instances Documents n'en font pas partie."
  type        = list(string)
  default     = []
}

variable "documents_kek_assignments" {
  description = "Clé racine documents-kek de chaque instance Documents (clé = service) : key_id (identifiant ARM de la clé), managed_hsm_id (null si la clé est dans un Key Vault) et key_name. L'identité du service reçoit le rôle d'enveloppement/déballage sur cette seule clé."
  type = map(object({
    key_id         = optional(string)
    managed_hsm    = bool
    managed_hsm_id = optional(string)
    key_name       = string
  }))
  default = {}
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
