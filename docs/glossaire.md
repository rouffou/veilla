# Glossaire (langage omniprésent)

Ce glossaire reprend les termes du §17 du cahier des charges SEPP (v1.1, [cahier-des-charges-sepp.pdf](cahier-des-charges-sepp.pdf)).
Il sert de langage commun entre métier, architecture et code (DDD) : les noms de la colonne « Nom dans le code » sont
ceux qui existent réellement dans `src/` ; « — » signifie qu'aucun nom correspondant n'a été trouvé. Les définitions
sont reformulées. Les points non tranchés sont listés en fin de document.

| Terme | Définition | Nom dans le code | Service propriétaire |
|---|---|---|---|
| API Gateway | Point d'entrée unique des appels externes : sécurité, routage et limitation de débit. | `infra/modules/apim` (module Terraform) | Plateforme |
| BCE | Banque-Carrefour des Entreprises. Définition à vérifier (note 1). | `EntrepriseBce`, `BceAdaptateurReel` | Integrations |
| BCSS | Banque Carrefour de la Sécurité Sociale. Définition à vérifier (note 1). | `ConfigurationBcss` | Integrations |
| BFF | Backend for Frontend : service d'agrégation propre à un canal (interne, employeur, travailleur). | `Sepp.Bff.Employeur`, `Sepp.Bff.Travailleur` | BFF employeur, BFF travailleur (`src/Bff/`) |
| CBE | Contrôle du bien-être au travail (inspection du SPF ETCS). Définition à vérifier (note 1). | — | Externe (inspection) |
| Chiffrement par enveloppe | Chiffrement d'un document par une clé de données aléatoire, elle-même chiffrée (enveloppée) par une clé racine de la zone : seule la zone peut déballer la clé de données. | — | Documents |
| Comité PPT | Comité pour la prévention et la protection au travail (CPPT), organe de concertation de l'entreprise. | `TypeOrgane.ComitePpt` | Affilies |
| Clean architecture | Architecture en couches concentriques où les dépendances pointent vers le domaine métier. | — (projets `*.Domain`, `*.Application`, `*.Adapters` par service) | Plateforme |
| Clé racine | Clé non exportable du coffre de la zone, qui enveloppe les clés de données. | — | Plateforme |
| Conteneur (OCI) | Unité d'exécution standardisée qui regroupe un service et ses dépendances. | — | Plateforme |
| Contexte délimité | Périmètre métier cohérent (Domain-Driven Design), correspondant en principe à un microservice. | — (un dossier `src/Services/<Service>` par contexte) | Plateforme |
| CPAP | Conseiller en prévention aspects psychosociaux. | `Roles.Cpap` (`Sepp.BuildingBlocks.Application`, `Security/Security.cs`) | Plateforme |
| CPMT | Conseiller en prévention-médecin du travail. | `Roles.Cpmt` (`Sepp.BuildingBlocks.Application`, `Security/Security.cs`) | Plateforme |
| CQRS | Séparation des modèles d'écriture (commandes) et de lecture (requêtes). | `ICommandHandler`, `IQueryHandler` (`Sepp.BuildingBlocks.Application`) | Plateforme |
| DIMONA | Déclaration immédiate de l'emploi : entrées et sorties des travailleurs, échangées via la BCSS. | `EnregistrerEntreeDimonaHandler`, `EnregistrerSortieDimonaHandler`, `DimonaAdaptateurReel` | Integrations |
| DmfA | Déclaration multifonctionnelle trimestrielle adressée à l'ONSS, échangée via la BCSS. | — | Integrations |
| Fedris | Agence fédérale des risques professionnels, partenaire du SEPP pour les maladies professionnelles et les demandes d'information. | `DemandeInformationFedris` | SurveillanceMedicale |
| Instance de zone | Déploiement d'un même service dans une zone de sensibilité (standard, médicale, psychosociale). | — | Plateforme |
| Kubernetes | Orchestrateur de conteneurs (déploiement, mise à l'échelle, réseau, redémarrage). Non retenu : l'ADR 0003 choisit Azure Container Apps. | — | Plateforme |
| NISS | Numéro d'identification à la sécurité sociale, identifiant d'une personne physique. | `NissIndex` | Personnes |
| OIDC | OpenID Connect : protocole standard d'authentification (ADR 0005 : Entra ID et CSAM). | `AddJwtBearer`, `AddAuthentication` | Plateforme |
| Outbox | Table dans laquelle un service écrit ses événements, dans la même transaction que ses données, avant publication. | `OutboxMessage`, `OutboxProcessor` (`Sepp.BuildingBlocks.Infrastructure/Messaging`) | Plateforme |
| Poste de sécurité | Poste dont l'occupation peut mettre en danger d'autres travailleurs (conduite, machines, armes). | `CategorieRisque.PosteSecurite`, `TypeListeNominative.PosteSecurite` | PostesRisques |
| Poste de vigilance | Poste impliquant une surveillance permanente d'une installation, où un défaut de vigilance met autrui en danger. | `CategorieRisque.PosteVigilance`, `TypeListeNominative.PosteVigilance` | PostesRisques |
| Saga | Processus long réparti entre plusieurs services, avec des étapes de compensation en cas d'échec. | `ProcessusReprise` (agrégat de la saga d'examen de reprise, voir `docs/architecture/saga-examen-reprise.md`) | Obligations |
| SEPP | Service externe de prévention et de protection au travail : le SEPP est le client du présent logiciel. Définition à confirmer (note 2). | Espace de noms `Sepp.*` | Plateforme (transverse) |
| SIPP | Service interne de prévention et de protection au travail. Définition à confirmer (note 2). | `Roles.Sipp` (`Sepp.BuildingBlocks.Application`, `Security/Security.cs`) | Plateforme |
| Unité de prévention | Unité de mesure des prestations d'un SEPP, issue de la cotisation forfaitaire. Ne pas confondre avec `UniteEtablissement` (unité d'établissement BCE, Affilies). Définition à confirmer (note 2). | — | À confirmer (lot 5) |

## Points à vérifier

1. **BCE, BCSS et CBE.** Dans le tableau du §17 (p. 35), les définitions « Banque-Carrefour des Entreprises »,
   « Banque Carrefour de la Sécurité Sociale » et « Contrôle du bien-être au travail » ne sont pas alignées sur leurs
   termes : la mise en page décale les lignes. Le §12 du même document associe BCE à la Banque-Carrefour des
   Entreprises et BCSS à la Banque Carrefour de la Sécurité Sociale, alors que le §17 semble les inverser. Le §6 cite
   « Inspection (CBE) ». Le glossaire ne choisit pas de version : les trois définitions restent à confirmer auprès de
   l'auteur du cahier des charges.
2. **SEPP, SIPP et Unité de prévention.** Le même décalage de mise en page affecte probablement ces trois lignes
   (la définition « service externe » semble alignée sur SIPP et « service interne » sur l'unité de prévention). Les
   définitions ci-dessus suivent le sens usuel des sigles ; à confirmer dans le cahier.
3. **Kubernetes.** Le §17 le cite comme orchestrateur, mais la cible retenue est Azure Container Apps (ADR 0003).
   Le terme est conservé pour la compréhension du cahier, sans correspondance dans le code.
