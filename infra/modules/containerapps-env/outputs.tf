output "id" {
  description = "Identifiant de l'environnement Container Apps."
  value       = azurerm_container_app_environment.this.id
}

output "default_domain" {
  description = "Domaine par défaut de l'environnement (résolu en privé dans le VNet)."
  value       = azurerm_container_app_environment.this.default_domain

  depends_on = [azurerm_private_dns_a_record.wildcard, azurerm_private_dns_zone_virtual_network_link.this]
}

output "static_ip_address" {
  description = "Adresse IP privée de l'équilibreur interne de l'environnement."
  value       = azurerm_container_app_environment.this.static_ip_address
}

output "workload_profile_name" {
  description = "Profil de charge à utiliser par les applications (dédié s'il existe, sinon Consumption)."
  value       = var.dedicated_workload_profile == null ? "Consumption" : "dedie"
}
