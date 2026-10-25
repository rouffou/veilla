variable "name_prefix" {
  description = "Préfixe de nommage (ex. sepp-dev)."
  type        = string
}

variable "resource_group_name" {
  description = "Groupe de ressources qui porte les alertes et le workbook (zone platform)."
  type        = string
}

variable "location" {
  description = "Région Azure."
  type        = string
}

variable "action_group_id" {
  description = "Groupe d'actions d'exploitation notifié par toutes les alertes."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Espace Log Analytics qui reçoit la télémétrie OpenTelemetry (AppRequests, AppTraces) et les journaux de plateforme."
  type        = string
}

variable "application_insights_id" {
  description = "Ressource Application Insights à laquelle le workbook est rattaché."
  type        = string
}

variable "servicebus_namespace_id" {
  description = "Namespace Service Bus (métrique DeadletteredMessages, files du workbook)."
  type        = string
}

variable "postgres_server_ids" {
  description = "Serveurs PostgreSQL flexibles par zone (clé = zone). Une règle de métrique est créée par serveur et par indicateur."
  type        = map(string)
  default     = {}
}

variable "container_app_ids" {
  description = "Container Apps par service (clé = nom du service). Vide quand les applications ne sont pas déployées : aucune règle propre aux applications n'est alors créée."
  type        = map(string)
  default     = {}
}

variable "thresholds" {
  description = <<-EOT
    Seuils des règles d'alerte (valeurs par défaut raisonnables, à ajuster après observation de la charge réelle, NF-62).
      error_rate_percent          : part de réponses 5xx par service (%) sur la fenêtre.
      error_rate_min_requests     : nombre minimal de requêtes sur la fenêtre pour évaluer le taux (évite le bruit à faible trafic).
      latency_p95_ms              : latence p95 par service (ms) sur la fenêtre.
      deadletter_messages         : messages en file morte au-delà desquels on alerte (0 = au moins un message).
      restart_count               : redémarrages de réplicas d'une application sur 15 minutes.
      probe_failures              : échecs de sonde (système Container Apps) par application sur 15 minutes.
      postgres_cpu_percent        : CPU PostgreSQL moyen (%) sur 15 minutes.
      postgres_storage_percent    : stockage PostgreSQL utilisé (%).
      postgres_memory_percent     : mémoire PostgreSQL moyenne (%) sur 15 minutes.
      postgres_active_connections : connexions actives maximales (dépend du SKU, voir max_connections du serveur).
      outbox_publish_failures     : échecs de publication de messages d'outbox par service sur 15 minutes.
      waf_blocked_requests        : requêtes bloquées par le WAF sur 15 minutes.
  EOT
  type = object({
    error_rate_percent          = optional(number, 5)
    error_rate_min_requests     = optional(number, 50)
    latency_p95_ms              = optional(number, 2000)
    deadletter_messages         = optional(number, 0)
    restart_count               = optional(number, 3)
    probe_failures              = optional(number, 10)
    postgres_cpu_percent        = optional(number, 85)
    postgres_storage_percent    = optional(number, 85)
    postgres_memory_percent     = optional(number, 90)
    postgres_active_connections = optional(number, 80)
    outbox_publish_failures     = optional(number, 5)
    waf_blocked_requests        = optional(number, 200)
  })
  default = {}
}

variable "severities" {
  description = "Sévérité Azure Monitor de chaque règle (0 critique, 1 erreur, 2 avertissement, 3 information, 4 verbeux)."
  type = object({
    error_rate         = optional(number, 1)
    latency            = optional(number, 2)
    deadletter         = optional(number, 1)
    restarts           = optional(number, 2)
    probe_failures     = optional(number, 2)
    postgres_cpu       = optional(number, 2)
    postgres_storage   = optional(number, 1)
    postgres_memory    = optional(number, 2)
    postgres_conns     = optional(number, 2)
    outbox_batch       = optional(number, 1)
    outbox_publish     = optional(number, 2)
    waf                = optional(number, 3)
    cle_racine         = optional(number, 0)
    migration_failures = optional(number, 1)
  })
  default = {}

  validation {
    condition     = alltrue([for s in values(var.severities) : s >= 0 && s <= 4])
    error_message = "Les sévérités vont de 0 (critique) à 4 (verbeux)."
  }
}

variable "tags" {
  description = "Étiquettes appliquées aux ressources."
  type        = map(string)
  default     = {}
}
