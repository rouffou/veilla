# Environnement de production (CTR-22).
# Les identifiants Entra ID ci-dessous sont des valeurs d'exemple à remplacer ;
# ce ne sont pas des secrets (CTR-16). L'abonnement est fourni par ARM_SUBSCRIPTION_ID.

environment   = "prod"
address_space = "10.50.0.0/16"

# CTR-14 / ARC-52 / ARC-53 : disponibilité des zones et services à confirmer pour Belgium Central.
zone_redundancy_enabled = true
enable_dr_replication   = true
dr_location             = "swedencentral"

# CTR-14 : au moins deux instances par service (le module racine l'impose aussi).
default_min_replicas  = 2
deploy_container_apps = true
image_tag             = "0.1.0"
dedicated_workload_profile = {
  workload_profile_type = "D4"
  minimum_count         = 3
  maximum_count         = 20
}

# NF-33 : sauvegardes quotidiennes, restauration à un instant donné sur 35 jours.
postgres_sku_name                     = "GP_Standard_D4ds_v5"
postgres_backup_retention_days        = 35
postgres_high_availability_enabled    = true
postgres_geo_redundant_backup_enabled = false
postgres_entra_admin = {
  object_id      = "00000000-0000-0000-0000-000000000000"
  principal_name = "grp-sepp-prod-dba"
}

servicebus_capacity      = 2
storage_replication_type = "ZRS"

# NF-20 / NF-21 : rétention WORM minimale de 10 ans ; le verrouillage est IRRÉVERSIBLE.
# À valider avec le responsable du traitement avant le premier déploiement.
document_retention_days      = 3650
document_immutability_locked = true

# ARC-44 : Managed HSM dédié aux zones médicale et psychosociale.
managed_hsm_enabled          = true
managed_hsm_admin_object_ids = ["00000000-0000-0000-0000-000000000000"]

log_retention_days = 365
alert_email        = "exploitation-sepp@example.be"

apim_sku_name = "Premium_3"
apim_zones    = ["1", "2", "3"]
# Certificat TLS public obligatoire en production (ARC-42) : identifiant sans version, ex.
# https://kv-prod-plt-xxxxxx.vault.azure.net/secrets/cert-portail
apim_publisher_email            = "exploitation-sepp@example.be"
appgw_min_capacity              = 2
appgw_max_capacity              = 20
appgw_ssl_certificate_secret_id = null
waf_mode                        = "Prevention"

policy_effect                 = "Deny"
additional_allowed_registries = []

tags = {
  centre_de_cout = "sepp-prod"
}
