# -----------------------------------------------------------------------------
# Module Container App : un microservice (§14.3) dans l'environnement de sa zone.
#   CTR-05 : aucune configuration d'environnement dans l'image (injectée ici).
#   CTR-12 : sondes de démarrage, de vivacité et de disponibilité ; arrêt propre.
#   CTR-13 : ressources CPU/mémoire dimensionnées ; mise à l'échelle KEDA (HTTP,
#            longueur des subscriptions Service Bus).
#   CTR-14 : au moins deux instances en production (calculé par le module racine).
#   CTR-16 : identité managée pour l'ACR, Key Vault et Service Bus ; secrets lus
#            dans Key Vault à l'exécution.
#   ARC-46 : entrée limitée au VNet et aux seuls appelants autorisés.
#   ARC-50 : révisions multiples pour le déploiement progressif.
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.7"
    }
  }
}

locals {
  http_port = 8080
}

resource "azurerm_container_app" "this" {
  name                         = var.name
  resource_group_name          = var.resource_group_name
  container_app_environment_id = var.container_app_environment_id
  workload_profile_name        = var.workload_profile_name
  revision_mode                = "Multiple"
  max_inactive_revisions       = 10
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.registry_server
    identity = var.identity_id
  }

  secret {
    name  = "appinsights-connection-string"
    value = var.app_insights_connection_string
  }

  dynamic "secret" {
    for_each = var.key_vault_secrets
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = var.identity_id
    }
  }

  ingress {
    # « external » dans un environnement interne = joignable depuis le VNet
    # uniquement, et restreint aux plages autorisées ci-dessous (ARC-46).
    external_enabled           = true
    target_port                = local.http_port
    transport                  = "http"
    allow_insecure_connections = false

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }

    dynamic "ip_security_restriction" {
      for_each = var.allowed_source_prefixes
      content {
        name             = "autorise-${ip_security_restriction.key}"
        action           = "Allow"
        ip_address_range = ip_security_restriction.value
      }
    }
  }

  template {
    min_replicas                     = var.min_replicas
    max_replicas                     = var.max_replicas
    termination_grace_period_seconds = var.termination_grace_period_seconds

    container {
      name   = var.name
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      env {
        name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        secret_name = "appinsights-connection-string"
      }

      dynamic "env" {
        for_each = var.environment_variables
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.key_vault_secrets
        content {
          name        = upper(replace(env.key, "-", "_"))
          secret_name = env.key
        }
      }

      # CTR-12 : sondes de santé exposées par chaque service sur le port 8080.
      startup_probe {
        transport               = "HTTP"
        port                    = local.http_port
        path                    = "/health/startup"
        initial_delay           = 5
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 10
      }

      liveness_probe {
        transport               = "HTTP"
        port                    = local.http_port
        path                    = "/health/live"
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        port                    = local.http_port
        path                    = "/health/ready"
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }

    # CTR-13 : mise à l'échelle sur la charge HTTP...
    http_scale_rule {
      name                = "http"
      concurrent_requests = tostring(var.http_concurrent_requests)
    }

    # ... et sur la longueur des subscriptions Service Bus (KEDA, identité managée).
    dynamic "custom_scale_rule" {
      for_each = var.servicebus_scale_rules
      content {
        name             = "sb-${custom_scale_rule.value.topic}"
        custom_rule_type = "azure-servicebus"
        identity_id      = var.identity_id
        metadata = {
          namespace        = var.servicebus_namespace_name
          topicName        = custom_scale_rule.value.topic
          subscriptionName = custom_scale_rule.value.subscription
          messageCount     = tostring(var.servicebus_message_count)
        }
      }
    }
  }

  lifecycle {
    # Les nouvelles versions d'image et la répartition du trafic entre révisions
    # sont pilotées par la chaîne CI/CD (ARC-50), pas par Terraform.
    ignore_changes = [
      template[0].container[0].image,
      ingress[0].traffic_weight,
    ]
  }
}
