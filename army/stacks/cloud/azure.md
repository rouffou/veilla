# Référentiel cloud — Microsoft Azure

## Cadres
- **Cloud Adoption Framework (CAF)** : landing zones, gouvernance, conventions de nommage.
- **Well-Architected Framework (WAF)** : fiabilité, sécurité, coût, excellence opérationnelle, performance.

## Organisation
- Management Groups → Subscriptions (une par environnement et par charge critique) → Resource Groups (par application et cycle de vie).
- Nommage CAF : `<type>-<app>-<env>-<region>-<instance>` (ex. `app-orders-prd-weu-001`).
- Tags obligatoires : `app`, `env`, `owner`, `cost-center`, `data-classification`.
- Azure Policy pour imposer régions autorisées, tags, chiffrement, interdiction d'IP publiques non justifiées.

## Services par défaut
| Besoin | Service par défaut | Alternatives |
|---|---|---|
| Conteneurs (API, workers) | Azure Container Apps | AKS (besoins avancés), App Service |
| Fonctions événementielles | Azure Functions (Flex Consumption) | — |
| Base relationnelle | Azure Database for PostgreSQL Flexible Server / Azure SQL | — |
| NoSQL | Cosmos DB | — |
| Cache | Azure Cache for Redis / Azure Managed Redis | — |
| Messaging | Service Bus (commandes), Event Grid (événements), Event Hubs (streaming) | — |
| Stockage objet | Blob Storage | — |
| Secrets / clés | Key Vault (RBAC) | — |
| Identité | Entra ID, Managed Identity, Workload Identity Federation (CI) | — |
| Exposition | Front Door + WAF, API Management | Application Gateway |
| Observabilité | Azure Monitor, Application Insights (OpenTelemetry), Log Analytics | — |
| Registre | Azure Container Registry | — |
| IaC | Bicep (AVM — Azure Verified Modules) | Terraform (azurerm) |

## Sécurité réseau
- Hub-and-spoke ou Virtual WAN ; Private Endpoints pour PaaS ; accès public désactivé par défaut.
- Defender for Cloud activé ; Microsoft Sentinel si SIEM requis.

## FinOps
- Budgets et alertes par subscription ; Azure Advisor ; réservations / savings plans pour charges stables ; arrêt automatique des environnements hors prod.
