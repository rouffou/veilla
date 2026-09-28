variable "name" {
  description = "Nom globalement unique du compte de stockage (3 à 24 caractères, minuscules et chiffres)."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.name))
    error_message = "Le nom du compte de stockage doit compter 3 à 24 minuscules ou chiffres."
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

variable "replication_type" {
  description = "Type de réplication (LRS, ZRS, GRS, GZRS...)."
  type        = string
  default     = "ZRS"
}

variable "containers" {
  description = "Conteneurs documentaires à créer (un par zone de sensibilité)."
  type        = list(string)
}

variable "immutability_period_in_days" {
  description = "Durée de rétention WORM (jours)."
  type        = number
}

variable "immutability_locked" {
  description = "Verrouille la stratégie WORM (irréversible)."
  type        = bool
  default     = false
}

variable "blob_contributor_principal_ids" {
  description = "Principaux autorisés à lire et écrire les documents (clé = nom du service)."
  type        = map(string)
  default     = {}
}

variable "private_endpoint_subnet_id" {
  description = "Sous-réseau des points de terminaison privés."
  type        = string
}

variable "private_dns_zone_id" {
  description = "Zone DNS privée privatelink.blob.core.windows.net."
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
