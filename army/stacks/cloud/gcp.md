# Référentiel cloud — Google Cloud Platform

## Cadres
- **Google Cloud Architecture Framework** (excellence opérationnelle, sécurité, fiabilité, coût, performance).
- **Enterprise foundations blueprint** pour la landing zone.

## Organisation
- Organisation → Folders (par environnement / BU) → Projects (par application et environnement).
- Organization Policies : régions autorisées, interdiction des clés de comptes de service, IP publiques restreintes.
- Labels obligatoires : `app`, `env`, `owner`, `cost-center`, `data-classification`.

## Services par défaut
| Besoin | Service par défaut | Alternatives |
|---|---|---|
| Conteneurs | Cloud Run | GKE Autopilot (besoins Kubernetes) |
| Fonctions | Cloud Run functions | — |
| Base relationnelle | Cloud SQL (PostgreSQL) / AlloyDB | Spanner (échelle mondiale) |
| NoSQL | Firestore / Bigtable | — |
| Cache | Memorystore | — |
| Messaging | Pub/Sub, Eventarc | — |
| Analytique | BigQuery | — |
| Stockage objet | Cloud Storage (Uniform bucket-level access) | — |
| Secrets / clés | Secret Manager, Cloud KMS | — |
| Identité | Cloud IAM, Workload Identity (GKE), Workload Identity Federation (CI) | — |
| Exposition | Cloud Load Balancing + Cloud Armor, API Gateway / Apigee | — |
| Observabilité | Cloud Logging, Monitoring, Trace (OpenTelemetry) | — |
| Registre | Artifact Registry | — |
| IaC | Terraform (google provider) | — |

## Sécurité
- Security Command Center ; VPC Service Controls pour les données sensibles ; Private Service Connect.
- Comptes de service dédiés par charge, sans clés téléchargées.

## FinOps
- Budgets et alertes, export de facturation vers BigQuery, Committed Use Discounts, recommandations Active Assist.
