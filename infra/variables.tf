# -----------------------------------------------------------------------------
# Variables du module racine SEPP.
# Chaque environnement (CTR-22) fournit ses valeurs via environments/<env>/terraform.tfvars.
# Aucune variable ne contient de secret (CTR-16) : seuls des identifiants d'objets
# Entra ID et des références Key Vault sont attendus.
# -----------------------------------------------------------------------------

variable "environment" {
  description = "Nom de l'environnement (CTR-22) : dev, test, recette, preprod ou prod."
  type        = string

  validation {
    condition     = contains(["dev", "test", "recette", "preprod", "prod"], var.environment)
    error_message = "L'environnement doit être dev, test, recette, preprod ou prod."
  }
}

variable "location" {
  description = "Région Azure principale (ARC-52 : Belgium Central, trois zones de disponibilité)."
  type        = string
  default     = "belgiumcentral"
}

variable "dr_location" {
  description = "Région UE secondaire pour la reprise après sinistre (ARC-52, Belgium Central n'étant pas appairée)."
  type        = string
  default     = "swedencentral"

  validation {
    condition = contains([
      "swedencentral", "francecentral", "germanywestcentral", "westeurope", "northeurope",
      "italynorth", "polandcentral", "spaincentral", "austriaeast", "denmarkeast",
    ], var.dr_location)
    error_message = "La région de reprise doit être une région de l'Union européenne (NF-13)."
  }
}

variable "enable_dr_replication" {
  description = "Active la réplication vers la région de reprise (géo-réplication ACR) — ARC-52."
  type        = bool
  default     = false
}

variable "zone_redundancy_enabled" {
  description = "Active la redondance de zone lorsque la région et le niveau de service le permettent (CTR-14, ARC-53)."
  type        = bool
  default     = false
}

variable "address_space" {
  description = "Plage d'adresses du VNet (un /16 par environnement, découpé automatiquement en sous-réseaux)."
  type        = string

  validation {
    condition     = can(cidrhost(var.address_space, 0)) && endswith(var.address_space, "/16")
    error_message = "address_space doit être un bloc CIDR /16 (ex. 10.10.0.0/16)."
  }
}

variable "tags" {
  description = "Étiquettes additionnelles appliquées à toutes les ressources."
  type        = map(string)
  default     = {}
}

# -----------------------------------------------------------------------------
# Microservices (§14.3) et exécution (CTR-10 à CTR-14)
# -----------------------------------------------------------------------------

# Catalogue des microservices conteneurisés (§14.3). Clé = nom du service, qui est aussi
# le nom de son topic Service Bus s'il publie des événements. Attributs :
# - zone : platform | standard | medicale | psychosociale (CTR-10) ;
# - cpu / memory : ressources du conteneur (CTR-13) ;
# - min_replicas / max_replicas : bornes de mise à l'échelle (minimum forcé à 2 en prod, CTR-14) ;
# - database : base PostgreSQL dédiée sur le serveur de la zone (ARC-02) ;
# - publishes_events : crée le topic Service Bus du service (ARC-05) ;
# - subscribes_to : topics auxquels le service s'abonne (une subscription par service abonné). Doit être aligné sur les
#   AddIntegrationEventHandler du service : les tests d'architecture (Sepp.<Service>.Architecture.Tests) vérifient que les
#   topics souscrits par le code sont inclus dans cette liste. Un abonnement sans gestionnaire n'est conservé que si le
#   service producteur ou consommateur n'est pas encore écrit (commentaire en regard) ;
# - subject_filters : topic -> sujets (nom versionné du contrat, ex. audit.bris-de-glace-signale.v1) acceptés par la
#   subscription ; crée une règle SQL `sys.Label IN (...)` (ADR 0004 : le sujet du message est le nom versionné du
#   contrat). Absent = tous les messages du topic. Le filtre remplace la règle `$Default` créée par Azure
#   (azapi, voir infra/README.md) ;
# - http_concurrency : seuil de la règle KEDA HTTP ; queue_scaling / message_count : règles KEDA Service Bus (CTR-13) ;
# - image : image complète (sinon <acr>/sepp/<service>:<image_tag>) ;
# - key_vault_secrets : variables alimentées par des secrets du Key Vault de la zone, nom -> nom du secret (CTR-16).
variable "services" {
  description = "Catalogue des microservices (zone, ressources, mise à l'échelle, base de données, événements). Voir le commentaire ci-dessus."
  type = map(object({
    zone              = string
    cpu               = optional(number, 0.5)
    memory            = optional(string, "1Gi")
    min_replicas      = optional(number)
    max_replicas      = optional(number, 10)
    database          = optional(bool, false)
    publishes_events  = optional(bool, true)
    subscribes_to     = optional(list(string), [])
    subject_filters   = optional(map(list(string)), {})
    http_concurrency  = optional(number, 50)
    queue_scaling     = optional(bool, true)
    message_count     = optional(number, 20)
    image             = optional(string)
    key_vault_secrets = optional(map(string), {})
  }))
  default = {
    # --- Zone plateforme : BFF (ARC-43) et identité (ARC-40) ---
    "bff-interne"     = { zone = "platform", publishes_events = false }
    "bff-employeur"   = { zone = "platform" }
    "bff-travailleur" = { zone = "platform" }
    "identite"        = { zone = "platform", database = true }

    # --- Zone standard / transverse ---
    # Les abonnements suivent les gestionnaires d'événements réellement enregistrés dans chaque service (saga de
    # reprise, ARC-33). Personnes et referentiels n'ont pas encore de consommateur : leurs abonnements sont
    # ceux prévus pour leur écriture.
    # affilies : seul integrations.donnees-bce-recues.v1 est consommé (DonneesBceRecuesHandler, AFF-01, AFF-02, INT-04).
    "affilies" = {
      zone            = "standard"
      database        = true
      subscribes_to   = ["integrations"]
      subject_filters = { integrations = ["integrations.donnees-bce-recues.v1"] }
    }
    "personnes" = { zone = "standard", database = true, subscribes_to = ["integrations", "affilies"] }
    "postes-risques" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["referentiels", "personnes", "surveillance-medicale"]
      # personnes.etat-particulier-declare.v1 est réservé à Obligations (catalogue des événements).
      subject_filters = { personnes = ["personnes.affectation-modifiee.v1"] }
    }
    "obligations" = {
      zone     = "standard"
      database = true
      subscribes_to = [
        "personnes", "postes-risques", "planification", "surveillance-medicale", "referentiels", "documents",
        "integrations",  # incapacite-notifiee
        "bff-employeur", # reprise-annoncee (rétrocompatibilité : la saga démarre par l'API /reprises)
        "reintegration", # trajet-demarre, trajet-termine : service pas encore écrit
      ]
    }
    "planification" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["obligations", "referentiels", "communications"]
    }
    "prevention"  = { zone = "standard", database = true, subscribes_to = ["affilies", "postes-risques", "bff-employeur"] }
    "prestations" = { zone = "standard", database = true, subscribes_to = ["planification", "prevention", "surveillance-medicale", "reintegration", "psychosocial"] }
    "documents" = {
      zone     = "standard"
      database = true
      # surveillance-medicale : seul abonnement avec gestionnaire (DecisionEmise) ; les autres producteurs
      # (reintegration, psychosocial, prevention) ne sont pas encore écrits.
      subscribes_to = ["surveillance-medicale", "reintegration", "psychosocial", "prevention"]
    }
    "communications" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["planification", "documents", "audit"]
      # Seul le bris de glace intéresse Communications sur le topic partagé « audit » (NF-04).
      subject_filters = { audit = ["audit.bris-de-glace-signale.v1"] }
    }
    "integrations" = {
      zone     = "standard"
      database = true
      # affilies : seul abonnement avec gestionnaire (AffilieCree, AffilieModifie) ; prestations n'est pas encore écrit.
      subscribes_to = ["affilies", "prestations"]
    }
    "referentiels" = { zone = "standard", database = true }
    "reporting" = {
      zone             = "standard"
      database         = true
      publishes_events = false
      subscribes_to    = ["affilies", "personnes", "obligations", "planification", "prestations", "prevention", "surveillance-medicale", "psychosocial", "reintegration"]
      # Service pas encore écrit : liste prévue, à confirmer avec ses gestionnaires (état particulier réservé à Obligations).
      subject_filters = { personnes = ["personnes.affectation-modifiee.v1", "personnes.occupation-debutee.v1", "personnes.occupation-terminee.v1"] }
    }
    # Topic partagé « audit » : tous les services y publient leurs traces d'accès (NF-04, voir docs/architecture/evenements.md).
    "audit" = {
      zone             = "standard"
      database         = true
      publishes_events = true
      subscribes_to    = ["audit"]
    }

    # --- Zone médicale (ARC-04) ---
    "surveillance-medicale" = {
      zone          = "medicale"
      database      = true
      cpu           = 1
      memory        = "2Gi"
      subscribes_to = ["planification", "personnes", "postes-risques", "obligations", "referentiels", "prevention"]
      # personnes.etat-particulier-declare.v1 est réservé à Obligations (catalogue des événements).
      subject_filters = { personnes = ["personnes.affectation-modifiee.v1"] }
    }
    "reintegration" = {
      zone          = "medicale"
      database      = true
      subscribes_to = ["personnes", "surveillance-medicale", "bff-employeur"]
      # Service pas encore écrit : liste prévue, à confirmer avec ses gestionnaires (état particulier réservé à Obligations).
      subject_filters = { personnes = ["personnes.affectation-modifiee.v1", "personnes.occupation-debutee.v1", "personnes.occupation-terminee.v1"] }
    }

    # --- Zone psychosociale (ARC-04) ---
    "psychosocial" = {
      zone          = "psychosociale"
      database      = true
      subscribes_to = ["personnes", "affilies", "bff-travailleur"]
      # Service pas encore écrit : liste prévue, à confirmer avec ses gestionnaires (état particulier réservé à Obligations).
      subject_filters = { personnes = ["personnes.affectation-modifiee.v1", "personnes.occupation-debutee.v1", "personnes.occupation-terminee.v1"] }
    }
  }

  validation {
    condition     = alltrue([for s in values(var.services) : contains(["platform", "standard", "medicale", "psychosociale"], s.zone)])
    error_message = "La zone d'un service doit être platform, standard, medicale ou psychosociale (CTR-10)."
  }

  validation {
    condition = alltrue(flatten([
      for s in values(var.services) : [for t in s.subscribes_to : try(var.services[t].publishes_events, false)]
    ]))
    error_message = "Chaque entrée de subscribes_to doit désigner un service existant qui publie des événements."
  }

  validation {
    condition = alltrue(flatten([
      for s in values(var.services) : [for t in keys(s.subject_filters) : contains(s.subscribes_to, t)]
    ]))
    error_message = "Chaque clé de subject_filters doit être un topic présent dans subscribes_to du même service."
  }

  validation {
    condition = alltrue(flatten([
      for s in values(var.services) : [
        for sujets in values(s.subject_filters) : length(sujets) > 0 && alltrue([for x in sujets : can(regex("^[a-z0-9-]+\\.[a-z0-9-]+\\.v[0-9]+$", x))])
      ]
    ]))
    error_message = "Les sujets d'un filtre doivent être des noms de contrat versionnés (ex. audit.bris-de-glace-signale.v1)."
  }

  # L'état particulier (personnes.etat-particulier-declare.v1) est réservé à Obligations : tout autre abonné au topic
  # personnes doit déclarer ses sujets, et la liste ne peut pas le contenir.
  validation {
    condition = alltrue([
      for n, s in var.services : n == "obligations" || !contains(s.subscribes_to, "personnes") || (
        contains(keys(s.subject_filters), "personnes") && !contains(lookup(s.subject_filters, "personnes", []), "personnes.etat-particulier-declare.v1")
      )
    ])
    error_message = "Seul obligations peut recevoir personnes.etat-particulier-declare.v1 : les autres abonnés au topic personnes doivent déclarer subject_filters.personnes sans ce sujet."
  }

  validation {
    condition     = alltrue([for n in keys(var.services) : can(regex("^[a-z][a-z0-9-]{0,27}[a-z0-9]$", n))])
    error_message = "Les noms de services doivent être en minuscules (a-z, 0-9, -) et compter au plus 29 caractères (préfixe ca- inclus : 32)."
  }
}

variable "default_min_replicas" {
  description = "Nombre minimal d'instances par défaut (porté à 2 au minimum en production, CTR-14)."
  type        = number
  default     = 1
}

variable "deploy_container_apps" {
  description = "Déploie les Container Apps. Mettre à false lors du premier déploiement, avant la publication des images dans l'ACR."
  type        = bool
  default     = true
}

variable "image_tag" {
  description = "Étiquette immuable (version sémantique) des images à déployer initialement (CTR-07). Les déploiements suivants sont faits par la CI/CD (ARC-50)."
  type        = string
  default     = "0.1.0"
}

variable "dedicated_workload_profile" {
  description = "Profil de charge dédié ajouté à chaque environnement Container Apps (null = profil Consumption uniquement)."
  type = object({
    workload_profile_type = string
    minimum_count         = number
    maximum_count         = number
  })
  default = null
}

# -----------------------------------------------------------------------------
# Données : PostgreSQL, Service Bus, stockage documentaire
# -----------------------------------------------------------------------------

variable "postgres_sku_name" {
  description = "SKU des serveurs PostgreSQL Flexible (ex. B_Standard_B1ms en dev, GP_Standard_D4ds_v5 en prod)."
  type        = string
  default     = "GP_Standard_D2ds_v5"
}

variable "postgres_version" {
  description = "Version majeure de PostgreSQL."
  type        = string
  default     = "16"
}

variable "postgres_storage_mb" {
  description = "Taille de stockage initiale de chaque serveur PostgreSQL (Mo)."
  type        = number
  default     = 32768
}

variable "postgres_backup_retention_days" {
  description = "Rétention des sauvegardes PostgreSQL en jours (NF-33 : restauration à un instant donné, RPO 1 h)."
  type        = number
  default     = 7
}

variable "postgres_high_availability_enabled" {
  description = "Active la haute disponibilité PostgreSQL redondante en zone (non disponible sur les SKU Burstable)."
  type        = bool
  default     = false
}

variable "postgres_geo_redundant_backup_enabled" {
  description = "Active la sauvegarde géo-redondante PostgreSQL (disponibilité à vérifier pour Belgium Central, ARC-53)."
  type        = bool
  default     = false
}

variable "postgres_entra_admin" {
  description = "Groupe Entra ID administrateur des serveurs PostgreSQL (authentification Entra uniquement, aucun mot de passe — CTR-16)."
  type = object({
    object_id      = string
    principal_name = string
    principal_type = optional(string, "Group")
  })
}

variable "servicebus_capacity" {
  description = "Unités de messagerie du namespace Service Bus Premium (1, 2, 4, 8 ou 16)."
  type        = number
  default     = 1
}

variable "storage_replication_type" {
  description = "Réplication du compte de stockage documentaire (ZRS par défaut ; GZRS si disponible pour la région, ARC-53)."
  type        = string
  default     = "ZRS"
}

variable "document_retention_days" {
  description = "Durée de rétention WORM (jours) des conteneurs documentaires (NF-21). Doit être alignée sur les durées légales (NF-20)."
  type        = number
  default     = 365
}

variable "document_immutability_locked" {
  description = "Verrouille la stratégie d'immutabilité WORM (irréversible ! à réserver à la production)."
  type        = bool
  default     = false
}

# -----------------------------------------------------------------------------
# Secrets et clés (ARC-44)
# -----------------------------------------------------------------------------

variable "managed_hsm_enabled" {
  description = "Crée un Managed HSM dédié pour les zones médicale et psychosociale (ARC-44). Sinon, Key Vault Premium (clés HSM)."
  type        = bool
  default     = false
}

variable "managed_hsm_admin_object_ids" {
  description = "Object IDs Entra ID des administrateurs initiaux des Managed HSM (requis si managed_hsm_enabled)."
  type        = list(string)
  default     = []

  validation {
    condition     = !var.managed_hsm_enabled || length(var.managed_hsm_admin_object_ids) > 0
    error_message = "Au moins un administrateur est requis lorsque managed_hsm_enabled vaut true."
  }
}

# -----------------------------------------------------------------------------
# Observabilité (ARC-47, NF-62)
# -----------------------------------------------------------------------------

variable "log_retention_days" {
  description = "Rétention des journaux dans Log Analytics (jours)."
  type        = number
  default     = 90
}

variable "alert_email" {
  description = "Adresse de la liste de diffusion d'exploitation destinataire des alertes (null = pas de groupe d'actions)."
  type        = string
  default     = null
}

# -----------------------------------------------------------------------------
# Passerelle : API Management + Application Gateway WAF (ARC-42)
# -----------------------------------------------------------------------------

variable "apim_sku_name" {
  description = "SKU API Management compatible injection VNet interne (Developer_1 hors production, Premium_N en production)."
  type        = string
  default     = "Developer_1"
}

variable "apim_zones" {
  description = "Zones de disponibilité de l'API Management (Premium uniquement ; le nombre d'unités doit être un multiple du nombre de zones)."
  type        = list(string)
  default     = []
}

variable "apim_publisher_name" {
  description = "Nom de l'éditeur affiché par API Management."
  type        = string
  default     = "SEPP"
}

variable "apim_publisher_email" {
  description = "Adresse de contact de l'éditeur API Management."
  type        = string
}

variable "appgw_min_capacity" {
  description = "Capacité minimale (instances) de l'Application Gateway WAF_v2."
  type        = number
  default     = 1
}

variable "appgw_max_capacity" {
  description = "Capacité maximale (instances) de l'Application Gateway WAF_v2."
  type        = number
  default     = 3
}

variable "appgw_ssl_certificate_secret_id" {
  description = "Identifiant (sans version) du certificat TLS stocké dans le Key Vault plateforme. null = écouteur HTTP d'amorçage uniquement (hors production)."
  type        = string
  default     = null
}

variable "waf_mode" {
  description = "Mode du pare-feu applicatif : Detection ou Prevention."
  type        = string
  default     = "Prevention"
}

variable "waf_rate_limit_per_minute" {
  description = "Limitation de débit par adresse IP cliente et par minute (ARC-42)."
  type        = number
  default     = 1000
}

# -----------------------------------------------------------------------------
# Gouvernance : Azure Policy (CTR-17, NF-13)
# -----------------------------------------------------------------------------

variable "policy_effect" {
  description = "Effet des stratégies Azure Policy : Deny (recommandé), Audit ou Disabled."
  type        = string
  default     = "Deny"

  validation {
    condition     = contains(["Deny", "Audit", "Disabled"], var.policy_effect)
    error_message = "policy_effect doit valoir Deny, Audit ou Disabled."
  }
}

variable "additional_allowed_registries" {
  description = "Registres supplémentaires autorisés pour les images des Container Apps (ex. mcr.microsoft.com en dev pour l'amorçage)."
  type        = list(string)
  default     = []
}
