output "resource_group_names" {
  description = "Groupes de ressources par zone de sensibilité."
  value       = { for z, rg in azurerm_resource_group.zone : z => rg.name }
}

output "container_registry_login_server" {
  description = "Registre privé où la CI/CD publie les images (CTR-07)."
  value       = module.registry.login_server
}

output "container_apps_environment_domains" {
  description = "Domaine privé de l'environnement Container Apps de chaque zone."
  value       = { for z, env in module.container_apps_env : z => env.default_domain }
}

output "container_app_fqdns" {
  description = "Noms d'hôte privés des microservices déployés."
  value       = { for n, app in module.container_app : n => app.fqdn }
}

output "key_vault_uris" {
  description = "URI du Key Vault de chaque zone."
  value       = { for z, kv in module.keyvault : z => kv.key_vault_uri }
}

output "managed_hsm_uris" {
  description = "URI des Managed HSM des zones sensibles (à activer après création, ARC-44)."
  value       = { for z, kv in module.keyvault : z => kv.managed_hsm_uri if kv.managed_hsm_uri != null }
}

output "postgres_fqdns" {
  description = "Nom d'hôte du serveur PostgreSQL de chaque zone."
  value       = { for z, pg in module.postgres : z => pg.fqdn }
}

output "servicebus_namespace" {
  description = "Nom d'hôte complet du namespace Service Bus."
  value       = module.servicebus.fully_qualified_namespace
}

output "documents_blob_endpoints" {
  description = "Point de terminaison Blob du stockage documentaire WORM de chaque zone dotée d'une instance Documents."
  value       = { for z, st in module.storage : z => st.blob_endpoint }
}

output "documents_kek_uris" {
  description = "URI (sans version) de la clé racine documents-kek de chaque zone dotée d'une instance Documents."
  value       = { for z in local.documents_zones : z => module.keyvault[z].documents_kek_uri }
}

output "field_kek_uris" {
  description = "URI (sans version) de la clé racine <service>-fields-kek de chaque service qui chiffre des champs (ADR 0012)."
  value       = { for n, s in var.services : n => module.keyvault[s.zone].field_kek_uris["${n}-fields-kek"] if length(s.encryption) > 0 }
}

output "gateway_public_ip" {
  description = "Adresse IP publique de l'Application Gateway WAF (enregistrement DNS public à créer)."
  value       = module.gateway.public_ip_address
}

output "service_identities" {
  description = "Identités managées des services (client_id, principal_id)."
  value       = module.identity.identities
}

output "migration_identities" {
  description = "Identités de migration des services dotés d'une base (ADR 0010) : name (rôle PostgreSQL), principal_id (amorçage pgaadauth), client_id."
  value       = module.identity.migration_identities
}

output "migration_jobs" {
  description = "Job de migration de chaque service doté d'une base : nom et groupe de ressources (az containerapp job start)."
  value       = { for n, job in module.migration_job : n => { name = job.name, resource_group = azurerm_resource_group.zone[var.services[n].zone].name } }
}

output "action_group_id" {
  description = "Groupe d'actions d'exploitation ."
  value       = module.monitoring.action_group_id
}
