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

variable "crypto_zones" {
  description = "Zones dont les services reçoivent le rôle Key Vault Crypto User (chiffrement applicatif, ARC-45)."
  type        = list(string)
  default     = []
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
