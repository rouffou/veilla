# -----------------------------------------------------------------------------
# Module alertes et tableau de bord techniques.
#   NF-62  : supervision, alertes et tableaux de bord mis à disposition du SEPP.
#   ARC-47 : la télémétrie OpenTelemetry (AppRequests, AppTraces) alimente les règles.
#   ARC-44 : alerte sur l'indisponibilité de la clé racine de Documents (journal Error).
#   ADR 0010 : alerte sur l'échec des jobs de migration mig-*.
# Toutes les règles notifient le groupe d'actions d'exploitation du module monitoring.
# Aucune donnée médicale, psychosociale ou d'identité n'est lue : les requêtes ne
# portent que sur des compteurs, des durées et des messages d'exploitation (ARC-06).
# Procédure de réponse : infra/README.md, section « Supervision et alertes ».
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.80"
    }
  }
}

locals {
  t = var.thresholds
  s = var.severities

  has_apps = length(var.container_app_ids) > 0

  # Règles sur journaux (requêtes KQL sur l'espace Log Analytics).
  #   mesure   : colonne numérique comparée au seuil (null = nombre de lignes retournées).
  #   dimension: colonne qui sépare les alertes (une alerte par service, par exemple).
  #   activee  : false quand les ressources interrogées n'existent pas (applications non déployées).
  log_rules = {
    taux-5xx = {
      description = "Part de réponses HTTP 5xx supérieure à ${local.t.error_rate_percent} % pour un service (minimum ${local.t.error_rate_min_requests} requêtes sur 15 minutes)."
      severity    = local.s.error_rate
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = local.t.error_rate_percent
      aggregation = "Maximum"
      measure     = "Valeur"
      dimension   = "AppRoleName"
      periods     = { evaluated = 2, failing = 2 }
      enabled     = true
      query       = <<-KQL
        AppRequests
        | summarize Total = count(), Erreurs = countif(toint(ResultCode) >= 500) by AppRoleName
        | where Total >= ${local.t.error_rate_min_requests}
        | extend Valeur = round(100.0 * Erreurs / Total, 1)
      KQL
    }
    latence-p95 = {
      description = "Latence p95 supérieure à ${local.t.latency_p95_ms} ms pour un service (minimum ${local.t.error_rate_min_requests} requêtes sur 15 minutes)."
      severity    = local.s.latency
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = local.t.latency_p95_ms
      aggregation = "Maximum"
      measure     = "Valeur"
      dimension   = "AppRoleName"
      periods     = { evaluated = 2, failing = 2 }
      enabled     = true
      query       = <<-KQL
        AppRequests
        | summarize Total = count(), Valeur = percentile(DurationMs, 95) by AppRoleName
        | where Total >= ${local.t.error_rate_min_requests}
      KQL
    }
    outbox-lot = {
      description = "Échec du traitement d'un lot de l'outbox (journal Error d'OutboxProcessor) : des événements d'intégration ne sont plus publiés."
      severity    = local.s.outbox_batch
      frequency   = "PT5M"
      window      = "PT10M"
      threshold   = 0
      aggregation = "Maximum"
      measure     = "Echecs"
      dimension   = "AppRoleName"
      periods     = { evaluated = 1, failing = 1 }
      enabled     = true
      query       = <<-KQL
        AppTraces
        | where SeverityLevel >= 3 and Message contains "Échec du traitement d'un lot de l'outbox"
        | summarize Echecs = count() by AppRoleName
      KQL
    }
    outbox-publication = {
      description = "Plus de ${local.t.outbox_publish_failures} échecs de publication de messages d'outbox sur 15 minutes pour un service (journal Warning d'OutboxProcessor)."
      severity    = local.s.outbox_publish
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = local.t.outbox_publish_failures
      aggregation = "Maximum"
      measure     = "Echecs"
      dimension   = "AppRoleName"
      periods     = { evaluated = 1, failing = 1 }
      enabled     = true
      query       = <<-KQL
        AppTraces
        | where Message contains "Échec de publication du message"
        | summarize Echecs = count() by AppRoleName
      KQL
    }
    documents-cle-racine = {
      description = "Documents : version de clé racine inutilisable (erreur document.cle-racine-indisponible, journal Error). Ne pas désactiver ni supprimer une version utilisée par des archives : restaurer la version ou rétablir l'accès (ARC-44)."
      severity    = local.s.cle_racine
      frequency   = "PT5M"
      window      = "PT5M"
      threshold   = 0
      aggregation = "Maximum"
      measure     = "Erreurs"
      dimension   = "AppRoleName"
      periods     = { evaluated = 1, failing = 1 }
      enabled     = true
      query       = <<-KQL
        AppTraces
        | where SeverityLevel >= 3 and Message contains "version de clé racine" and Message contains "inutilisable"
        | summarize Erreurs = count() by AppRoleName
      KQL
    }
    migration-echec-telemetrie = {
      description = "Un job de migration mig-* a journalisé une erreur (service <nom>-migration, ADR 0010) : ne pas déployer la nouvelle révision."
      severity    = local.s.migration_failures
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = 0
      aggregation = "Maximum"
      measure     = "Erreurs"
      dimension   = "AppRoleName"
      periods     = { evaluated = 1, failing = 1 }
      enabled     = true
      query       = <<-KQL
        AppTraces
        | where AppRoleName endswith "-migration" and SeverityLevel >= 3
        | summarize Erreurs = count() by AppRoleName
      KQL
    }
    migration-echec-console = {
      description = "Un job de migration mig-* a écrit une erreur ou une exception non gérée sur sa sortie standard (couvre aussi un échec avant l'initialisation de la télémétrie)."
      severity    = local.s.migration_failures
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = 0
      aggregation = "Maximum"
      measure     = "Erreurs"
      dimension   = "Job"
      periods     = { evaluated = 1, failing = 1 }
      enabled     = local.has_apps
      query       = <<-KQL
        ContainerAppConsoleLogs_CL
        | extend Job = tostring(column_ifexists("ContainerJobName_s", ""))
        | where Job startswith "mig-" and (Log_s startswith "fail:" or Log_s contains "Unhandled exception")
        | summarize Erreurs = count() by Job
      KQL
    }
    sondes-echecs = {
      description = "Plus de ${local.t.probe_failures} échecs de sonde (démarrage, vivacité ou disponibilité) sur 15 minutes pour une Container App."
      severity    = local.s.probe_failures
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = local.t.probe_failures
      aggregation = "Maximum"
      measure     = "Echecs"
      dimension   = "Application"
      periods     = { evaluated = 1, failing = 1 }
      enabled     = local.has_apps
      query       = <<-KQL
        ContainerAppSystemLogs_CL
        | where Reason_s has "ProbeFailed" or Log_s has "probe failed"
        | extend Application = tostring(column_ifexists("ContainerAppName_s", ""))
        | summarize Echecs = count() by Application
      KQL
    }
    waf-blocages = {
      description = "Plus de ${local.t.waf_blocked_requests} requêtes bloquées par le WAF sur 15 minutes (attaque ou règle trop stricte)."
      severity    = local.s.waf
      frequency   = "PT5M"
      window      = "PT15M"
      threshold   = local.t.waf_blocked_requests
      aggregation = "Maximum"
      measure     = "Bloquees"
      dimension   = null
      periods     = { evaluated = 1, failing = 1 }
      enabled     = true
      query       = <<-KQL
        AzureDiagnostics
        | where ResourceType == "APPLICATIONGATEWAYS" and Category == "ApplicationGatewayFirewallLog"
        | where tostring(column_ifexists("action_s", "")) == "Blocked"
        | summarize Bloquees = count()
      KQL
    }
  }

  # Métriques PostgreSQL : une règle par serveur (zone) et par indicateur.
  postgres_metrics = {
    cpu = {
      metric    = "cpu_percent"
      aggregate = "Average"
      threshold = local.t.postgres_cpu_percent
      severity  = local.s.postgres_cpu
      window    = "PT15M"
      label     = "CPU"
    }
    stockage = {
      metric    = "storage_percent"
      aggregate = "Maximum"
      threshold = local.t.postgres_storage_percent
      severity  = local.s.postgres_storage
      window    = "PT15M"
      label     = "stockage"
    }
    memoire = {
      metric    = "memory_percent"
      aggregate = "Average"
      threshold = local.t.postgres_memory_percent
      severity  = local.s.postgres_memory
      window    = "PT15M"
      label     = "mémoire"
    }
    connexions = {
      metric    = "active_connections"
      aggregate = "Maximum"
      threshold = local.t.postgres_active_connections
      severity  = local.s.postgres_conns
      window    = "PT15M"
      label     = "connexions actives"
    }
  }

  postgres_rules = merge([
    for zone, id in var.postgres_server_ids : {
      for k, m in local.postgres_metrics : "${zone}-${k}" => merge(m, { zone = zone, server_id = id })
    }
  ]...)
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "log" {
  for_each = { for k, r in local.log_rules : k => r if r.enabled }

  name                 = "alert-${var.name_prefix}-${each.key}"
  display_name         = "SEPP ${var.name_prefix} : ${each.key}"
  resource_group_name  = var.resource_group_name
  location             = var.location
  description          = each.value.description
  severity             = each.value.severity
  enabled              = true
  evaluation_frequency = each.value.frequency
  window_duration      = each.value.window
  scopes               = [var.log_analytics_workspace_id]
  # Les tables Container Apps et WAF n'existent qu'après la première ingestion : pas de validation au plan.
  skip_query_validation            = true
  auto_mitigation_enabled          = true
  workspace_alerts_storage_enabled = false
  tags                             = var.tags

  criteria {
    query                   = each.value.query
    time_aggregation_method = each.value.aggregation
    metric_measure_column   = each.value.measure
    operator                = "GreaterThan"
    threshold               = each.value.threshold

    dynamic "dimension" {
      for_each = each.value.dimension == null ? [] : [each.value.dimension]
      content {
        name     = dimension.value
        operator = "Include"
        values   = ["*"]
      }
    }

    failing_periods {
      minimum_failing_periods_to_trigger_alert = each.value.periods.failing
      number_of_evaluation_periods             = each.value.periods.evaluated
    }
  }

  action {
    action_groups = [var.action_group_id]
  }
}

# Messages en file morte Service Bus (par entité : topic/subscription).
resource "azurerm_monitor_metric_alert" "servicebus" {
  for_each = { dlq = true }

  name                = "alert-${var.name_prefix}-servicebus-file-morte"
  resource_group_name = var.resource_group_name
  description         = "Messages en file morte Service Bus au-delà de ${local.t.deadletter_messages} : un consommateur rejette des événements (voir la procédure de reprise de l'infra/README.md)."
  scopes              = [var.servicebus_namespace_id]
  severity            = local.s.deadletter
  frequency           = "PT5M"
  window_size         = "PT15M"
  auto_mitigate       = true
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "DeadletteredMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = local.t.deadletter_messages

    dimension {
      name     = "EntityName"
      operator = "Include"
      values   = ["*"]
    }
  }

  action {
    action_group_id = var.action_group_id
  }
}

resource "azurerm_monitor_metric_alert" "postgres" {
  for_each = local.postgres_rules

  name                = "alert-${var.name_prefix}-postgres-${each.key}"
  resource_group_name = var.resource_group_name
  description         = "PostgreSQL zone ${each.value.zone} : ${each.value.label} au-delà de ${each.value.threshold}."
  scopes              = [each.value.server_id]
  severity            = each.value.severity
  frequency           = "PT5M"
  window_size         = each.value.window
  auto_mitigate       = true
  tags                = merge(var.tags, { zone = each.value.zone })

  criteria {
    metric_namespace = "Microsoft.DBforPostgreSQL/flexibleServers"
    metric_name      = each.value.metric
    aggregation      = each.value.aggregate
    operator         = "GreaterThan"
    threshold        = each.value.threshold
  }

  action {
    action_group_id = var.action_group_id
  }
}

# Redémarrages de réplicas (plantage, OOM, échec de sonde de vivacité) par Container App.
resource "azurerm_monitor_metric_alert" "restarts" {
  for_each = var.container_app_ids

  name                = "alert-${var.name_prefix}-redemarrages-${each.key}"
  resource_group_name = var.resource_group_name
  description         = "Plus de ${local.t.restart_count} redémarrages de réplicas de ${each.key} sur 15 minutes."
  scopes              = [each.value]
  severity            = local.s.restarts
  frequency           = "PT5M"
  window_size         = "PT15M"
  auto_mitigate       = true
  tags                = merge(var.tags, { service = each.key })

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = "RestartCount"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = local.t.restart_count
  }

  action {
    action_group_id = var.action_group_id
  }
}

# Tableau de bord technique : disponibilité, débit, erreurs, latence, Service Bus, PostgreSQL.
resource "azurerm_application_insights_workbook" "supervision" {
  # Le nom d'une ressource workbook est un GUID : dérivé du préfixe pour rester stable.
  name                = uuidv5("url", "https://sepp.example.be/workbooks/supervision/${var.name_prefix}")
  resource_group_name = var.resource_group_name
  location            = var.location
  display_name        = "SEPP ${var.name_prefix} : supervision technique"
  source_id           = lower(var.application_insights_id)
  tags                = var.tags

  data_json = templatefile("${path.module}/workbook.json.tftpl", {
    workspace_id         = var.log_analytics_workspace_id
    servicebus_id        = var.servicebus_namespace_id
    postgres_server_ids  = jsonencode(values(var.postgres_server_ids))
    latency_threshold_ms = local.t.latency_p95_ms
    error_rate_threshold = local.t.error_rate_percent
  })
}
