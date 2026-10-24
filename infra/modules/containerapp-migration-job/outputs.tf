output "id" {
  description = "Identifiant du job de migration."
  value       = azurerm_container_app_job.this.id
}

output "name" {
  description = "Nom du job de migration (az containerapp job start --name)."
  value       = azurerm_container_app_job.this.name
}
