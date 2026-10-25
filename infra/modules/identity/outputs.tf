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
    azurerm_role_assignment.documents_kek,
    azurerm_key_vault_managed_hardware_security_module_role_assignment.documents_kek,
    azurerm_role_assignment.field_kek,
    azurerm_key_vault_managed_hardware_security_module_role_assignment.field_kek,
  ]
}

output "migration_identities" {
  description = "Identités de migration par service doté d'une base (ADR 0010) : id, principal_id, client_id et name (rôle PostgreSQL)."
  value = {
    for k, i in azurerm_user_assigned_identity.migration : k => {
      id           = i.id
      principal_id = i.principal_id
      client_id    = i.client_id
      name         = i.name
    }
  }

  # Le job de migration ne doit tirer l'image qu'une fois le rôle AcrPull attribué.
  depends_on = [azurerm_role_assignment.migration_acr_pull]
}
