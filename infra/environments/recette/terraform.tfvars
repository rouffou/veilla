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

# Identité (ARC-40) : tenant Entra et audience des jetons de l'environnement. Valeurs d'exemple à remplacer
# (identifiants publics, pas des secrets) ; les secrets des comptes techniques sont dans le Key Vault de la zone.
entra_tenant_id         = "00000000-0000-0000-0000-000000000000"
authentication_audience = "api://sepp-recette"

# CORS des BFF (#358) : origines exactes des portails (domaines publics APIM / Application Gateway), https, sans joker.
# Valeurs d exemple à remplacer par les domaines réels de l environnement.
portal_origins = {
  "bff-employeur"   = ["https://employeur.recette.sepp.example.be"]
  "bff-travailleur" = ["https://travailleur.recette.sepp.example.be"]
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

# Chiffrement des champs par enveloppe (ARC-44, ARC-45, ADR 0012) : DEK courante en premier, anciennes DEK ensuite
# (rotation). Secrets <service>--<préfixe>--deks--d1 à créer avant l'apply (infra/README.md).
encryption_key_ids = ["D1"]
# Anciennes clés AES du format v1 (secrets <service>--<préfixe>--keys--k1), en lecture seule : à vider une fois toutes
# les valeurs v1 rechiffrées, ou dès l'origine pour un environnement sans données antérieures (à valider par environnement).
encryption_legacy_key_ids = ["K1"]

log_retention_days = 60
alert_email        = "exploitation-recette@sepp.example.be"

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
