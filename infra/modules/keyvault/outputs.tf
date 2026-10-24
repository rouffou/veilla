output "key_vault_id" {
  description = "Identifiant du Key Vault de la zone."
  value       = azurerm_key_vault.this.id
}

output "key_vault_uri" {
  description = "URI du Key Vault de la zone."
  value       = azurerm_key_vault.this.vault_uri

  depends_on = [azurerm_private_endpoint.key_vault]
}

output "documents_kek_id" {
  description = "Identifiant ARM de la clé racine de Documents (null si non créée) : portée du rôle Key Vault Crypto Service Encryption User."
  value       = one(azapi_resource.documents_kek[*].id)
}

output "documents_kek_name" {
  description = "Nom de la clé racine de Documents (null si non créée)."
  value       = var.documents_kek_enabled ? var.documents_kek_name : null
}

output "documents_kek_managed_hsm_id" {
  description = "Identifiant du Managed HSM qui porte la clé racine (null si la clé est dans le Key Vault ou n'est pas créée)."
  value       = var.documents_kek_enabled && var.managed_hsm_enabled ? one(azurerm_key_vault_managed_hardware_security_module.this[*].id) : null
}

output "documents_kek_uri" {
  description = "URI de la clé racine de Documents, sans version (null si non créée) : valeur de Documents:Enveloppe:CleKek."
  value = !var.documents_kek_enabled ? null : "${trimsuffix(
    var.managed_hsm_enabled ? one(azurerm_key_vault_managed_hardware_security_module.this[*].hsm_uri) : azurerm_key_vault.this.vault_uri, "/"
  )}/keys/${var.documents_kek_name}"

  # L'application ne démarre qu'une fois la clé créée et le point privé en place.
  depends_on = [azapi_resource.documents_kek, azurerm_private_endpoint.key_vault, azurerm_private_endpoint.managed_hsm]
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
