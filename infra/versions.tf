# Contraintes de versions du module racine SEPP (CTR-21 : infrastructure déclarative).
# Les versions sont épinglées ; toute montée de version passe par une revue (ARC-51).
terraform {
  required_version = ">= 1.9.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.13"
    }
  }
}
