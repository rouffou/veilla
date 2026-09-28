variable "name" {
  description = "Nom de l'environnement Container Apps."
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

variable "infrastructure_subnet_id" {
  description = "Sous-réseau délégué à Microsoft.App/environments (CTR-10)."
  type        = string
}

variable "virtual_network_id" {
  description = "Réseau virtuel auquel lier la zone DNS privée de l'environnement."
  type        = string
}

variable "zone_redundancy_enabled" {
  description = "Active la redondance de zone (CTR-14)."
  type        = bool
  default     = false
}

variable "dedicated_workload_profile" {
  description = "Profil de charge dédié additionnel (null = Consumption uniquement)."
  type = object({
    workload_profile_type = string
    minimum_count         = number
    maximum_count         = number
  })
  default = null
}

variable "log_analytics_workspace_id" {
  description = "Espace Log Analytics destinataire des journaux de l'environnement."
  type        = string
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
