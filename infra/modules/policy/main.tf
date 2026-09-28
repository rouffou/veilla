# -----------------------------------------------------------------------------
# Module gouvernance : Azure Policy sur les groupes de ressources SEPP.
#   CTR-17 : Azure Policy impose CTR-07 (registre privé autorisé) et CTR-11
#            (aucun accès réseau public aux ressources de données).
#   NF-13  : hébergement dans l'Union européenne (localisations autorisées).
# Les définitions intégrées (built-in) sont référencées par leur identifiant ;
# une définition personnalisée limite les registres des images des Container Apps.
# CTR-04 (non root) et CTR-06 (signature) ne sont pas vérifiables par Azure
# Policy sur Container Apps : ils sont contrôlés dans la chaîne CI/CD.
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
  builtin = "/providers/Microsoft.Authorization/policyDefinitions"

  # Définitions intégrées « accès réseau public désactivé » (CTR-11), effet paramétrable.
  public_network_policies = {
    acr-public-network     = "${local.builtin}/0fdf0491-d080-4575-b627-ad0e843cba0f"
    kv-public-network      = "${local.builtin}/405c5871-3e91-4644-8a63-58e19d68ff5b"
    psql-public-network    = "${local.builtin}/5e1de0e3-42cb-4ebc-a86d-61d0c619ca48"
    sb-public-network      = "${local.builtin}/cbd11fd3-3002-4907-b6c8-579f0e700e13"
    storage-public-network = "${local.builtin}/b2982f36-99f2-4db5-8eff-283140c09693"
    cae-public-network     = "${local.builtin}/d074ddf8-01a5-4b5e-a2b8-964aed452c0a"
  }

  allowed_locations_policy = "${local.builtin}/e56962a6-4747-49cd-b67b-bf8b01975c4c"

  # Produit cartésien groupe de ressources x stratégie.
  public_network_assignments = {
    for pair in setproduct(keys(var.resource_group_ids), keys(local.public_network_policies)) :
    "${pair[0]}-${pair[1]}" => {
      resource_group = pair[0]
      policy         = pair[1]
    }
  }
}

# --- CTR-07 / CTR-17 : registres autorisés pour les images des Container Apps -----
resource "azurerm_policy_definition" "allowed_registries" {
  name         = "${var.name_prefix}-containerapps-registres-autorises"
  display_name = "SEPP ${var.environment} - Container Apps : images issues des registres autorisés uniquement"
  description  = "Refuse toute Container App dont une image ne provient pas d'un registre autorisé (CTR-07, CTR-17)."
  policy_type  = "Custom"
  mode         = "Indexed"

  metadata = jsonencode({
    category = "Container Apps"
    version  = "1.0.0"
  })

  parameters = jsonencode({
    allowedRegistries = {
      type = "Array"
      metadata = {
        displayName = "Registres autorisés"
        description = "Noms d'hôte des registres autorisés (ex. monacr.azurecr.io)."
      }
    }
    effect = {
      type          = "String"
      allowedValues = ["Audit", "Deny", "Disabled"]
      defaultValue  = "Deny"
      metadata = {
        displayName = "Effet"
      }
    }
  })

  policy_rule = jsonencode({
    "if" = {
      allOf = [
        {
          field  = "type"
          equals = "Microsoft.App/containerApps"
        },
        {
          count = {
            field = "Microsoft.App/containerApps/template.containers[*]"
            where = {
              value = "[first(split(current('Microsoft.App/containerApps/template.containers[*].image'), '/'))]"
              notIn = "[parameters('allowedRegistries')]"
            }
          }
          greater = 0
        },
      ]
    }
    "then" = {
      effect = "[parameters('effect')]"
    }
  })

  lifecycle {
    # Azure enrichit les métadonnées (createdBy, createdOn...) : pas de dérive à signaler.
    ignore_changes = [metadata]
  }
}

resource "azurerm_resource_group_policy_assignment" "allowed_registries" {
  for_each = var.resource_group_ids

  name                 = "sepp-registres-autorises"
  display_name         = "SEPP - registres d'images autorisés (CTR-07)"
  resource_group_id    = each.value
  policy_definition_id = azurerm_policy_definition.allowed_registries.id

  parameters = jsonencode({
    allowedRegistries = { value = var.allowed_registries }
    effect            = { value = var.effect }
  })
}

# --- CTR-11 / CTR-17 : aucun accès réseau public aux ressources de données ---------
resource "azurerm_resource_group_policy_assignment" "public_network" {
  for_each = local.public_network_assignments

  name                 = "sepp-${each.value.policy}"
  display_name         = "SEPP - ${each.value.policy} désactivé (CTR-11)"
  resource_group_id    = var.resource_group_ids[each.value.resource_group]
  policy_definition_id = local.public_network_policies[each.value.policy]

  parameters = jsonencode({
    effect = { value = var.effect }
  })
}

# --- NF-13 : localisations autorisées (Union européenne) ----------------------------
resource "azurerm_resource_group_policy_assignment" "allowed_locations" {
  for_each = var.resource_group_ids

  name                 = "sepp-localisations-ue"
  display_name         = "SEPP - localisations UE autorisées (NF-13)"
  resource_group_id    = each.value
  policy_definition_id = local.allowed_locations_policy

  parameters = jsonencode({
    listOfAllowedLocations = { value = var.allowed_locations }
  })
}
