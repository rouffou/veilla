output "allowed_registries_definition_id" {
  description = "Identifiant de la définition personnalisée « registres autorisés »."
  value       = azurerm_policy_definition.allowed_registries.id
}
