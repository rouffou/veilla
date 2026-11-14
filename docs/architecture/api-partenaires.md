# API partenaires (INT-01)

Statut : conception proposée, à valider par l'architecte. Ticket #152 ; publication dans APIM : #206 ; implémentation du code :
voir #428. Aucune implémentation dans cette livraison.

## Périmètre

Les partenaires (logiciels de gestion de tiers, prestataires, sociétés du groupe) consomment **uniquement des objets métier non
médicaux** : affiliés, personnes de contact, postes et risques non sensibles, obligations et échéances, planification
(créneaux sans motif), référentiels. Sont exclus, quelle que soit la portée demandée :

- toute donnée des zones médicale et psychosociale (Surveillance médicale, Réintégration, documents des zones `medicale` et
  `psychosociale`) ;
- tout élément d'identité directe ou NISS (ARC-06) ;
- les journaux d'audit, la matrice des droits et l'administration.

Cette exclusion est structurelle (aucune route ne les expose), non une simple règle de portée.

## Architecture

```
Partenaire --client credentials--> Entra ID
Partenaire --jeton--> APIM (produit « Partenaires ») --> BFF Partenaires --> services non médicaux
```

- ARC-43 : seuls les BFF sont joignables par APIM. L'API partenaires est donc portée par un **BFF Partenaires** dédié
  (`src/Bff/Partenaires`), sur le modèle du BFF interne (ADR 0013) : relais d'une liste fermée de routes non médicales, sans
  base de données ni logique métier. Il ne relaie jamais vers les services des zones médicale et psychosociale.
- Un contrat OpenAPI propre au BFF (versionné `docs/api/openapi/bff-partenaires.json`, protégé par le test de contrat) est la
  seule définition publiée à APIM ; les contrats des services restent internes.
- Versionnement par chemin (`/partenaires/v1/...`) ; une rupture crée `v2` et `v1` reste servie pendant la période de
  dépréciation annoncée (le test de contrat signale les ruptures).

## Authentification : clients OAuth2 en client credentials

- Un **enregistrement d'application Entra par partenaire** (jamais partagé entre partenaires), rôle d'application attribué
  par l'administrateur après contrat. Flux `client_credentials` uniquement ; pas de flux utilisateur.
- Authentification du client par **certificat** (préférée) ou secret à rotation courte ; aucun secret dans le dépôt, les
  secrets sont dans le coffre de l'environnement et communiqués par canal sécurisé.
- Audience dédiée `api://sepp-partenaires` (distincte de `sepp-api` des internes, de sorte qu'un jeton partenaire soit refusé
  par les services et par les autres BFF, et inversement).
- Le BFF Partenaires valide le jeton (émetteur Entra, audience, expiration) en plus d'APIM (`validate-jwt`) : défense en
  profondeur ; il exige l'absence de rôles internes et la présence d'un rôle d'application partenaire.
- Le schéma OAuth2 du document OpenAPI (client credentials) est celui du socle (`SecuriteOpenApi`), configuré par
  `Authentication:*` ; ici `Authentication:Scopes` annonce les portées ci-dessous.

## Portées (rôles d'application Entra)

Lecture seule en première version ; une portée d'écriture exige une décision explicite.

| Portée | Contenu | Remarque |
|---|---|---|
| `partenaires.affilies.lire` | affiliés (entreprises) et sites | sans donnée de contact nominative |
| `partenaires.obligations.lire` | obligations et échéances de l'affilié | |
| `partenaires.postes.lire` | postes de travail et catégories de risques non sensibles | pas de surveillance de santé |
| `partenaires.planification.lire` | créneaux et rendez-vous, sans motif | |
| `partenaires.referentiels.lire` | référentiels publics, calendrier, paramètres légaux | |

Les portées sont ajoutées à `docs/securite/entra-app-roles.json` et à la matrice des droits (`matrice-droits.csv`, revue
trimestrielle) comme profil « partenaire » ; chaque partenaire reçoit le **sous-ensemble minimal** contractualisé.

## Cloisonnement par partenaire

Un partenaire n'accède qu'aux objets de **ses affiliés** : le BFF dérive la liste d'affiliés autorisés de l'identifiant du
client (`azp`/`appid`) via une table de mandats tenue par le service Affiliés (partenaire → affiliés, période de validité,
portées). Toute requête hors mandat reçoit 404 (pas 403, pour ne pas révéler l'existence). Aucune opération en masse
inter-affiliés.

## Politique APIM (produit « Partenaires »)

- Produit dédié, abonnement par partenaire (clé d'abonnement APIM en plus du jeton, pour l'identification et les quotas).
- `validate-jwt` (émetteur, audience `api://sepp-partenaires`, rôle d'application requis par opération).
- `rate-limit-by-key` par client (valeurs initiales **à valider**, ex. 600 appels/minute) et `quota-by-key` journalier.
- Restriction d'IP optionnelle selon contrat ; journaux de diffusion vers Log Analytics ; identifiant de corrélation
  `X-Correlation-Id` propagé jusqu'aux services (ARC-47).
- Réponses d'erreur en ProblemDetails, sans détail interne.

## Audit, journalisation et RGPD

- Chaque appel est tracé (client, affilié, opération, statut, corrélation) sans corps de requête ni de réponse ; accès
  consultables dans Audit.
- Le registre des sous-traitants et la base légale du partage sont à documenter par partenaire avant ouverture du client
  (`docs/conformite/`) ; DPA signé en prérequis.
- Révocation immédiate : désactivation du client Entra, de l'abonnement APIM et du mandat.

## Cycle de vie d'un partenaire

1. Contrat et DPA ; périmètre et portées arrêtés.
2. Création de l'application Entra (certificat), attribution du rôle, mandat dans Affiliés, abonnement APIM.
3. Environnement de recette d'abord (données fictives), puis production.
4. Revue trimestrielle des portées, mandats et secrets avec la revue des droits.

## Points à valider

- Liste exacte des objets et champs exposés par portée (revue avec le métier et la protection des données).
- Authentification par certificat obligatoire ou secret toléré.
- Quotas et limites de débit.
- Modèle de mandat partenaire → affiliés (propriétaire : Affiliés).
- Besoin d'écriture (non prévu en v1).

## Suite

Implémentation : BFF Partenaires, table de mandats, portées Entra et produit APIM dans le ticket de suite (#428) ;
publication APIM dans #206.
