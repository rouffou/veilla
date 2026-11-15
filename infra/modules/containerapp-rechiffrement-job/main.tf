# -----------------------------------------------------------------------------
# Module job de rechiffrement des champs : « rech-<service> » (rechiffre) ou « verif-<service> » (vérifie seulement),
# selon la variable RECHIFFREMENT__* fournie par le module appelant (#396, #482, #474, ADR 0012).
#   Contrairement au job de migration (ADR 0010), il a besoin des clés du service : il s'exécute avec l'identité
#   d'EXÉCUTION du service (droits DML, déballage des DEK sur sa clé racine), ses secrets Key Vault --deks-- et --keys--
#   et sa configuration. Jamais une identité de pipeline élargie : le pipeline ne fait que démarrer le job.
#   Déclenchement manuel uniquement (az containerapp job start), une seule exécution à la fois, sans nouvelle tentative.
#   Le job n'a ni entrée réseau ni sonde : il s'arrête avec le code de sortie de RechiffrementHote (0 = complet).
#   Les variables RECHIFFREMENT__* ne figurent que dans ces jobs : jamais dans une révision de service (sinon le service
#   quitterait au démarrage et redémarrerait en boucle).
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

resource "azurerm_container_app_job" "this" {
  name                         = var.name
  resource_group_name          = var.resource_group_name
  location                     = var.location
  container_app_environment_id = var.container_app_environment_id
  workload_profile_name        = var.workload_profile_name
  replica_timeout_in_seconds   = var.replica_timeout_in_seconds
  # Un échec est examiné avant toute reprise : pas de nouvelle tentative automatique (le traitement est rejouable à la main).
  replica_retry_limit = 0
  tags                = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.registry_server
    identity = var.identity_id
  }

  dynamic "secret" {
    for_each = var.key_vault_secrets
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = var.identity_id
    }
  }

  # Déclenchement manuel (az containerapp job start), une exécution à la fois : aucun déclencheur planifié ni événementiel.
  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "rechiffrement"
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

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
    }
  }

  lifecycle {
    # L'image est mise à jour par la chaîne de déploiement (sans démarrer le job) en même temps que celle du service.
    ignore_changes = [template[0].container[0].image]
  }
}
