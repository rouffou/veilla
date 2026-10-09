# Décisions d'architecture (ADR)

| N° | Décision | Statut | Exigences |
|---|---|---|---|
| [0001](0001-stack-backend-dotnet.md) | Backend en .NET 10 (LTS) / C# | Acceptée | ARC-08, §14.9 |
| [0002](0002-monorepo.md) | Monodépôt avec pipelines par service | Acceptée | ARC-01, ARC-24, CTR-20 |
| [0003](0003-plateforme-container-apps.md) | Exécution sur Azure Container Apps | Acceptée | CTR-00, CTR-10 |
| [0004](0004-messagerie-outbox.md) | Azure Service Bus + outbox/inbox maison | Acceptée | ARC-05, ARC-31, ARC-32 |
| [0005](0005-identite-autorisation.md) | OIDC (Entra ID + CSAM), autorisation centralisée | Acceptée | ARC-40, ARC-41, NF-01, NF-02 |
| [0006](0006-front-angular.md) | Front Angular, un workspace, trois applications | Acceptée | §14.9, NF-40, NF-50 |
| [0007](0007-infrastructure-terraform.md) | Infrastructure as Code en Terraform | Acceptée (amendée : règle `$Default` Service Bus via azapi, #295) | ARC-51, CTR-21 |
| [0008](0008-sagas-processus-persistants.md) | Sagas : processus persistants, minuteries en base, déclencheur par API (amende la 0004) | Proposée (déclencheur à valider par l'architecte) | ARC-33, ARC-32, POR-04 |

Format : contexte, décision, alternatives, conséquences.
