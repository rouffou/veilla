output "apim_id" {
  description = "Identifiant de l'API Management."
  value       = azurerm_api_management.this.id
}

output "apim_private_ip_addresses" {
  description = "Adresses IP privées de l'API Management."
  value       = azurerm_api_management.this.private_ip_addresses
}

output "public_ip_address" {
  description = "Adresse IP publique de l'Application Gateway (seul point d'entrée Internet)."
  value       = azurerm_public_ip.appgw.ip_address
}
