# -----------------------------------------------------------------------------
# Module passerelle : Application Gateway WAF_v2 + API Management interne.
#   ARC-42 : terminaison TLS, pare-feu applicatif, limitation de débit, routage
#            vers les BFF.
#   ARC-43 : un BFF par canal, seuls points d'entrée des fronts.
#   CTR-11 : l'API Management est injectée en mode VNet « Internal » ; seule
#            l'Application Gateway expose une IP publique.
# Chaîne : Internet -> Application Gateway (WAF) -> API Management -> BFF
# (environnement Container Apps plateforme, interne).
# -----------------------------------------------------------------------------
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

locals {
  apim_gateway_host = "${var.apim_name}.azure-api.net"
  tls_enabled       = var.ssl_certificate_secret_id != null

  appgw_names = {
    gateway_ip  = "gwip"
    frontend_ip = "feip-public"
    port        = local.tls_enabled ? "port-443" : "port-80"
    pool        = "pool-apim"
    settings    = "settings-apim"
    probe       = "probe-apim"
    listener    = "listener-public"
    certificate = "cert-public"
    rule        = "rule-apim"
  }
}

# --- API Management (VNet interne) ---------------------------------------------------
# IP publique de gestion, exigée pour une API Management redondante en zone
# injectée dans un VNet (plateforme stv2). Le trafic applicatif reste privé.
resource "azurerm_public_ip" "apim" {
  count = length(var.zones) > 0 ? 1 : 0

  name                = "pip-${var.apim_name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  allocation_method   = "Static"
  sku                 = "Standard"
  zones               = var.zones
  domain_name_label   = var.apim_name
  tags                = var.tags
}

resource "azurerm_api_management" "this" {
  name                 = var.apim_name
  resource_group_name  = var.resource_group_name
  location             = var.location
  publisher_name       = var.publisher_name
  publisher_email      = var.publisher_email
  sku_name             = var.sku_name
  zones                = length(var.zones) > 0 ? var.zones : null
  public_ip_address_id = one(azurerm_public_ip.apim[*].id)
  virtual_network_type = "Internal"
  tags                 = var.tags

  identity {
    type = "SystemAssigned"
  }

  virtual_network_configuration {
    subnet_id = var.apim_subnet_id
  }
}

resource "azurerm_monitor_diagnostic_setting" "apim" {
  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_api_management.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }
}

# Backends : un par BFF (ARC-43). Les API (contrats OpenAPI) sont publiées par la
# chaîne CI/CD de chaque BFF.
resource "azurerm_api_management_backend" "bff" {
  for_each = var.bff_backends

  name                = each.key
  resource_group_name = var.resource_group_name
  api_management_name = azurerm_api_management.this.name
  protocol            = "http"
  url                 = "https://${each.value}"
  description         = "BFF ${each.key} (Container Apps, zone plateforme)"
}

# Résolution privée des points de terminaison de l'API Management interne.
resource "azurerm_private_dns_zone" "apim" {
  name                = "azure-api.net"
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "apim" {
  name                  = "link-${var.apim_name}"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.apim.name
  virtual_network_id    = var.virtual_network_id
  registration_enabled  = false
  tags                  = var.tags
}

resource "azurerm_private_dns_a_record" "apim" {
  for_each = toset([
    var.apim_name,
    "${var.apim_name}.portal",
    "${var.apim_name}.developer",
    "${var.apim_name}.management",
    "${var.apim_name}.scm",
  ])

  name                = each.value
  zone_name           = azurerm_private_dns_zone.apim.name
  resource_group_name = var.resource_group_name
  ttl                 = 300
  records             = azurerm_api_management.this.private_ip_addresses
  tags                = var.tags
}

# --- Application Gateway WAF_v2 -------------------------------------------------------
resource "azurerm_public_ip" "appgw" {
  name                = "pip-${var.appgw_name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  allocation_method   = "Static"
  sku                 = "Standard"
  zones               = length(var.zones) > 0 ? var.zones : null
  tags                = var.tags
}

# Identité de l'Application Gateway pour lire le certificat TLS dans Key Vault (CTR-16).
resource "azurerm_user_assigned_identity" "appgw" {
  name                = "id-${var.appgw_name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

resource "azurerm_role_assignment" "appgw_key_vault" {
  scope                = var.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.appgw.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_web_application_firewall_policy" "this" {
  name                = "waf-${var.appgw_name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags

  policy_settings {
    enabled                     = true
    mode                        = var.waf_mode
    request_body_check          = true
    max_request_body_size_in_kb = 128
    file_upload_limit_in_mb     = 100
  }

  # ARC-42 : limitation de débit par adresse IP cliente. La condition (IP ne
  # correspondant pas à 255.255.255.255/32) sélectionne tout le trafic.
  custom_rules {
    name                 = "LimiteDebitParIp"
    priority             = 10
    rule_type            = "RateLimitRule"
    action               = "Block"
    rate_limit_duration  = "OneMin"
    rate_limit_threshold = var.rate_limit_per_minute
    group_rate_limit_by  = "ClientAddr"

    match_conditions {
      operator           = "IPMatch"
      negation_condition = true
      match_values       = ["255.255.255.255/32"]

      match_variables {
        variable_name = "RemoteAddr"
      }
    }
  }

  managed_rules {
    managed_rule_set {
      type    = "Microsoft_DefaultRuleSet"
      version = "2.1"
    }

    managed_rule_set {
      type    = "Microsoft_BotManagerRuleSet"
      version = "1.1"
    }
  }
}

resource "azurerm_application_gateway" "this" {
  name                = var.appgw_name
  resource_group_name = var.resource_group_name
  location            = var.location
  zones               = length(var.zones) > 0 ? var.zones : null
  firewall_policy_id  = azurerm_web_application_firewall_policy.this.id
  http2_enabled       = true
  tags                = var.tags

  sku {
    name = "WAF_v2"
    tier = "WAF_v2"
  }

  autoscale_configuration {
    min_capacity = var.appgw_min_capacity
    max_capacity = var.appgw_max_capacity
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.appgw.id]
  }

  ssl_policy {
    policy_type = "Predefined"
    policy_name = "AppGwSslPolicy20220101S"
  }

  gateway_ip_configuration {
    name      = local.appgw_names.gateway_ip
    subnet_id = var.appgw_subnet_id
  }

  frontend_ip_configuration {
    name                 = local.appgw_names.frontend_ip
    public_ip_address_id = azurerm_public_ip.appgw.id
  }

  frontend_port {
    name = local.appgw_names.port
    port = local.tls_enabled ? 443 : 80
  }

  dynamic "ssl_certificate" {
    for_each = local.tls_enabled ? [var.ssl_certificate_secret_id] : []
    content {
      name                = local.appgw_names.certificate
      key_vault_secret_id = ssl_certificate.value
    }
  }

  http_listener {
    name                           = local.appgw_names.listener
    frontend_ip_configuration_name = local.appgw_names.frontend_ip
    frontend_port_name             = local.appgw_names.port
    protocol                       = local.tls_enabled ? "Https" : "Http"
    ssl_certificate_name           = local.tls_enabled ? local.appgw_names.certificate : null
  }

  backend_address_pool {
    name         = local.appgw_names.pool
    ip_addresses = azurerm_api_management.this.private_ip_addresses
  }

  # Chiffrement de bout en bout : l'Application Gateway rejoint l'API Management en HTTPS.
  backend_http_settings {
    name                  = local.appgw_names.settings
    protocol              = "Https"
    port                  = 443
    cookie_based_affinity = "Disabled"
    request_timeout       = 60
    host_name             = local.apim_gateway_host
    probe_name            = local.appgw_names.probe
  }

  probe {
    name                = local.appgw_names.probe
    protocol            = "Https"
    host                = local.apim_gateway_host
    path                = "/status-0123456789abcdef"
    port                = 443
    interval            = 30
    timeout             = 30
    unhealthy_threshold = 3

    match {
      status_code = ["200-399"]
    }
  }

  request_routing_rule {
    name                       = local.appgw_names.rule
    priority                   = 100
    rule_type                  = "Basic"
    http_listener_name         = local.appgw_names.listener
    backend_address_pool_name  = local.appgw_names.pool
    backend_http_settings_name = local.appgw_names.settings
  }

  depends_on = [azurerm_role_assignment.appgw_key_vault, azurerm_private_dns_a_record.apim]
}

resource "azurerm_monitor_diagnostic_setting" "appgw" {
  name                       = "diag-log-analytics"
  target_resource_id         = azurerm_application_gateway.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }
}
