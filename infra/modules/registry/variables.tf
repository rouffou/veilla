variable "name" {
  description = "Nom globalement unique du registre (alphanumérique, 5 à 50 caractères)."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z0-9]{5,50}$", var.name))
    error_message = "Le nom du registre doit être alphanumérique (5 à 50 caractères)."
  }
}

variable "resource_group_name" {
  description = "Groupe de ressources cible."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "zone_redundancy_enabled" {
  description = "Active la redondance de zone du registre (CTR-14)."
  type        = bool
  default     = false
}

variable "replication_location" {
  description = "Région UE de géo-réplication (ARC-52) ; null pour désactiver."
  type        = string
  default     = null
}

variable "untagged_retention_days" {
  description = "Durée de conservation des manifestes non étiquetés (CTR-07)."
  type        = number
  default     = 30
}

variable "private_endpoint_subnet_id" {
  description = "Sous-réseau des points de terminaison privés de la plateforme."
  type        = string
}

variable "private_dns_zone_id" {
  description = "Zone DNS privée privatelink.azurecr.io."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Espace Log Analytics destinataire des journaux de diagnostic."
  type        = string
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
