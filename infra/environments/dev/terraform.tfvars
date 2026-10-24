# Environnement de développement (CTR-22). Données fictives uniquement (NF-14).
# Les identifiants Entra ID ci-dessous sont des valeurs d'exemple à remplacer ;
# ce ne sont pas des secrets (CTR-16). L'abonnement est fourni par ARM_SUBSCRIPTION_ID.

environment   = "dev"
address_space = "10.10.0.0/16"

# Disponibilité : pas de redondance de zone ni de réplication hors production.
zone_redundancy_enabled = false
enable_dr_replication   = false
dr_location             = "swedencentral"

# Exécution : mise à zéro possible en dev (KEDA), profil Consumption uniquement.
default_min_replicas       = 0
deploy_container_apps      = true
image_tag                  = "0.1.0"
dedicated_workload_profile = null

# PostgreSQL
postgres_sku_name                     = "B_Standard_B1ms"
postgres_backup_retention_days        = 7
postgres_high_availability_enabled    = false
postgres_geo_redundant_backup_enabled = false
postgres_entra_admin = {
  object_id      = "00000000-0000-0000-0000-000000000000"
  principal_name = "grp-sepp-dev-dba"
}

# Bus et stockage documentaire
servicebus_capacity          = 1
storage_replication_type     = "LRS"
document_retention_days      = 1
document_immutability_locked = false

# Clés : Key Vault Premium (clés HSM) au lieu du Managed HSM hors production.
managed_hsm_enabled          = false
managed_hsm_admin_object_ids = []

# Identité (ARC-40) : tenant Entra et audience des jetons de l'environnement. Valeurs d'exemple à remplacer
# (identifiants publics, pas des secrets) ; les secrets des comptes techniques sont dans le Key Vault de la zone.
entra_tenant_id         = "00000000-0000-0000-0000-000000000000"
authentication_audience = "api://sepp-dev"

# CORS des BFF (#358) : origines exactes des portails (domaines publics APIM / Application Gateway), https, sans joker.
# Valeurs d exemple à remplacer par les domaines réels de l environnement.
portal_origins = {
  "bff-employeur"   = ["https://employeur.dev.sepp.example.be"]
  "bff-travailleur" = ["https://travailleur.dev.sepp.example.be"]
}
technical_account_client_ids = {
  affilies           = "00000000-0000-0000-0000-000000000000"
  communications     = "00000000-0000-0000-0000-000000000000"
  documents          = "00000000-0000-0000-0000-000000000000"
  documents-medicale = "00000000-0000-0000-0000-000000000000" # instance zone médicale : GUID d'exemple à remplacer
  integrations       = "00000000-0000-0000-0000-000000000000"
}

# Adaptateurs externes : Simulateur hors production (ARC-05, NF-14).
adapter_mode = "Simulateur"

# Chiffrement applicatif (ARC-45) : identifiant de la clé courante en premier, anciennes clés ensuite (rotation).
encryption_key_ids = ["K1"]

# Observabilité
log_retention_days = 30
alert_email        = null

# Passerelle
apim_sku_name                   = "Developer_1"
apim_zones                      = []
apim_publisher_email            = "exploitation-sepp@example.be"
appgw_min_capacity              = 1
appgw_max_capacity              = 2
appgw_ssl_certificate_secret_id = null
waf_mode                        = "Detection"

# Gouvernance
policy_effect                 = "Deny"
additional_allowed_registries = []

tags = {
  centre_de_cout = "sepp-dev"
}
