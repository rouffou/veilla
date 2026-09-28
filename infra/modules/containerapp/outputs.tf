output "id" {
  description = "Identifiant de la Container App."
  value       = azurerm_container_app.this.id
}

output "fqdn" {
  description = "Nom d'hôte privé de l'application (<nom>.<domaine de l'environnement>)."
  value       = azurerm_container_app.this.ingress[0].fqdn
}
