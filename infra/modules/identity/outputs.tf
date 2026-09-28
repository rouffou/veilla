output "identities" {
  description = "Identités managées par service : id, principal_id, client_id."
  value = {
    for k, i in azurerm_user_assigned_identity.this : k => {
      id           = i.id
      principal_id = i.principal_id
      client_id    = i.client_id
    }
  }

  # Les applications ne doivent démarrer qu'une fois leurs droits attribués.
  depends_on = [
    azurerm_role_assignment.acr_pull,
    azurerm_role_assignment.key_vault_secrets_user,
    azurerm_role_assignment.key_vault_crypto_user,
  ]
}
