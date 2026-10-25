# Registre des sous-traitants et services tiers

Ce registre liste les fournisseurs et services tiers qui hébergent, traitent ou transmettent des données du logiciel
SEPP, ainsi que les outils exécutés en intégration continue. Il répond à l'exigence NF-13 (hébergement dans l'Union
européenne) et à la liste contractualisée des sous-traitants suivie dans le ticket #165 (NF-13).

Principes de lecture :

- **Statut « à confirmer »** : l'information n'est pas dans le dépôt (contrat, version du DPA, localisation exacte,
  opérateur). Rien n'est supposé ; chaque point doit être vérifié par le responsable de traitement avant le premier
  déploiement en production.
- **Déploiement réel** : seule la configuration du dépôt (Terraform, code, workflows) fait foi. Aucun `terraform plan`
  ni `apply` réel n'a encore été exécuté : la vérification en réel est suivie dans #282.
- Les données de santé et psychosociales ne sont traitées que dans les zones médicale et psychosociale (ADR 0003,
  ARC-06) ; les données personnelles désignent les données des affiliés, des travailleurs et des employeurs.

## 1. Hébergement et exécution (Microsoft Azure)

Le contrat Azure, son DPA et sa version acceptée par le responsable de traitement ne figurent pas dans le dépôt :
la colonne « Base contractuelle » indique donc « à confirmer » pour chaque service.

| Fournisseur | Service utilisé | Finalité | Localisation des données | Données traitées | Base contractuelle | Statut |
|---|---|---|---|---|---|---|
| Microsoft (Azure) | Container Apps (environnements par zone, microservices, jobs de migration) | Exécution des services métier | `location` = `belgiumcentral` par défaut (`infra/variables.tf`) ; `dr_location` = `swedencentral` si `enable_dr_replication` | Toutes les données métier transitant par les services : données personnelles ; **données de santé** (zone médicale : surveillance médicale, réintégration) ; **données psychosociales** (zone psychosociale) | DPA Microsoft : à confirmer | Configuré (Terraform) ; contrat et localisation effective à confirmer (#282) |
| Microsoft (Azure) | Azure Container Registry (Premium, images des services) | Stockage des images signées et de leurs attestations SBOM | Région principale ; géo-réplication vers `dr_location` si `enable_dr_replication` | Images sans donnée métier ; étiquettes, digests, attestations | DPA Microsoft : à confirmer | Configuré ; à confirmer |
| Microsoft (Azure) | PostgreSQL Flexible Server (un serveur par zone, une base par service) | Persistance des données métier | Région principale (`belgiumcentral`) ; sauvegardes selon la configuration Azure (à confirmer) | **Données personnelles, de santé et psychosociales** selon la zone ; données chiffrées au niveau applicatif lorsque le service le prévoit (ARC-45) | DPA Microsoft : à confirmer | Configuré ; à confirmer |
| Microsoft (Azure) | Key Vault (par zone) et Managed HSM (zones sensibles, `managed_hsm_enabled`) | Secrets, clés de chiffrement, clé racine `documents-kek` | Région principale | Secrets techniques et clés ; aucune donnée métier en clair | DPA Microsoft : à confirmer | Configuré ; Managed HSM selon l'environnement ; à confirmer |
| Microsoft (Azure) | Service Bus Premium (topics, subscriptions) | Messagerie entre services (événements d'intégration) | Région principale | Événements d'intégration : ARC-06 interdit les données médicales, psychosociales et d'identité ; données personnelles à minimiser | DPA Microsoft : à confirmer | Configuré ; à confirmer |
| Microsoft (Azure) | Storage Account Blob (documents, stockage WORM, `ZRS`) | Stockage des documents (WORM, rétention de 10 ans en prod) | Région principale ; une instance par zone | **Documents** pouvant contenir des données personnelles, de santé et psychosociales | DPA Microsoft : à confirmer | Configuré ; à confirmer |
| Microsoft (Azure) | Storage Account de l'état Terraform (backend `azurerm`) | Stockage de l'état Terraform par environnement | Région à confirmer (compte fourni à l'initialisation, `infra/README.md`) | État d'infrastructure (identifiants de ressources) | DPA Microsoft : à confirmer | Hors dépôt ; à confirmer |
| Microsoft (Azure) | Log Analytics, Application Insights, Azure Monitor (alertes, groupe d'actions, workbook) | Journaux, traces, métriques, alertes e-mail vers l'exploitation (`alert_email`) | Région principale (espace de travail dans `location`) | Journaux techniques ; ARC-06 interdit les données médicales ou psychosociales dans les journaux ; **adresses e-mail** des destinataires d'alerte | DPA Microsoft : à confirmer | Configuré ; contenu réel des journaux à confirmer |
| Microsoft (Azure) | Application Gateway WAF_v2 (IP publique, TLS) et API Management interne (`Premium_3` en prod) | Point d'entrée Internet, WAF, passerelle vers les BFF | Région principale ; IP publique Azure | Trafic HTTPS des utilisateurs (données personnelles, de santé et psychosociales en transit ; TLS de bout en bout) | DPA Microsoft : à confirmer | Configuré ; à confirmer |
| Microsoft (Azure) | Réseau virtuel, Private Link, DNS privé, NSG | Isolation réseau par zone | Région principale | Métadonnées réseau uniquement | DPA Microsoft : à confirmer | Configuré |
| Microsoft (Azure) | Azure Policy, Azure Resource Manager (plan de contrôle) | Gouvernance (registres autorisés, accès public, localisations) | Plan de contrôle Azure (sans donnée métier) | Journaux d'administration (identités des opérateurs) | DPA Microsoft : à confirmer | Configuré ; à confirmer |
| Microsoft (Entra ID) | Authentification des internes et émission des jetons (`Authentication:Authority`) ; identités managées | Authentification, autorisation, identités de services | Service global Microsoft (localisation des données d'annuaire : à confirmer) | Identifiants et attributs des utilisateurs internes et des comptes techniques | DPA Microsoft : à confirmer | Configuré (identifiants de tenant en exemple) ; localisation à confirmer |

Remarque sur la localisation : Entra ID, les zones DNS privées et d'autres ressources « globales » ne sont pas
forcément couvertes par la stratégie de localisation décrite plus bas. Leur localisation effective est à confirmer.

## 2. Forge logicielle et chaîne d'intégration continue

| Fournisseur | Service utilisé | Finalité | Localisation des données | Données traitées | Base contractuelle | Statut |
|---|---|---|---|---|---|---|
| GitHub (Microsoft) | Dépôt `rouffou/veilla` (public), pull requests, issues, règle de protection de `main` | Gestion du code source et des changements | Localisation de l'hébergement GitHub : à confirmer | Code source public ; noms et identifiants des contributeurs ; contenu des issues et des PR (aucune donnée personnelle attendue) | Conditions GitHub et DPA GitHub : à confirmer | En service ; à confirmer |
| GitHub (Microsoft) | GitHub Actions (exécution des workflows sur les runners GitHub, artefacts, journaux) | Compilation, tests, analyse, construction et publication des images | Localisation des runners : à confirmer | Code, journaux de build, rapports Trivy archivés en artefacts, secrets GitHub (stockés chiffrés par GitHub) ; aucune donnée de production attendue | Conditions GitHub et DPA GitHub : à confirmer | En service ; à confirmer |
| GitHub (Microsoft) | Dependabot (mises à jour de dépendances et alertes de sécurité) | Maintien à jour des paquets NuGet, npm et GitHub Actions | Localisation : à confirmer | Manifestes de dépendances (`.github/dependabot.yml`) | Conditions GitHub : à confirmer | En service ; à confirmer |
| GitHub (Microsoft) | CodeQL (analyse statique de code) | Analyse de sécurité du code (CTR-20) | Localisation : à confirmer | Code source | Conditions GitHub : à confirmer | En service ; à confirmer |
| GitHub (Microsoft) | Jetons OIDC de GitHub Actions vers Azure (`azure/login`) | Authentification sans secret de la CI/CD vers Azure et ACR | Sans donnée stockée | Jetons éphémères (identité du workflow) | Conditions GitHub et Azure : à confirmer | En service ; à confirmer |
| Sigstore (instance publique Fulcio et Rekor, communauté OpenSSF) | Signature sans clé des images (`cosign sign`) et journal de transparence public Rekor | Preuve d'origine des images (CTR-06) | Infrastructure publique Sigstore : localisation à confirmer (pas de garantie UE) | **Publié publiquement dans Rekor** : empreinte de l'image, certificat éphémère et identité OIDC du workflow (dépôt, référence, nom du workflow), horodatage. Ces métadonnées sont **publiques et non effaçables** ; aucune donnée métier, aucune donnée personnelle attendue | Service public sans DPA : à confirmer (conditions d'utilisation Sigstore) | En service ; à confirmer |
| Sigstore / cosign | Outil `cosign` (installé par `sigstore/cosign-installer`) | Signature et attestation SBOM des images | Exécuté sur les runners GitHub ; attestation écrite dans l'ACR | Aucune transmission de donnée métier ; téléchargement du binaire depuis GitHub | Licence open source ; pas de DPA | En service ; à confirmer si l'attestation SBOM complète est publiée dans Rekor ou seulement dans l'ACR |
| Aqua Security | Trivy (analyse des vulnérabilités, secrets et licences des images, `aquasecurity/trivy-action`) | Contrôle des images avant publication (CTR-06) | Exécuté sur les runners GitHub ; base de vulnérabilités téléchargée depuis un dépôt public (à confirmer) | Aucune donnée métier transmise au fournisseur, sous réserve de confirmation de la télémétrie (à confirmer) ; rapports archivés dans GitHub | Licence open source ; pas de DPA | En service ; télémétrie et source de la base à confirmer |
| Anchore | Syft (génération de la SBOM SPDX, `anchore/sbom-action`) | Inventaire des composants des images | Exécuté localement sur les runners | Aucune transmission | Licence open source ; pas de DPA | En service |
| HashiCorp | Terraform (CLI) et téléchargement des providers (`registry.terraform.io`) | Exécution de l'infrastructure en Terraform (ARC-51) | Registre des providers : localisation à confirmer | Aucune donnée métier ; versions et adresses IP des téléchargements | Licence open source ; pas de DPA | En service ; à confirmer |

## 3. Communications sortantes et services d'échange

Ces canaux sont définis par des ports et des adaptateurs. Les adaptateurs « réels » ne sont **pas raccordés** à un
fournisseur dans le dépôt : en dehors de la production, `Simulateur` est imposé (ARC-05, NF-14, `GardeAdaptateurs`).

| Fournisseur | Service utilisé | Finalité | Localisation des données | Données traitées | Base contractuelle | Statut |
|---|---|---|---|---|---|---|
| À désigner (serveur SMTP) | Envoi d'e-mails par SMTP (MailKit, `Communications:Smtp`) | Notifications aux employeurs et travailleurs ; message générique sans donnée sensible | Hôte SMTP non configuré dans le dépôt : à désigner | **Adresse e-mail** du destinataire, nom de l'expéditeur, notification générique ; mot de passe SMTP dans Key Vault | DPA du fournisseur : à confirmer | Adaptateur réel implémenté ; **fournisseur non désigné** |
| À désigner | Envoi de SMS | Notifications par SMS | Non défini | **Numéro de téléphone** du destinataire ; contenu du message | DPA du fournisseur : à confirmer | **Non raccordé** (`EnvoiSmsNonRaccorde`) |
| À désigner | Courrier postal | Envoi de courriers | Non défini | **Nom et adresse postale**, contenu du courrier (données personnelles, pouvant être de santé selon le document) | DPA du fournisseur : à confirmer | **Non raccordé** (`EnvoiCourrierNonRaccorde`) |
| À désigner | Recommandé électronique | Envoi de notifications recommandées | Non défini | Identité, adresse électronique, contenu de la notification | DPA du fournisseur : à confirmer | **Non raccordé** (`EnvoiRecommandeElectroniqueNonRaccorde`) |
| eBox (opérateur à confirmer) | eBox entreprise et eBox citoyen | Dépôt des notifications dans les boîtes électroniques sécurisées | Localisation à confirmer (plateforme belge à confirmer) | Identité du destinataire, documents et notifications, potentiellement données de santé | Convention eBox : à confirmer | **Non raccordé** (`EBoxEntrepriseNonRaccordee`, `EBoxCitoyenNonRaccordee`) |

## 4. Services publics et plateformes d'échange

Ces services ne sont pas raccordés : les adaptateurs réels (`src/Services/Integrations/src/Sepp.Integrations.Adapters/External/AdaptateursReels.cs`)
lèvent `NotImplementedException`. Chaque raccordement exige une délibération du Comité de sécurité de l'information
(INT-03) et une documentation du service. Les données décrites sont celles attendues par le port ; elles ne sont pas
encore reçues.

| Fournisseur | Service utilisé | Finalité | Localisation des données | Données traitées | Base contractuelle | Statut |
|---|---|---|---|---|---|---|
| SPF Économie (Banque-Carrefour des Entreprises) | Service d'accès aux données des entreprises (BCE) | Vérification et enrichissement des données d'entreprise (affiliés) | Localisation : à confirmer (service public belge) | Numéro BCE, dénomination, adresse de l'entreprise (données d'entreprise ; données personnelles des dirigeants possibles) | Délibération CSI et contrat d'accès : à confirmer | **Non raccordé** (`BceAdaptateurReel`) ; à confirmer |
| BCSS (Banque-Carrefour de la sécurité sociale) | Déclarations DIMONA et DmfA | Suivi des déclarations de travail de l'employeur | Localisation : à confirmer (service public belge) | **NISS** et données d'emploi des travailleurs (données personnelles) | Délibération CSI (INT-03), certificat d'accès, contrat : à confirmer | **Non raccordé** (`DimonaAdaptateurReel`) |
| BCSS (Banque-Carrefour de la sécurité sociale) | Registre national (consultation de l'identité et mutations : adresse, nom, prénom, langue, décès) | Mise à jour des identités des personnes | Localisation : à confirmer (service public belge) | **NISS**, identité et adresse des personnes (données personnelles) | Délibération CSI (INT-03), certificat d'accès, contrat : à confirmer | **Non raccordé** (`RegistreNationalAdaptateurReel`) |
| Plateforme eHealth (avec mutualités et INAMI) | Notifications d'incapacité de travail | Suivi des incapacités de travail pour la réintégration | Localisation : à confirmer (service public belge) | Référence, travailleur, employeur, émetteur et dates de l'incapacité ; **données de santé** (aucun diagnostic n'est retenu par l'adaptateur) | Délibération CSI (INT-03), certificat eHealth, contrat : à confirmer | **Non raccordé** (`IncapacitesEHealthAdaptateurReel`) |
| SPF Stratégie et Appui (CSAM) et itsme | Fédération d'identité OIDC pour employeurs et travailleurs (POR-01, POR-10, ADR 0005) | Authentification des employeurs et des travailleurs | Localisation : à confirmer | Identifiants de connexion et attributs d'identité (NISS éventuel : à confirmer) | Conventions CSAM et itsme : à confirmer | **À confirmer** : référencée dans la configuration et le code (émetteur des portails, revendications du BFF employeur) ; la fédération elle-même n'est pas configurée dans le dépôt |
| Prestataire de services de confiance qualifié à désigner (eID, itsme) | Signature électronique qualifiée des documents (service Documents, `SignatureQualifieeReelle`) | Signature des documents à valeur probante | Localisation : à confirmer | Documents signés (données personnelles, de santé et psychosociales selon le document) ; identité du signataire | Contrat avec le prestataire eIDAS : à confirmer | **Non raccordé** : prestataire à désigner (`src/Services/Documents/src/Sepp.Documents.Adapters/Externe/Adaptateurs.cs`) |

## 5. Hors registre

- **Keycloak** : fournisseur d'identité auto-hébergé, utilisé uniquement en local et en test (ADR 0005). Ce n'est pas un
  sous-traitant tant qu'il n'est pas déployé en production. À confirmer pour les environnements de test hébergés.
- **Paquets NuGet, npm et images de base Docker** : téléchargés pendant la compilation depuis des registres publics.
  Aucune donnée métier n'y est transmise ; leur suivi relève de la gestion des dépendances (CTR-06).

## Contrôle de la localisation

**Policy Azure (NF-13).** Le module `infra/modules/policy` affecte aux groupes de ressources de chaque zone
(`rg-sepp-<env>-<zone>`) la stratégie intégrée « Allowed locations » (identifiant
`e56962a6-4747-49cd-b67b-bf8b01975c4c`), avec `listOfAllowedLocations` égal à `[location, dr_location]`
(`infra/main.tf`, appel `module "policy"`). L'affectation `sepp-localisations-ue` n'envoie pas de paramètre `effect` :
l'effet effectif est celui par défaut de la définition intégrée (à confirmer). La variable `dr_location` n'accepte que
dix régions de l'Union européenne (`infra/variables.tf`). La politique `policy_effect` (`Deny` dans tous les
environnements) couvre les autres stratégies du module.

**Limites connues** : la stratégie ne s'applique qu'aux ressources Azure des groupes de ressources SEPP. Elle ne couvre
ni GitHub, ni Sigstore, ni les services publics et les fournisseurs listés plus haut, ni forcément les ressources
« globales » (Entra ID, DNS privé). Ces points sont traités par les contrats et par les vérifications listées ci-dessous.

**Vérification en réel** : la policy n'a été validée que par `terraform validate`. Son effet réel (refus d'une ressource
hors UE) et la localisation effective des données restent à vérifier sur Azure dans le ticket **#282**.

## Points à confirmer

1. DPA Microsoft accepté pour le compte SEPP, version et date d'acceptation, clauses contractuelles types (services Azure).
2. Localisation effective des données (sauvegardes PostgreSQL, Log Analytics, Storage) et des ressources globales (Entra ID, DNS privé).
3. Localisation des données GitHub, des runners GitHub Actions, de Dependabot et de CodeQL, et conditions de traitement (DPA GitHub).
4. Contenu publié dans le journal Rekor : la seule attestation SBOM ou seulement la signature (version de cosign), et localisation de l'infrastructure Sigstore.
5. Trivy : source de la base de vulnérabilités et absence de télémétrie vers le fournisseur.
6. Fournisseur SMTP, SMS, courrier et recommandé électronique : à désigner, avec DPA.
7. eBox : opérateur, convention et localisation.
8. BCE, BCSS (DIMONA, DmfA, registre national) et eHealth : délibérations CSI, certificats et contrats, avant tout raccordement.
9. CSAM et itsme : conventions, et mise en œuvre de la fédération d'identité.
10. Effet réel de la policy « Allowed locations » (#282).
11. Contenu réel des journaux (ARC-06), notamment les adresses e-mail des alertes.
12. Région de l'état Terraform (compte de stockage fourni à l'initialisation).
