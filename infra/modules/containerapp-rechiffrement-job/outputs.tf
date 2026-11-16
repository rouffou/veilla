output "id" {
  description = "Identifiant du job."
  value       = azurerm_container_app_job.this.id
}

output "name" {
  description = "Nom du job (az containerapp job start --name)."
  value       = azurerm_container_app_job.this.name
}
