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
# - image : image complète (sinon <acr>/sepp/<instance_de ou service>:<image_tag>) ;
# - instance_de : nom du service dont ce service est une instance supplémentaire (même code, même image), déployée dans
#   sa propre zone avec sa base, son stockage et sa clé racine (décision #356, ADR 0009). Détermine le dépôt d'image, le
#   topic de publication partagé (le topic du service d'origine, par un émetteur partagé, sans topic propre : mettre
#   publishes_events = false) et l'appsettings correspondant. Exemple : documents-medicale est une instance de documents ;
# - key_vault_secrets : variables alimentées par des secrets du Key Vault de la zone, nom -> nom du secret (CTR-16).
#   Le nom de la variable d'environnement est le nom de l'application en majuscules, « - » devenant « _ »
#   (ex. blindindex--cleenveloppee -> BLINDINDEX__CLEENVELOPPEE) ;
# - compte_technique : préfixe de configuration du compte technique OIDC du service (client credentials), par ex.
#   Affilies__CompteTechnique. Terraform injecte <préfixe>__TokenEndpoint, __ClientId (var.technical_account_client_ids)
#   et __Scope, et référence le secret <préfixe>__ClientSecret du Key Vault de la zone (secret à créer hors dépôt) ;
# - internal_services : clé de configuration -> service appelé ; la valeur injectée est l'adresse interne du service ;
# - encryption : préfixe de configuration d'un jeu de clés de chiffrement des champs (ARC-44, ARC-45, ADR 0012) -> zone
#   du Key Vault qui porte les clés. Terraform crée la clé racine non exportable <service>-fields-kek dans le coffre de la
#   zone (rôle d'enveloppement porté sur la clé), injecte <préfixe>__Enveloppe__Type (KeyVault), <préfixe>__Enveloppe__CleKek
#   (URI sans version) et <préfixe>__CurrentKeyId, et référence un secret <préfixe>__Deks__<id> (DEK enveloppée) par
#   identifiant de var.encryption_key_ids, plus <préfixe>__Keys__<id> (anciennes clés AES du format v1, lecture seule) par
#   identifiant de var.encryption_legacy_key_ids (secrets à créer hors dépôt). Un service ne référence que le coffre de sa
#   propre zone (ARC-04) : la validation refuse toute autre zone. Les clés racines de Documents (documents-kek) ne passent
#   pas par cet attribut ;
# - adapters : clés de configuration <Section>__Adaptateurs__<Port> du choix Reel / Simulateur ; la valeur injectée est
#   var.adapter_mode (Simulateur partout hors production, ARC-05, NF-14).
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
    instance_de       = optional(string)
    key_vault_secrets = optional(map(string), {})
    compte_technique  = optional(string)
    internal_services = optional(map(string), {})
    encryption        = optional(map(string), {})
    adapters          = optional(list(string), [])
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
    # affilies : integrations.donnees-bce-recues.v1 (DonneesBceRecuesHandler, AFF-01, AFF-02, INT-04) et les occupations de
    # Personnes (OccupationDebuteeHandler, OccupationTermineeHandler : employeurs du travailleur connecté, POR-11, #340), avec
    # leur instantané de rattrapage (OccupationInstantaneeHandler, ADR 0011).
    "affilies" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["integrations", "personnes"]
      subject_filters = {
        integrations = ["integrations.donnees-bce-recues.v1"]
        personnes    = ["personnes.occupation-debutee.v1", "personnes.occupation-terminee.v1", "personnes.occupation-instantanee.v1"]
      }
      # AFF-01, AFF-02, INT-04 : lecture des données d'entreprise chez Intégrations avec le compte technique du service.
      compte_technique  = "Affilies__CompteTechnique"
      internal_services = { Affilies__ServicesInternes__Integrations = "integrations" }
    }
    "personnes" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["integrations", "affilies"]
      # ARC-45, DAT-06 : clés de chiffrement des champs sensibles et clé de l'index aveugle du NISS, toutes deux
      # enveloppées par la clé racine personnes-fields-kek (ADR 0012).
      encryption        = { Encryption = "standard" }
      key_vault_secrets = { "blindindex--cleenveloppee" = "blindindex--cleenveloppee" }
    }
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
        "reintegration", # trajet-demarre, trajet-termine, incapacite-saisie (TrajetDemarreHandler, TrajetTermineHandler, IncapaciteSaisieHandler)
      ]
      # Gestionnaires enregistrés pour ces deux rubriques (SAN-60, SAN-62) : seuls ces sujets intéressent le service.
      subject_filters = {
        integrations  = ["integrations.incapacite-notifiee.v1"]
        reintegration = ["reintegration.trajet-demarre.v1", "reintegration.trajet-termine.v1", "reintegration.incapacite-saisie.v1"]
      }
    }
    "planification" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["obligations", "referentiels", "communications"]
      adapters      = ["Planification__Adaptateurs__OutilRh", "Planification__Adaptateurs__AgendaExterne"]
    }
    "prevention"  = { zone = "standard", database = true, subscribes_to = ["affilies", "postes-risques", "bff-employeur"] }
    "prestations" = { zone = "standard", database = true, subscribes_to = ["planification", "prevention", "surveillance-medicale", "reintegration", "psychosocial"] }
    # Documents : une instance par zone qui produit des documents (décision #356). Celle-ci est l'instance de la zone
    # standard ; documents-medicale (zone médicale) exécute le même code avec sa base, son stockage et sa clé racine.
    "documents" = {
      zone     = "standard"
      database = true
      # surveillance-medicale : seul abonnement avec gestionnaire pour la zone standard (formulaire d'évaluation de santé,
      # SAN-30, exemplaire employeur) ; les autres producteurs (reintegration, psychosocial, prevention) ne sont pas
      # encore écrits. La copie du dossier et le carnet de vaccination sont traités par documents-medicale.
      # affilies, personnes : projection du contexte linguistique (ProjectionLinguistiqueHandler, NF-41, ADR 0009), tenue
      # par chaque instance ; l'instance standard la tient prête mais lit la langue par HTTP (DOCUMENTS__SOURCELINGUISTIQUE).
      # Les instantanés affilie-instantane et personne-instantanee la rattrapent à la mise en service (ADR 0011).
      subscribes_to = ["surveillance-medicale", "reintegration", "psychosocial", "prevention", "affilies", "personnes"]
      subject_filters = {
        surveillance-medicale = ["surveillance-medicale.decision-emise.v1"]
        affilies              = ["affilies.affilie-cree.v1", "affilies.affilie-modifie.v1", "affilies.affilie-instantane.v1"]
        personnes             = ["personnes.occupation-debutee.v1", "personnes.langue-modifiee.v1", "personnes.personne-instantanee.v1"]
      }
      # NF-41 : langue des documents lue chez Affiliés et Personnes (compte technique). Aucun appel à la zone médicale.
      compte_technique = "Documents__CompteTechnique"
      internal_services = {
        Documents__ServicesInternes__Affilies  = "affilies"
        Documents__ServicesInternes__Personnes = "personnes"
      }
      adapters = [
        "Documents__Adaptateurs__Horodatage",
        "Documents__Adaptateurs__Signature",
        "Documents__Adaptateurs__SourceLinguistique",
      ]
    }
    # Instance zone médicale de Documents (décision #356, ADR 0009, ARC-06) : exemplaires travailleur et dossier de
    # DecisionEmise (SAN-30), copie du dossier (SAN-43), carnet de vaccination (SAN-50) et pièces jointes (SAN-41) sont
    # lus, rendus et archivés dans la zone médicale. Clé racine documents-kek, base et stockage de la zone médicale.
    # Le contexte linguistique vient d'une projection alimentée par les événements d'Affiliés et de Personnes
    # (DOCUMENTS__SOURCELINGUISTIQUE = Projection, lot 7, #366) : aucun appel à Affiliés ni à Personnes, que le NSG de la
    # zone médicale interdit. Seuls la langue et le régime linguistique voyagent (ARC-06).
    "documents-medicale" = {
      zone             = "medicale"
      instance_de      = "documents"
      database         = true
      publishes_events = false # publie sur le topic « documents » par l'émetteur partagé
      subscribes_to    = ["surveillance-medicale", "affilies", "personnes"]
      # Contrats de la Surveillance médicale qui ont un gestionnaire dans l'instance médicale (SAN-30, SAN-43, SAN-50) et
      # contrats qui alimentent la projection linguistique (ProjectionLinguistiqueHandler), instantanés de rattrapage compris
      # (ADR 0011 : sans eux, les affiliés et personnes antérieurs à la mise en service n'ont pas de langue projetée).
      subject_filters = {
        surveillance-medicale = [
          "surveillance-medicale.decision-emise.v1",
          "surveillance-medicale.copie-dossier-validee.v1",
          "surveillance-medicale.vaccination-administree.v1",
        ]
        affilies  = ["affilies.affilie-cree.v1", "affilies.affilie-modifie.v1", "affilies.affilie-instantane.v1"]
        personnes = ["personnes.occupation-debutee.v1", "personnes.langue-modifiee.v1", "personnes.personne-instantanee.v1"]
      }
      # Contenu clinique lu chez Surveillance médicale (intra-zone) avec le compte technique de l'instance.
      compte_technique = "Documents__CompteTechnique"
      internal_services = {
        Documents__ServicesInternes__SurveillanceMedicale = "surveillance-medicale"
      }
      # SourceLinguistique « Reel » : adaptateur Projection (table locale), sans appel réseau hors de la zone.
      adapters = [
        "Documents__Adaptateurs__Horodatage",
        "Documents__Adaptateurs__Signature",
        "Documents__Adaptateurs__SourceLinguistique",
      ]
    }
    "communications" = {
      zone          = "standard"
      database      = true
      subscribes_to = ["planification", "documents", "audit"]
      # Seul le bris de glace intéresse Communications sur le topic partagé « audit » (NF-04).
      subject_filters = { audit = ["audit.bris-de-glace-signale.v1"] }
      # DOC-03 : coordonnées et langue des destinataires lues chez Affiliés et Personnes (compte technique).
      compte_technique = "Communications__CompteTechnique"
      internal_services = {
        Communications__ServicesInternes__Affilies  = "affilies"
        Communications__ServicesInternes__Personnes = "personnes"
      }
      adapters = [
        "Communications__Adaptateurs__Annuaire",
        "Communications__Adaptateurs__Portail",
        "Communications__Adaptateurs__Email",
        "Communications__Adaptateurs__Sms",
        "Communications__Adaptateurs__Courrier",
        "Communications__Adaptateurs__RecommandeElectronique",
        "Communications__Adaptateurs__EBoxEntreprise",
        "Communications__Adaptateurs__EBoxCitoyen",
      ]
    }
    "integrations" = {
      zone     = "standard"
      database = true
      # affilies : seul abonnement avec gestionnaire (AffilieCree, AffilieModifie) ; prestations n'est pas encore écrit.
      subscribes_to = ["affilies", "prestations"]
      # INT-04 : appel du service Personnes (flux DIMONA) avec le compte technique ; chiffrement des charges utiles (ARC-45).
      compte_technique  = "Integrations__Personnes"
      internal_services = { Integrations__Personnes__BaseAddress = "personnes" }
      encryption        = { Encryption = "standard" }
      adapters = [
        "Integrations__Adaptateurs__Bce",
        "Integrations__Adaptateurs__Dimona",
        "Integrations__Adaptateurs__RegistreNational",
        "Integrations__Adaptateurs__Incapacites",
      ]
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
      # audit : traces d'accès (NF-04) ; referentiels : durée de conservation du journal, AUDIT.CONSERVATION (ARC-21).
      subscribes_to   = ["audit", "referentiels"]
      subject_filters = { referentiels = ["referentiels.parametre-legal-modifie.v1"] }
    }

    # --- Zone médicale (ARC-04) ---
    "surveillance-medicale" = {
      zone          = "medicale"
      database      = true
      cpu           = 1
      memory        = "2Gi"
      subscribes_to = ["planification", "personnes", "postes-risques", "obligations", "referentiels", "prevention", "documents"]
      # personnes.etat-particulier-declare.v1 est réservé à Obligations (catalogue des événements) ; documents : seule la
      # publication intéresse le service (copie du dossier publiée au travailleur, SAN-43).
      # referentiels : paramètres légaux (conservation du dossier, délais de concertation et de recours, ARC-21) et jours fériés (DAT-08).
      subject_filters = {
        personnes    = ["personnes.affectation-modifiee.v1"]
        documents    = ["documents.document-publie.v1"]
        referentiels = ["referentiels.parametre-legal-modifie.v1", "referentiels.jours-feries-modifies.v1"]
      }
      # ARC-04, ARC-45 : clés propres à la zone médicale (Key Vault Premium / HSM de la zone), jamais partagées.
      # Pas d'adaptateur « Reel » : seuls les simulateurs existent, fixés par appsettings.json (le service refuse le reste).
      encryption = { ZoneMedicale__Encryption = "medicale" }
    }
    "reintegration" = {
      zone          = "medicale"
      database      = true
      subscribes_to = ["integrations", "obligations", "planification", "referentiels"]
      # Gestionnaires enregistrés (SAN-60, SAN-63, ARC-21, DAT-08) : seuls ces sujets intéressent le service.
      subject_filters = {
        integrations  = ["integrations.incapacite-notifiee.v1"]
        obligations   = ["obligations.obligation-creee.v1", "obligations.reprise-enregistree.v1"]
        planification = ["planification.absence-rendez-vous-constatee.v1"]
        referentiels  = ["referentiels.parametre-legal-modifie.v1", "referentiels.jours-feries-modifies.v1"]
      }
      # ARC-04, ARC-45 : clés propres à la zone médicale, jamais partagées. SAN-64 : canal eHealth (squelette, INT-04).
      encryption = { ZoneMedicale__Encryption = "medicale" }
      adapters   = ["Reintegration__Adaptateurs__EHealth"]
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

  # Une instance supplémentaire désigne un service d'origine existant (lui-même pas une instance) et ne crée pas de topic :
  # elle publie sur le topic du service d'origine par l'émetteur partagé.
  validation {
    condition = alltrue([
      for n, s in var.services : s.instance_de == null || (
        try(var.services[s.instance_de].instance_de == null, false) && !s.publishes_events
      )
    ])
    error_message = "instance_de doit désigner un service existant qui n'est pas lui-même une instance, et l'instance ne doit pas avoir de topic propre (publishes_events = false : émetteur partagé du service d'origine)."
  }

  validation {
    condition = alltrue([
      for n, s in var.services : s.instance_de == null || s.zone != try(var.services[s.instance_de].zone, s.zone)
    ])
    error_message = "Une instance (instance_de) doit être déployée dans une autre zone que le service d'origine."
  }

  # Une seule instance de Documents par zone : un stockage et une clé racine par zone.
  validation {
    condition = alltrue([
      for z in distinct([for s in values(var.services) : s.zone]) :
      length([for n, s in var.services : n if s.zone == z && coalesce(s.instance_de, n) == "documents"]) <= 1
    ])
    error_message = "Une seule instance du service documents par zone (un stockage et une clé racine par zone)."
  }

  # ARC-04 : un service ne référence que le coffre de sa propre zone.
  validation {
    condition     = alltrue([for s in values(var.services) : alltrue([for zone in values(s.encryption) : zone == s.zone])])
    error_message = "encryption ne peut désigner que la zone du service (ARC-04) : un service ne lit jamais le Key Vault d'une autre zone."
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

variable "encryption_key_ids" {
  description = "Identifiants des clés de données (DEK) du chiffrement des champs (ARC-44, ARC-45, ADR 0012) : le premier est la DEK courante (CurrentKeyId), les suivants sont les anciennes DEK conservées pour déchiffrer pendant la rotation. Un secret Key Vault <service>--<préfixe>--deks--<id en minuscules>, valeur « <kid>|<DEK enveloppée en base64> » produite avec la clé racine du service, doit exister dans le coffre de sa zone (infra/README.md). Identifiants en majuscules et chiffres : les variables d'environnement sont en majuscules."
  type        = list(string)
  default     = ["D1"]

  validation {
    condition     = length(var.encryption_key_ids) > 0 && alltrue([for id in var.encryption_key_ids : can(regex("^[A-Z0-9]{1,16}$", id))])
    error_message = "encryption_key_ids doit contenir au moins un identifiant de 1 à 16 caractères parmi A-Z et 0-9."
  }
}

variable "encryption_legacy_key_ids" {
  description = "Identifiants des anciennes clés AES du format v1 (secrets <service>--<préfixe>--keys--<id>, antérieurs à l'ADR 0012), injectées en lecture seule tant que des valeurs v1 subsistent ; elles ne chiffrent plus jamais. Vide pour un environnement sans données antérieures."
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for id in var.encryption_legacy_key_ids : can(regex("^[A-Z0-9]{1,16}$", id)) && !contains(var.encryption_key_ids, id)])
    error_message = "encryption_legacy_key_ids : identifiants de 1 à 16 caractères parmi A-Z et 0-9, distincts de encryption_key_ids."
  }
}

# -----------------------------------------------------------------------------
# Identité et adaptateurs externes (ARC-40, ARC-05, NF-14)
# -----------------------------------------------------------------------------

variable "entra_tenant_id" {
  description = "Identifiant du tenant Microsoft Entra de l'environnement : émetteur des jetons (Authentication__Authority) et point de jeton des comptes techniques. Identifiant public, pas un secret."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.entra_tenant_id))
    error_message = "entra_tenant_id doit être un GUID."
  }
}

variable "authentication_audience" {
  description = "Audience des jetons acceptés par les services de l'environnement (Authentication__Audience), par ex. api://sepp-dev. Les comptes techniques demandent la portée <audience>/.default."
  type        = string

  validation {
    condition     = length(trimspace(var.authentication_audience)) > 0
    error_message = "authentication_audience ne peut pas être vide."
  }
}

variable "portal_origins" {
  description = "Origines CORS autorisées sur chaque BFF (#358, ARC-42, ARC-43, ADR 0013), par nom de service : bff-interne (application interne), bff-employeur et bff-travailleur. Une origine = https://hôte[:port] des domaines publics (APIM, Application Gateway ou domaine du portail) ; ni joker, ni http, ni chemin. Injectées en CORS__ORIGINESAUTORISEES__<i> ; au démarrage, le BFF refuse une liste vide ou invalide."
  type        = map(list(string))

  validation {
    condition     = alltrue([for n in ["bff-interne", "bff-employeur", "bff-travailleur"] : contains(keys(var.portal_origins), n)]) && alltrue([for n in keys(var.portal_origins) : contains(["bff-interne", "bff-employeur", "bff-travailleur"], n)])
    error_message = "portal_origins doit définir exactement bff-interne, bff-employeur et bff-travailleur."
  }

  validation {
    condition     = alltrue([for n, l in var.portal_origins : length(l) > 0])
    error_message = "portal_origins : au moins une origine par BFF (une liste vide empêche le démarrage du BFF)."
  }

  validation {
    condition     = alltrue([for n, l in var.portal_origins : alltrue([for o in l : !strcontains(o, "*")])])
    error_message = "portal_origins : le joker * est interdit, les origines doivent être explicites (NF-03)."
  }

  validation {
    condition     = alltrue([for n, l in var.portal_origins : alltrue([for o in l : can(regex("^https://[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?(:[0-9]{1,5})?$", o))])])
    error_message = "portal_origins : chaque origine doit être de la forme https://hôte[:port], sans chemin, sans barre finale ni identifiants (https obligatoire)."
  }
}

variable "technical_account_client_ids" {
  description = "Identifiant client (public) du compte technique OIDC de chaque service qui en déclare un (compte_technique), par nom de service. Le secret du compte est dans le Key Vault de la zone, jamais ici."
  type        = map(string)
  default     = {}

  validation {
    condition     = alltrue([for n, s in var.services : s.compte_technique == null || contains(keys(var.technical_account_client_ids), n)])
    error_message = "technical_account_client_ids doit fournir l'identifiant client de chaque service qui déclare un compte_technique dans var.services."
  }
}

variable "adapter_mode" {
  description = "Choix des adaptateurs externes des services (Simulateur ou Reel) : Simulateur partout hors production. Reel n'est accepté qu'en production ; les services refusent au démarrage un adaptateur Reel non implémenté hors production."
  type        = string
  default     = "Simulateur"

  validation {
    condition     = contains(["Simulateur", "Reel"], var.adapter_mode)
    error_message = "adapter_mode doit valoir Simulateur ou Reel."
  }

  validation {
    condition     = var.adapter_mode == "Simulateur" || var.environment == "prod"
    error_message = "Les adaptateurs Reel ne sont autorisés qu'en production : utiliser Simulateur dans les autres environnements."
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
  description = "Adresse de la liste de diffusion d'exploitation destinataire des alertes (obligatoire, NF-62). Aucune adresse réelle dans le dépôt public : utiliser une adresse @sepp.example.be d'exemple."
  type        = string

  validation {
    condition     = var.alert_email != null && can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.alert_email))
    error_message = "alert_email est obligatoire et doit être une adresse e-mail valide (valeur vide refusée)."
  }
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
