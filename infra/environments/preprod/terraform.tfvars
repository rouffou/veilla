# Environnement de préproduction (CTR-22) : reproduit la topologie de production
# (redondance de zone, HSM, passerelle Premium, réplication de reprise).
# Les identifiants Entra ID ci-dessous sont des valeurs d'exemple à remplacer ;
# ce ne sont pas des secrets (CTR-16). L'abonnement est fourni par ARM_SUBSCRIPTION_ID.

environment   = "preprod"
address_space = "10.40.0.0/16"

# CTR-14 / ARC-52 / ARC-53 : disponibilité des zones et services à confirmer pour Belgium Central.
zone_redundancy_enabled = true
enable_dr_replication   = true
dr_location             = "swedencentral"

default_min_replicas  = 2
deploy_container_apps = true
image_tag             = "0.1.0"
dedicated_workload_profile = {
  workload_profile_type = "D4"
  minimum_count         = 3
  maximum_count         = 10
}

postgres_sku_name                     = "GP_Standard_D4ds_v5"
postgres_backup_retention_days        = 35
postgres_high_availability_enabled    = true
postgres_geo_redundant_backup_enabled = false
postgres_entra_admin = {
  object_id      = "00000000-0000-0000-0000-000000000000"
  principal_name = "grp-sepp-preprod-dba"
}

servicebus_capacity          = 1
storage_replication_type     = "ZRS"
document_retention_days      = 30
document_immutability_locked = false

# ARC-44 : Managed HSM dédié aux zones médicale et psychosociale.
managed_hsm_enabled          = true
managed_hsm_admin_object_ids = ["00000000-0000-0000-0000-000000000000"]

log_retention_days = 90
alert_email        = "exploitation-sepp@example.be"

apim_sku_name                   = "Premium_3"
apim_zones                      = ["1", "2", "3"]
apim_publisher_email            = "exploitation-sepp@example.be"
appgw_min_capacity              = 2
appgw_max_capacity              = 10
appgw_ssl_certificate_secret_id = null
waf_mode                        = "Prevention"

policy_effect                 = "Deny"
additional_allowed_registries = []

tags = {
  centre_de_cout = "sepp-preprod"
}
