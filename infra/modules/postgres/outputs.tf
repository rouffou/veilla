output "server_id" {
  description = "Identifiant du serveur PostgreSQL."
  value       = azurerm_postgresql_flexible_server.this.id
}

output "fqdn" {
  description = "Nom d'hôte du serveur (résolu vers le point de terminaison privé)."
  value       = azurerm_postgresql_flexible_server.this.fqdn

  depends_on = [azurerm_private_endpoint.this]
}

output "database_names" {
  description = "Bases créées sur le serveur."
  value       = [for d in azurerm_postgresql_flexible_server_database.this : d.name]
}
