# -----------------------------------------------------------------------------
# Module job de migration : applique les migrations de schéma EF Core d'un
# service avant le déploiement de sa nouvelle révision (ADR 0010).
#   ARC-50 : ordre migration -> révision -> trafic, piloté par la chaîne de
#            déploiement (déclenchement manuel du job, image mise à jour par la CI).
#   CTR-05 : même image que le service, configuration injectée ici
#            (Database__MigrateAndExit=true : migrer puis quitter).
#   CTR-16 : identité managée de migration, distincte de celle du service ;
#            droits DDL portés par son rôle PostgreSQL ; aucun secret.
# Le job n'a ni entrée réseau ni sonde : il s'arrête avec le code de sortie de
# la migration (0 = succès), sans nouvelle tentative automatique.
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
  # Un échec est examiné avant toute reprise : pas de nouvelle tentative automatique.
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

  # Déclenchement manuel (az containerapp job start) par la chaîne de déploiement, une exécution à la fois.
  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "migration"
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
    }
  }

  lifecycle {
    # L'image est mise à jour par la chaîne de déploiement avant chaque exécution (même étiquette que la révision).
    ignore_changes = [template[0].container[0].image]
  }
}
