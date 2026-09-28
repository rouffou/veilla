output "key_vault_id" {
  description = "Identifiant du Key Vault de la zone."
  value       = azurerm_key_vault.this.id
}

output "key_vault_uri" {
  description = "URI du Key Vault de la zone."
  value       = azurerm_key_vault.this.vault_uri

  depends_on = [azurerm_private_endpoint.key_vault]
}

output "managed_hsm_id" {
  description = "Identifiant du Managed HSM (null si non créé)."
  value       = one(azurerm_key_vault_managed_hardware_security_module.this[*].id)
}

output "managed_hsm_uri" {
  description = "URI du Managed HSM (null si non créé)."
  value       = one(azurerm_key_vault_managed_hardware_security_module.this[*].hsm_uri)

  depends_on = [azurerm_private_endpoint.managed_hsm]
}
