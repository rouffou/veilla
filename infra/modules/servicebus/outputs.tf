output "namespace_id" {
  description = "Identifiant du namespace Service Bus."
  value       = azurerm_servicebus_namespace.this.id
}

output "namespace_name" {
  description = "Nom du namespace (utilisé par les règles KEDA)."
  value       = azurerm_servicebus_namespace.this.name
}

output "fully_qualified_namespace" {
  description = "Nom d'hôte complet du namespace (<nom>.servicebus.windows.net)."
  value       = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net"

  depends_on = [azurerm_private_endpoint.this]
}

output "subscription_ids" {
  description = "Identifiants des subscriptions (clé = <topic>.<abonné>)."
  value       = { for k, s in azurerm_servicebus_subscription.this : k => s.id }

  depends_on = [azurerm_role_assignment.receiver, azurerm_role_assignment.scaler]
}
