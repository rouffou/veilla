output "log_analytics_workspace_id" {
  description = "Identifiant de l'espace de travail Log Analytics."
  value       = azurerm_log_analytics_workspace.this.id
}

output "application_insights_id" {
  description = "Identifiant de la ressource Application Insights."
  value       = azurerm_application_insights.this.id
}

output "application_insights_connection_string" {
  description = "Chaîne de connexion Application Insights (contient la clé d'instrumentation : sensible)."
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
}

output "action_group_id" {
  description = "Identifiant du groupe d'actions d'exploitation (null si non créé)."
  value       = one(azurerm_monitor_action_group.ops[*].id)
}
