output "id" {
  description = "Identifiant du registre."
  value       = azurerm_container_registry.this.id
}

output "login_server" {
  description = "Nom d'hôte de connexion du registre (<nom>.azurecr.io)."
  value       = azurerm_container_registry.this.login_server

  # Le registre n'est joignable qu'après création de son point de terminaison privé.
  depends_on = [azurerm_private_endpoint.this]
}
