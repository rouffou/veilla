variable "name" {
  description = "Nom de la Container App (minuscules, chiffres et tirets, 32 caractères au plus)."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{0,30}[a-z0-9]$", var.name))
    error_message = "Le nom doit compter au plus 32 caractères : minuscules, chiffres et tirets."
  }
}

variable "resource_group_name" {
  description = "Groupe de ressources de la zone."
  type        = string
}

variable "container_app_environment_id" {
  description = "Environnement Container Apps de la zone du service."
  type        = string
}

variable "workload_profile_name" {
  description = "Profil de charge de l'environnement à utiliser."
  type        = string
  default     = "Consumption"
}

variable "identity_id" {
  description = "Identité managée user-assigned du service (CTR-16)."
  type        = string
}

variable "registry_server" {
  description = "Nom d'hôte de l'Azure Container Registry (CTR-07)."
  type        = string
}

variable "image" {
  description = "Image complète du conteneur (registre/dépôt:étiquette immuable)."
  type        = string
}

variable "cpu" {
  description = "vCPU alloués au conteneur (CTR-13)."
  type        = number
  default     = 0.5
}

variable "memory" {
  description = "Mémoire allouée au conteneur, ex. 1Gi (CTR-13)."
  type        = string
  default     = "1Gi"
}

variable "min_replicas" {
  description = "Nombre minimal d'instances (CTR-14)."
  type        = number
  default     = 1
}

variable "max_replicas" {
  description = "Nombre maximal d'instances."
  type        = number
  default     = 10
}

variable "termination_grace_period_seconds" {
  description = "Délai d'arrêt propre après le signal (fin des traitements, publication de l'outbox — CTR-12)."
  type        = number
  default     = 60
}

variable "http_concurrent_requests" {
  description = "Nombre de requêtes simultanées par instance déclenchant la mise à l'échelle."
  type        = number
  default     = 50
}

variable "servicebus_namespace_name" {
  description = "Nom du namespace Service Bus (règles KEDA)."
  type        = string
  default     = null
}

variable "servicebus_scale_rules" {
  description = "Subscriptions Service Bus surveillées par KEDA."
  type = list(object({
    topic        = string
    subscription = string
  }))
  default = []
}

variable "servicebus_message_count" {
  description = "Nombre de messages en attente par instance déclenchant la mise à l'échelle."
  type        = number
  default     = 20
}

variable "allowed_source_prefixes" {
  description = "Plages CIDR autorisées à appeler le service (restriction d'entrée, ARC-46). Vide = tout le VNet."
  type        = list(string)
  default     = []
}

variable "environment_variables" {
  description = "Variables d'environnement non sensibles (CTR-05)."
  type        = map(string)
  default     = {}
}

variable "key_vault_secrets" {
  description = "Secrets Key Vault exposés au conteneur : nom du secret de l'application -> identifiant (sans version) du secret Key Vault."
  type        = map(string)
  default     = {}
}

variable "app_insights_connection_string" {
  description = "Chaîne de connexion Application Insights (ARC-47)."
  type        = string
  sensitive   = true
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
