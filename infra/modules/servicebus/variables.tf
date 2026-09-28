variable "name" {
  description = "Nom globalement unique du namespace Service Bus."
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

variable "capacity" {
  description = "Unités de messagerie Premium (1, 2, 4, 8 ou 16)."
  type        = number
  default     = 1

  validation {
    condition     = contains([1, 2, 4, 8, 16], var.capacity)
    error_message = "La capacité Premium doit valoir 1, 2, 4, 8 ou 16."
  }
}

variable "topics" {
  description = "Topics à créer : un par service publieur (le nom du topic est le nom du service)."
  type        = list(string)
}

variable "subscriptions" {
  description = "Subscriptions à créer, clé = <topic>.<abonné>."
  type = map(object({
    topic      = string
    subscriber = string
  }))
  default = {}
}

variable "principal_ids" {
  description = "Principal ID de l'identité managée de chaque service (clé = nom du service)."
  type        = map(string)
}

variable "queue_scaled_services" {
  description = "Services dont la mise à l'échelle KEDA dépend de la longueur de leurs subscriptions."
  type        = list(string)
  default     = []
}

variable "private_endpoint_subnet_id" {
  description = "Sous-réseau des points de terminaison privés de la plateforme."
  type        = string
}

variable "private_dns_zone_id" {
  description = "Zone DNS privée privatelink.servicebus.windows.net."
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
