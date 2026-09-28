# Environnement de recette (CTR-22, REC-01). Données pseudonymisées uniquement (NF-14).
# Les identifiants Entra ID ci-dessous sont des valeurs d'exemple à remplacer ;
# ce ne sont pas des secrets (CTR-16). L'abonnement est fourni par ARM_SUBSCRIPTION_ID.

environment   = "recette"
address_space = "10.30.0.0/16"

zone_redundancy_enabled = false
enable_dr_replication   = false
dr_location             = "swedencentral"

default_min_replicas       = 1
deploy_container_apps      = true
image_tag                  = "0.1.0"
dedicated_workload_profile = null

postgres_sku_name                     = "GP_Standard_D2ds_v5"
postgres_backup_retention_days        = 7
postgres_high_availability_enabled    = false
postgres_geo_redundant_backup_enabled = false
postgres_entra_admin = {
  object_id      = "00000000-0000-0000-0000-000000000000"
  principal_name = "grp-sepp-recette-dba"
}

servicebus_capacity          = 1
storage_replication_type     = "ZRS"
document_retention_days      = 7
document_immutability_locked = false

managed_hsm_enabled          = false
managed_hsm_admin_object_ids = []

log_retention_days = 60
alert_email        = "exploitation-sepp@example.be"

apim_sku_name                   = "Developer_1"
apim_zones                      = []
apim_publisher_email            = "exploitation-sepp@example.be"
appgw_min_capacity              = 1
appgw_max_capacity              = 3
appgw_ssl_certificate_secret_id = null
waf_mode                        = "Prevention"

policy_effect                 = "Deny"
additional_allowed_registries = []

tags = {
  centre_de_cout = "sepp-recette"
}
