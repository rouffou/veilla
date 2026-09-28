variable "name_prefix" {
  description = "Préfixe de nommage (ex. sepp-dev)."
  type        = string
}

variable "resource_group_name" {
  description = "Groupe de ressources cible."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "retention_days" {
  description = "Rétention des journaux et de la télémétrie (jours)."
  type        = number
  default     = 90
}

variable "alert_email" {
  description = "Adresse de la liste d'exploitation destinataire des alertes (null = aucun groupe d'actions)."
  type        = string
  default     = null
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
