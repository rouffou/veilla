# -----------------------------------------------------------------------------
# Module réseau : VNet, sous-réseaux par zone de sensibilité, NSG « refus par
# défaut » et zones DNS privées des points de terminaison privés.
#   CTR-10 : un sous-réseau dédié par environnement Container Apps (zone).
#   CTR-11 : réseau en refus par défaut, données accessibles par points privés.
#   ARC-46 : segmentation réseau par zone de sensibilité.
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.7"
    }
  }
}

locals {
  # Plan d'adressage déduit du /16 :
  #   x.x.0.0/23 à x.x.6.0/23   : environnements Container Apps (un /23 par zone)
  #   x.x.8.0/26 à x.x.8.192/26 : points de terminaison privés (un /26 par zone)
  #   x.x.9.0/24                : API Management (injection VNet interne)
  #   x.x.10.0/24               : Application Gateway WAF_v2
  cae_prefixes = { for i, z in var.zones : z => cidrsubnet(var.address_space, 7, i) }
  pe_prefixes  = { for i, z in var.zones : z => cidrsubnet(var.address_space, 10, 32 + i) }
  apim_prefix  = cidrsubnet(var.address_space, 8, 9)
  appgw_prefix = cidrsubnet(var.address_space, 8, 10)

  subnets = merge(
    { for z in var.zones : "cae-${z}" => { prefix = local.cae_prefixes[z], kind = "cae" } },
    { for z in var.zones : "pe-${z}" => { prefix = local.pe_prefixes[z], kind = "pe" } },
    {
      apim  = { prefix = local.apim_prefix, kind = "apim" }
      appgw = { prefix = local.appgw_prefix, kind = "appgw" }
    },
  )

  # Zones dont les conteneurs n'ont aucun accès Internet sortant (ARC-06 : aucune
  # donnée clinique ou psychosociale ne quitte sa zone ; les systèmes externes
  # sont joints par le service Intégrations de la zone standard).
  no_internet_zones = ["medicale", "psychosociale"]

  # Règle finale commune : tout flux entrant non explicitement autorisé est refusé (CTR-11).
  deny_all_inbound = {
    name         = "RefuserToutEntrant"
    priority     = 4096
    direction    = "Inbound"
    access       = "Deny"
    protocol     = "*"
    sources      = ["*"]
    destinations = ["*"]
    ports        = ["*"]
  }

  # --- Environnements Container Apps --------------------------------------------
  cae_rules = {
    for z in var.zones : "cae-${z}" => concat(
      [
        {
          name         = "AutoriseAzureLoadBalancer"
          priority     = 100
          direction    = "Inbound"
          access       = "Allow"
          protocol     = "*"
          sources      = ["AzureLoadBalancer"]
          destinations = [local.cae_prefixes[z]]
          ports        = ["*"]
        },
        {
          name         = "AutoriseIntraZone"
          priority     = 110
          direction    = "Inbound"
          access       = "Allow"
          protocol     = "*"
          sources      = [local.cae_prefixes[z]]
          destinations = [local.cae_prefixes[z]]
          ports        = ["*"]
        },
        {
          # Plateforme : seule l'API Management joint les BFF. Zones métier : seuls
          # les BFF (environnement plateforme) joignent les services (ARC-43, ARC-46).
          name         = "AutoriseAppelantsHttps"
          priority     = 120
          direction    = "Inbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = z == "platform" ? [local.apim_prefix] : [local.cae_prefixes["platform"]]
          destinations = [local.cae_prefixes[z]]
          ports        = ["80", "443"]
        },
        local.deny_all_inbound,
        {
          name         = "AutoriseAzureMonitor"
          priority     = 100
          direction    = "Outbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = [local.cae_prefixes[z]]
          destinations = ["AzureMonitor"]
          ports        = ["443"]
        },
        {
          name         = "AutoriseMicrosoftContainerRegistry"
          priority     = 110
          direction    = "Outbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = [local.cae_prefixes[z]]
          destinations = ["MicrosoftContainerRegistry"]
          ports        = ["443"]
        },
        {
          name         = "AutoriseFrontDoorFirstParty"
          priority     = 120
          direction    = "Outbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = [local.cae_prefixes[z]]
          destinations = ["AzureFrontDoor.FirstParty"]
          ports        = ["443"]
        },
        {
          name         = "AutoriseEntraId"
          priority     = 130
          direction    = "Outbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = [local.cae_prefixes[z]]
          destinations = ["AzureActiveDirectory"]
          ports        = ["443"]
        },
      ],
      contains(local.no_internet_zones, z) ? [
        {
          name         = "RefuserInternetSortant"
          priority     = 4000
          direction    = "Outbound"
          access       = "Deny"
          protocol     = "*"
          sources      = ["*"]
          destinations = ["Internet"]
          ports        = ["*"]
        },
      ] : [],
    )
  }

  # --- Points de terminaison privés ---------------------------------------------
  # Plateforme (Service Bus, ACR, Key Vault plateforme) : joignable par toutes les zones.
  # Zones de données : joignables uniquement depuis l'environnement de la même zone (ARC-04).
  pe_rules = {
    for z in var.zones : "pe-${z}" => concat(
      [
        {
          name         = "AutoriseConteneursDeLaZone"
          priority     = 100
          direction    = "Inbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = z == "platform" ? values(local.cae_prefixes) : [local.cae_prefixes[z]]
          destinations = [local.pe_prefixes[z]]
          ports        = z == "platform" ? ["443", "5671", "5672"] : ["443", "5432"]
        },
      ],
      z == "platform" ? [
        {
          # Passerelle : lecture des certificats TLS dans le Key Vault plateforme.
          name         = "AutorisePasserelle"
          priority     = 110
          direction    = "Inbound"
          access       = "Allow"
          protocol     = "Tcp"
          sources      = [local.appgw_prefix, local.apim_prefix]
          destinations = [local.pe_prefixes[z]]
          ports        = ["443"]
        },
      ] : [],
      [local.deny_all_inbound],
    )
  }

  # --- API Management (mode VNet interne) -----------------------------------------
  apim_rules = {
    apim = [
      {
        name         = "AutoriseGestionApim"
        priority     = 100
        direction    = "Inbound"
        access       = "Allow"
        protocol     = "Tcp"
        sources      = ["ApiManagement"]
        destinations = ["VirtualNetwork"]
        ports        = ["3443"]
      },
      {
        name         = "AutoriseAzureLoadBalancer"
        priority     = 110
        direction    = "Inbound"
        access       = "Allow"
        protocol     = "Tcp"
        sources      = ["AzureLoadBalancer"]
        destinations = ["VirtualNetwork"]
        ports        = ["6390"]
      },
      {
        name         = "AutoriseApplicationGateway"
        priority     = 120
        direction    = "Inbound"
        access       = "Allow"
        protocol     = "Tcp"
        sources      = [local.appgw_prefix]
        destinations = [local.apim_prefix]
        ports        = ["443"]
      },
      local.deny_all_inbound,
    ]
  }

  # --- Application Gateway WAF_v2 (seul point d'entrée Internet, ARC-42) ---------
  appgw_rules = {
    appgw = [
      {
        name         = "AutoriseGatewayManager"
        priority     = 100
        direction    = "Inbound"
        access       = "Allow"
        protocol     = "Tcp"
        sources      = ["GatewayManager"]
        destinations = ["*"]
        ports        = ["65200-65535"]
      },
      {
        name         = "AutoriseAzureLoadBalancer"
        priority     = 110
        direction    = "Inbound"
        access       = "Allow"
        protocol     = "*"
        sources      = ["AzureLoadBalancer"]
        destinations = ["*"]
        ports        = ["*"]
      },
      {
        name         = "AutoriseHttpsInternet"
        priority     = 120
        direction    = "Inbound"
        access       = "Allow"
        protocol     = "Tcp"
        sources      = ["Internet"]
        destinations = [local.appgw_prefix]
        ports        = ["80", "443"]
      },
      local.deny_all_inbound,
    ]
  }

  nsg_rules = merge(local.cae_rules, local.pe_rules, local.apim_rules, local.appgw_rules)

  nsg_rules_flat = {
    for r in flatten([
      for subnet, rules in local.nsg_rules : [
        for rule in rules : {
          key          = "${subnet}-${rule.name}"
          subnet       = subnet
          name         = rule.name
          priority     = rule.priority
          direction    = rule.direction
          access       = rule.access
          protocol     = rule.protocol
          sources      = tolist(rule.sources)
          destinations = tolist(rule.destinations)
          ports        = tolist(rule.ports)
        }
      ]
    ]) : r.key => r
  }

  # Zones DNS privées des services PaaS joints par points de terminaison privés (CTR-11).
  private_dns_zones = {
    postgres   = "privatelink.postgres.database.azure.com"
    servicebus = "privatelink.servicebus.windows.net"
    blob       = "privatelink.blob.core.windows.net"
    acr        = "privatelink.azurecr.io"
    keyvault   = "privatelink.vaultcore.azure.net"
    managedhsm = "privatelink.managedhsm.azure.net"
  }
}

resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  location            = var.location
  address_space       = [var.address_space]
  tags                = var.tags
}

resource "azurerm_network_security_group" "this" {
  for_each = local.subnets

  name                = "nsg-${var.name_prefix}-${each.key}"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

resource "azurerm_network_security_rule" "this" {
  for_each = local.nsg_rules_flat

  resource_group_name         = var.resource_group_name
  network_security_group_name = azurerm_network_security_group.this[each.value.subnet].name
  name                        = each.value.name
  priority                    = each.value.priority
  direction                   = each.value.direction
  access                      = each.value.access
  protocol                    = each.value.protocol
  source_port_range           = "*"

  source_address_prefix        = length(each.value.sources) == 1 ? each.value.sources[0] : null
  source_address_prefixes      = length(each.value.sources) > 1 ? each.value.sources : null
  destination_address_prefix   = length(each.value.destinations) == 1 ? each.value.destinations[0] : null
  destination_address_prefixes = length(each.value.destinations) > 1 ? each.value.destinations : null
  destination_port_range       = length(each.value.ports) == 1 ? each.value.ports[0] : null
  destination_port_ranges      = length(each.value.ports) > 1 ? each.value.ports : null
}

resource "azurerm_subnet" "this" {
  for_each = local.subnets

  name                 = "snet-${each.key}"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [each.value.prefix]

  # Les NSG s'appliquent aussi aux points de terminaison privés (CTR-11).
  private_endpoint_network_policies = each.value.kind == "pe" ? "Enabled" : "Disabled"

  # Environnements Container Apps avec profils de charge : délégation obligatoire.
  dynamic "delegation" {
    for_each = each.value.kind == "cae" ? [1] : []
    content {
      name = "containerapps"
      service_delegation {
        name    = "Microsoft.App/environments"
        actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
      }
    }
  }
}

resource "azurerm_subnet_network_security_group_association" "this" {
  for_each = local.subnets

  subnet_id                 = azurerm_subnet.this[each.key].id
  network_security_group_id = azurerm_network_security_group.this[each.key].id

  # Les règles doivent exister avant l'association (API Management et
  # Application Gateway valident les règles requises à la création).
  depends_on = [azurerm_network_security_rule.this]
}

resource "azurerm_private_dns_zone" "this" {
  for_each = local.private_dns_zones

  name                = each.value
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "this" {
  for_each = local.private_dns_zones

  name                  = "link-${var.name_prefix}-${each.key}"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.this[each.key].name
  virtual_network_id    = azurerm_virtual_network.this.id
  registration_enabled  = false
  tags                  = var.tags
}
