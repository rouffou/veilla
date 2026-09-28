# Variables de la racine d'environnement, transmises au module racine infra/.
# Les descriptions détaillées figurent dans infra/variables.tf.

variable "subscription_id" {
  description = "Abonnement Azure cible (null = variable d'environnement ARM_SUBSCRIPTION_ID)."
  type        = string
  default     = null
}

variable "environment" {
  description = "Nom de l'environnement (dev, test, recette, preprod, prod)."
  type        = string
}

variable "address_space" {
  description = "Bloc /16 du VNet de l'environnement."
  type        = string
}

variable "zone_redundancy_enabled" {
  description = "Redondance de zone (CTR-14)."
  type        = bool
}

variable "enable_dr_replication" {
  description = "Réplication vers la région UE de reprise (ARC-52)."
  type        = bool
}

variable "dr_location" {
  description = "Région UE de reprise après sinistre (ARC-52)."
  type        = string
}

variable "default_min_replicas" {
  description = "Instances minimales par service (2 au moins en prod, CTR-14)."
  type        = number
}

variable "deploy_container_apps" {
  description = "Déploie les Container Apps (false au premier déploiement)."
  type        = bool
}

variable "image_tag" {
  description = "Étiquette immuable des images initiales (CTR-07)."
  type        = string
}

variable "dedicated_workload_profile" {
  description = "Profil de charge dédié des environnements Container Apps (null = Consumption)."
  type = object({
    workload_profile_type = string
    minimum_count         = number
    maximum_count         = number
  })
}

variable "postgres_sku_name" {
  description = "SKU des serveurs PostgreSQL."
  type        = string
}

variable "postgres_backup_retention_days" {
  description = "Rétention des sauvegardes PostgreSQL (jours)."
  type        = number
}

variable "postgres_high_availability_enabled" {
  description = "Haute disponibilité PostgreSQL redondante en zone."
  type        = bool
}

variable "postgres_geo_redundant_backup_enabled" {
  description = "Sauvegarde géo-redondante PostgreSQL."
  type        = bool
}

variable "postgres_entra_admin" {
  description = "Groupe Entra ID administrateur PostgreSQL."
  type = object({
    object_id      = string
    principal_name = string
    principal_type = optional(string, "Group")
  })
}

variable "servicebus_capacity" {
  description = "Unités de messagerie Service Bus Premium."
  type        = number
}

variable "storage_replication_type" {
  description = "Réplication du stockage documentaire."
  type        = string
}

variable "document_retention_days" {
  description = "Rétention WORM des documents (jours)."
  type        = number
}

variable "document_immutability_locked" {
  description = "Verrouillage (irréversible) de la stratégie WORM."
  type        = bool
}

variable "managed_hsm_enabled" {
  description = "Managed HSM pour les zones médicale et psychosociale (ARC-44)."
  type        = bool
}

variable "managed_hsm_admin_object_ids" {
  description = "Administrateurs initiaux des Managed HSM."
  type        = list(string)
}

variable "log_retention_days" {
  description = "Rétention Log Analytics (jours)."
  type        = number
}

variable "alert_email" {
  description = "Liste de diffusion d'exploitation (null = pas de groupe d'actions)."
  type        = string
}

variable "apim_sku_name" {
  description = "SKU API Management."
  type        = string
}

variable "apim_zones" {
  description = "Zones de disponibilité de la passerelle."
  type        = list(string)
}

variable "apim_publisher_email" {
  description = "Adresse de l'éditeur API Management."
  type        = string
}

variable "appgw_min_capacity" {
  description = "Capacité minimale de l'Application Gateway."
  type        = number
}

variable "appgw_max_capacity" {
  description = "Capacité maximale de l'Application Gateway."
  type        = number
}

variable "appgw_ssl_certificate_secret_id" {
  description = "Certificat TLS public dans Key Vault (null = écouteur HTTP d'amorçage)."
  type        = string
}

variable "waf_mode" {
  description = "Mode du WAF (Detection ou Prevention)."
  type        = string
}

variable "policy_effect" {
  description = "Effet des stratégies Azure Policy."
  type        = string
}

variable "additional_allowed_registries" {
  description = "Registres d'images supplémentaires autorisés."
  type        = list(string)
}

variable "tags" {
  description = "Étiquettes additionnelles."
  type        = map(string)
}
