variable "apim_name" {
  description = "Nom globalement unique de l'API Management."
  type        = string
}

variable "appgw_name" {
  description = "Nom de l'Application Gateway."
  type        = string
}

variable "resource_group_name" {
  description = "Groupe de ressources de la plateforme."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "sku_name" {
  description = "SKU API Management compatible injection VNet interne (Developer_1, Premium_N)."
  type        = string

  validation {
    condition     = can(regex("^(Developer|Premium)_[0-9]+$", var.sku_name))
    error_message = "Le mode VNet interne exige un SKU Developer ou Premium."
  }
}

variable "zones" {
  description = "Zones de disponibilité de l'API Management et de l'Application Gateway (vide = aucune)."
  type        = list(string)
  default     = []
}

variable "publisher_name" {
  description = "Nom de l'éditeur des API."
  type        = string
}

variable "publisher_email" {
  description = "Adresse de contact de l'éditeur des API."
  type        = string
}

variable "apim_subnet_id" {
  description = "Sous-réseau dédié à l'API Management."
  type        = string
}

variable "appgw_subnet_id" {
  description = "Sous-réseau dédié à l'Application Gateway."
  type        = string
}

variable "virtual_network_id" {
  description = "Réseau virtuel auquel lier la zone DNS privée azure-api.net."
  type        = string
}

variable "key_vault_id" {
  description = "Key Vault plateforme contenant le certificat TLS public."
  type        = string
}

variable "ssl_certificate_secret_id" {
  description = "Identifiant (sans version) du certificat TLS dans Key Vault ; null = écouteur HTTP d'amorçage."
  type        = string
  default     = null
}

variable "appgw_min_capacity" {
  description = "Capacité minimale de l'Application Gateway."
  type        = number
  default     = 1
}

variable "appgw_max_capacity" {
  description = "Capacité maximale de l'Application Gateway."
  type        = number
  default     = 3
}

variable "waf_mode" {
  description = "Mode du WAF : Detection ou Prevention."
  type        = string
  default     = "Prevention"

  validation {
    condition     = contains(["Detection", "Prevention"], var.waf_mode)
    error_message = "waf_mode doit valoir Detection ou Prevention."
  }
}

variable "rate_limit_per_minute" {
  description = "Nombre maximal de requêtes par minute et par adresse IP cliente (ARC-42)."
  type        = number
  default     = 1000
}

variable "bff_backends" {
  description = "Backends API Management : nom du BFF -> nom d'hôte privé de la Container App."
  type        = map(string)
  default     = {}
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
