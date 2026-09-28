output "vnet_id" {
  description = "Identifiant du réseau virtuel."
  value       = azurerm_virtual_network.this.id
}

output "subnet_ids" {
  description = "Identifiants des sous-réseaux (clés : cae-<zone>, pe-<zone>, apim, appgw)."
  value       = { for k, s in azurerm_subnet.this : k => s.id }

  # Les sous-réseaux ne sont utilisables qu'une fois leur NSG associé.
  depends_on = [azurerm_subnet_network_security_group_association.this]
}

output "subnet_prefixes" {
  description = "Plages d'adresses des sous-réseaux (mêmes clés que subnet_ids)."
  value       = { for k, s in local.subnets : k => s.prefix }
}

output "private_dns_zone_ids" {
  description = "Identifiants des zones DNS privées (clés : postgres, servicebus, blob, acr, keyvault, managedhsm)."
  value       = { for k, z in azurerm_private_dns_zone.this : k => z.id }

  depends_on = [azurerm_private_dns_zone_virtual_network_link.this]
}
