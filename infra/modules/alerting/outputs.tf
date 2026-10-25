output "workbook_id" {
  description = "Identifiant du workbook Azure Monitor de supervision technique."
  value       = azurerm_application_insights_workbook.supervision.id
}

output "alert_rule_names" {
  description = "Noms des règles d'alerte créées (journaux et métriques)."
  value = sort(concat(
    [for r in azurerm_monitor_scheduled_query_rules_alert_v2.log : r.name],
    [for r in azurerm_monitor_metric_alert.servicebus : r.name],
    [for r in azurerm_monitor_metric_alert.postgres : r.name],
    [for r in azurerm_monitor_metric_alert.restarts : r.name],
  ))
}
