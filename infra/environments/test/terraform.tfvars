# Environnement de test (CTR-22). Données fictives ou pseudonymisées uniquement (NF-14).
# Les identifiants Entra ID ci-dessous sont des valeurs d'exemple à remplacer ;
# ce ne sont pas des secrets (CTR-16). L'abonnement est fourni par ARM_SUBSCRIPTION_ID.

environment   = "test"
address_space = "10.20.0.0/16"

zone_redundancy_enabled = false
enable_dr_replication   = false
dr_location             = "swedencentral"

default_min_replicas       = 1
deploy_container_apps      = true
image_tag                  = "0.1.0"
dedicated_workload_profile = null

postgres_sku_name                     = "B_Standard_B2s"
postgres_backup_retention_days        = 7
postgres_high_availability_enabled    = false
postgres_geo_redundant_backup_enabled = false
postgres_entra_admin = {
  object_id      = "00000000-0000-0000-0000-000000000000"
  principal_name = "grp-sepp-test-dba"
}

servicebus_capacity          = 1
storage_replication_type     = "LRS"
document_retention_days      = 1
document_immutability_locked = false

managed_hsm_enabled          = false
managed_hsm_admin_object_ids = []

log_retention_days = 30
alert_email        = null

apim_sku_name                   = "Developer_1"
apim_zones                      = []
apim_publisher_email            = "exploitation-sepp@example.be"
appgw_min_capacity              = 1
appgw_max_capacity              = 2
appgw_ssl_certificate_secret_id = null
waf_mode                        = "Prevention"

policy_effect                 = "Deny"
additional_allowed_registries = []

tags = {
  centre_de_cout = "sepp-test"
}
