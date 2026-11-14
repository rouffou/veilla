# Référentiel cloud — Amazon Web Services

## Cadres
- **AWS Well-Architected Framework** (6 piliers : excellence opérationnelle, sécurité, fiabilité, performance, coût, durabilité).
- **AWS Control Tower** + **Organizations** pour la landing zone multi-comptes.

## Organisation
- OU par usage (Security, Infrastructure, Workloads/Prod, Workloads/NonProd, Sandbox) ; un compte par application et environnement pour les charges critiques.
- Service Control Policies (SCP) : régions autorisées, interdiction de désactiver CloudTrail / GuardDuty.
- Tags obligatoires : `app`, `env`, `owner`, `cost-center`, `data-classification` (imposés par Tag Policies).

## Services par défaut
| Besoin | Service par défaut | Alternatives |
|---|---|---|
| Conteneurs | ECS sur Fargate | EKS (besoins Kubernetes), App Runner |
| Fonctions | Lambda | — |
| Base relationnelle | RDS / Aurora PostgreSQL | Aurora Serverless v2 |
| NoSQL | DynamoDB | DocumentDB |
| Cache | ElastiCache (Valkey / Redis) | — |
| Messaging | SQS (files), SNS (pub/sub), EventBridge (bus d'événements), Kinesis / MSK (streaming) | — |
| Stockage objet | S3 (Block Public Access activé) | — |
| Secrets / clés | Secrets Manager, KMS, SSM Parameter Store | — |
| Identité | IAM Identity Center, rôles IAM, OIDC pour la CI | — |
| Exposition | CloudFront + WAF, API Gateway, ALB | — |
| Observabilité | CloudWatch, X-Ray / ADOT (OpenTelemetry) | — |
| Registre | ECR | — |
| IaC | Terraform (aws provider) ou AWS CDK | CloudFormation |

## Sécurité
- GuardDuty, Security Hub, AWS Config, CloudTrail organisationnel activés.
- VPC avec sous-réseaux privés pour les charges ; VPC Endpoints pour les services AWS.
- Aucune clé d'accès IAM longue durée : rôles et OIDC.

## FinOps
- AWS Budgets, Cost Explorer, Compute Optimizer ; Savings Plans pour charges stables ; Graviton par défaut quand compatible.
