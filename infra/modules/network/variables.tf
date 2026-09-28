variable "name_prefix" {
  description = "Préfixe de nommage (ex. sepp-dev)."
  type        = string
}

variable "resource_group_name" {
  description = "Groupe de ressources de la plateforme qui héberge le réseau."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "address_space" {
  description = "Bloc CIDR /16 du réseau virtuel."
  type        = string
}

variable "zones" {
  description = "Zones de sensibilité, dans l'ordre d'allocation des sous-réseaux (CTR-10)."
  type        = list(string)

  validation {
    condition     = contains(var.zones, "platform") && length(var.zones) <= 4
    error_message = "La liste doit contenir la zone platform et au plus 4 zones (plan d'adressage)."
  }
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
