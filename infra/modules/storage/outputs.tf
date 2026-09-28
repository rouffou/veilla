output "id" {
  description = "Identifiant du compte de stockage."
  value       = azurerm_storage_account.this.id
}

output "blob_endpoint" {
  description = "Point de terminaison Blob (résolu vers le point de terminaison privé)."
  value       = azurerm_storage_account.this.primary_blob_endpoint

  depends_on = [azurerm_private_endpoint.this]
}
