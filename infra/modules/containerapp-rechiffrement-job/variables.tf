variable "name" {
  description = "Nom du job : rech-<service> ou verif-<service> (minuscules, chiffres et tirets, 32 caractères au plus)."
  type        = string

  validation {
    condition     = can(regex("^(rech|verif)-[a-z0-9][a-z0-9-]*[a-z0-9]$", var.name)) && length(var.name) <= 32
    error_message = "Le nom doit valoir rech-<service> ou verif-<service>, 32 caractères au plus."
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
  description = "Identité managée d'EXÉCUTION du service (droits DML, déballage des DEK), jamais celle de migration ni du pipeline."
  type        = string
}

variable "registry_server" {
  description = "Nom d'hôte de l'Azure Container Registry (CTR-07)."
  type        = string
}

variable "image" {
  description = "Image du service (registre/dépôt:étiquette immuable), la même que celle de la révision."
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
  description = "Durée maximale d'une exécution (balayage de toute la base : plus long qu'une migration)."
  type        = number
  default     = 14400

  validation {
    condition     = var.replica_timeout_in_seconds >= 60 && var.replica_timeout_in_seconds <= 86400
    error_message = "Le délai maximal d'une exécution doit être compris entre 60 et 86400 secondes."
  }
}

variable "environment_variables" {
  description = "Variables d'environnement non sensibles : configuration du service et mode (rechiffrement ou vérification seule) du job."
  type        = map(string)
  default     = {}
}

variable "key_vault_secrets" {
  description = "Secrets Key Vault du service (--deks-- et --keys--) : nom du secret de l'application -> identifiant (sans version) du secret Key Vault."
  type        = map(string)
  default     = {}
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
