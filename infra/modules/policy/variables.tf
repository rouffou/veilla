variable "name_prefix" {
  description = "Préfixe de nommage (ex. sepp-dev), rend la définition personnalisée unique par environnement."
  type        = string
}

variable "environment" {
  description = "Nom de l'environnement (affichage)."
  type        = string
}

variable "resource_group_ids" {
  description = "Groupes de ressources sur lesquels assigner les stratégies (clé = zone)."
  type        = map(string)
}

variable "allowed_registries" {
  description = "Noms d'hôte des registres d'images autorisés (CTR-07)."
  type        = list(string)
}

variable "allowed_locations" {
  description = "Régions Azure autorisées (Union européenne, NF-13)."
  type        = list(string)
}

variable "effect" {
  description = "Effet des stratégies : Deny, Audit ou Disabled."
  type        = string
  default     = "Deny"
}
