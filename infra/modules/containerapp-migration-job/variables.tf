variable "name" {
  description = "Nom du job (minuscules, chiffres et tirets, 32 caractères au plus)."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{0,30}[a-z0-9]$", var.name))
    error_message = "Le nom doit compter au plus 32 caractères : minuscules, chiffres et tirets."
  }
}

variable "resource_group_name" {
  description = "Groupe de ressources de la zone du service."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "container_app_environment_id" {
  description = "Environnement Container Apps de la zone du service (accès au point privé PostgreSQL de la zone)."
  type        = string
}

variable "workload_profile_name" {
  description = "Profil de charge de l'environnement à utiliser."
  type        = string
  default     = "Consumption"
}

variable "identity_id" {
  description = "Identité managée de migration du service (droits DDL, distincte de l'identité d'exécution)."
  type        = string
}

variable "registry_server" {
  description = "Nom d'hôte de l'Azure Container Registry (CTR-07)."
  type        = string
}

variable "image" {
  description = "Image du service (registre/dépôt:étiquette immuable), la même que celle de la révision à déployer."
  type        = string
}

variable "cpu" {
  description = "vCPU alloués à l'exécution du job."
  type        = number
  default     = 0.5
}

variable "memory" {
  description = "Mémoire allouée à l'exécution du job, ex. 1Gi."
  type        = string
  default     = "1Gi"
}

variable "replica_timeout_in_seconds" {
  description = "Durée maximale d'une exécution (migration et données de départ)."
  type        = number
  default     = 1800
}

variable "environment_variables" {
  description = "Variables d'environnement non sensibles : configuration du service, identité et mode de migration (aucun secret)."
  type        = map(string)
  default     = {}
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
