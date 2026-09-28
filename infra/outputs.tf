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

output "documents_blob_endpoint" {
  description = "Point de terminaison Blob du stockage documentaire WORM."
  value       = module.storage.blob_endpoint
}

output "gateway_public_ip" {
  description = "Adresse IP publique de l'Application Gateway WAF (enregistrement DNS public à créer)."
  value       = module.gateway.public_ip_address
}

output "service_identities" {
  description = "Identités managées des services (client_id, principal_id)."
  value       = module.identity.identities
}

output "action_group_id" {
  description = "Groupe d'actions d'exploitation (null si alert_email n'est pas renseigné)."
  value       = module.monitoring.action_group_id
}
