variable "name" {
  description = "Nom globalement unique du serveur PostgreSQL Flexible."
  type        = string
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
  description = "Locataire Entra ID pour l'authentification."
  type        = string
}

variable "postgres_version" {
  description = "Version majeure de PostgreSQL."
  type        = string
  default     = "16"
}

variable "sku_name" {
  description = "SKU du serveur (ex. B_Standard_B1ms, GP_Standard_D2ds_v5)."
  type        = string
}

variable "storage_mb" {
  description = "Taille de stockage initiale (Mo)."
  type        = number
  default     = 32768
}

variable "backup_retention_days" {
  description = "Rétention des sauvegardes (7 à 35 jours)."
  type        = number
  default     = 7

  validation {
    condition     = var.backup_retention_days >= 7 && var.backup_retention_days <= 35
    error_message = "La rétention des sauvegardes doit être comprise entre 7 et 35 jours."
  }
}

variable "geo_redundant_backup_enabled" {
  description = "Sauvegarde géo-redondante (disponibilité à vérifier par région, ARC-53)."
  type        = bool
  default     = false
}

variable "high_availability_enabled" {
  description = "Haute disponibilité redondante en zone (SKU General Purpose ou Memory Optimized)."
  type        = bool
  default     = false
}

variable "entra_admin" {
  description = "Principal Entra ID administrateur du serveur."
  type = object({
    object_id      = string
    principal_name = string
    principal_type = string
  })
}

variable "databases" {
  description = "Noms des bases à créer (une par service, en snake_case — DAT-09)."
  type        = list(string)
  default     = []
}

variable "private_endpoint_subnet_id" {
  description = "Sous-réseau des points de terminaison privés de la zone."
  type        = string
}

variable "private_dns_zone_id" {
  description = "Zone DNS privée privatelink.postgres.database.azure.com."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Espace Log Analytics destinataire des journaux."
  type        = string
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
