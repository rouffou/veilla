# Authentification forte (NF-01)

> NF-01 (cahier des charges §13.1) : authentification forte (MFA) pour tous les internes ; eID ou itsme obligatoire pour signer
> et pour accéder aux données médicales.

Deux niveaux de contrôle :

1. **Fournisseur d'identité** : Entra ID impose la MFA à tous les internes par accès conditionnel, et la fédération CSAM fournit
   l'eID ou itsme. En local, Keycloak permet la montée en niveau à la demande (`acr`).
2. **Services** : la politique `AuthenticationStrength` (BuildingBlocks, `Sepp.BuildingBlocks.Web`) vérifie dans le jeton que
   l'authentification est forte avant une signature ou un accès aux dossiers de santé et psychosociaux. Un jeton trop faible
   reçoit un défi de montée en niveau ([RFC 9470](https://www.rfc-editor.org/rfc/rfc9470)).

## Routes protégées

| Route | Mécanisme |
|---|---|
| Toute route `RequirePermission(dossier-sante:*)` ou `RequirePermission(dossier-psy:*)` | Automatique : la politique de la permission inclut l'exigence (`AuthenticationStrength.IsRequiredFor`), y compris pour les routes à venir du service psychosocial |
| `POST /api/v1/decisions/{id}/signature` (Surveillance médicale, SAN-32) | Explicite : `.RequireAuthenticationStrength()` en plus de `decision:ecrire` |

Deux permissions de ces zones sont exemptées, car elles n'ouvrent aucun contenu médical à une personne interne :
`dossier-sante:copie-demander` (le travailleur dépose sa demande de copie depuis le portail, authentifié par CSAM en Azure) et
`dossier-sante:transmettre-documents` (compte technique du service Documents, client credentials, sans utilisateur).

Les droits eux-mêmes (`Security.cs`, matrice §3.3) ne changent pas : l'authentification forte s'ajoute à la permission. Un profil
sans la permission reçoit toujours 403, jamais un défi (monter en niveau n'y changerait rien).

## Configuration (`Authentication:Strength`)

| Clé | Rôle | Local (Development, Test, Testing) | Azure (injecté par Terraform) |
|---|---|---|---|
| `Mode` | `Log` : accès accordé et insuffisance journalisée ; `Enforce` : 401 ; `Disabled` : aucun contrôle | `Log` par défaut | `Enforce`, imposé (non paramétrable) |
| `AcceptedAcr` | Valeurs acceptées du claim `acr` | `mfa` | — |
| `AcceptedAmr` | Valeurs acceptées du claim `amr` (une seule suffit) | `otp`, `mfa` | `mfa` (`var.authentication_strength.accepted_amr`) |
| `AcceptedAuthenticationContexts` | Valeurs acceptées du claim `acrs` (contexte d'authentification Entra ID, `c1` à `c99`) | — | `c1` (`var.authentication_strength.accepted_authentication_contexts`) |
| `ChallengeAcrValues` | `acr_values` annoncé dans le défi | `mfa` | — (Entra utilise le paramètre `claims`) |
| `MaxAge` | Ancienneté maximale de l'authentification (claim `auth_time`), par ex. `00:15:00` | non fixée | non fixée (**à valider**) |

Sans `Mode`, le service est en `Log` en Development, Test et Testing, et en `Enforce` partout ailleurs. Les valeurs locales ne
s'appliquent que hors Azure et seulement si aucune valeur n'est configurée. En `Enforce` sans aucune valeur acceptée, ou avec un
contexte qui n'est pas `c1`…`c99`, le service **refuse de démarrer** (validation des options au démarrage).

Un jeton est fort s'il porte au moins une valeur acceptée parmi `acr`, `amr` ou `acrs` (puis, si `MaxAge` est fixé, un
`auth_time` assez récent). En mode `Log`, le journal ne contient que la route et les niveaux présentés (`acr`, `amr`), jamais de
donnée du dossier (NF-04).

### Réponse en cas de niveau insuffisant

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="insufficient_user_authentication", error_description="A stronger authentication is required", acr_values="mfa"
Content-Type: application/problem+json

{"title":"authentification.niveau-insuffisant","status":401,"code":"authentification.niveau-insuffisant","acr_values":"mfa", ...}
```

En Azure, le défi porte en plus `claims="<base64>"`, demande du contexte d'authentification au format Entra ID
(`{"access_token":{"acrs":{"essential":true,"value":"c1"}}}`) que MSAL transmet tel quel (`claims` de la nouvelle demande de
jeton). `WWW-Authenticate` est exposé par le CORS des BFF ; le relais du BFF interne le transmet tel quel.

**Côté client (à faire, front)** : sur ce 401, relancer l'autorisation avec `acr_values` (Keycloak) ou `claims` (MSAL), puis
rejouer la requête. Tant que le front ne le gère pas, garder le mode `Log` en local.

## Keycloak local

Choix : **montée en niveau à la demande, sans OTP obligatoire à la connexion**, plus un utilisateur de démonstration dont l'OTP
est déjà configuré. Un OTP conditionnel par rôle médical aurait imposé l'enrôlement d'un OTP à `cpmt`, `infirmier`, `cpap`…
dès la première connexion et, dans le flux mot de passe, un paramètre `totp` à l'amorçage des données de démo : la démo
perdrait sa fluidité. Le contrôle qui protège réellement les données est celui des services ; Entra ID impose la MFA à la
connexion en Azure.

- **Niveaux** (`acr.loa.map` du realm) : `password` = 1, `mfa` = 2. Les mappers `acr` et `amr` du scope `sepp-api` émettent
  `acr` et `amr` dans le jeton d'accès.
- **Flux navigateur `veilla-navigateur`** : cookie, redirection éventuelle vers un fournisseur d'identité, puis niveau 1
  (identifiant et mot de passe, `amr=pwd`) et, si le niveau 2 est demandé (`acr_values=mfa`), OTP (`amr=otp`, enrôlement à la
  première demande). Niveau 2 valable une heure (`loa-max-age = 3600`, **à valider**).
- **Flux mot de passe `veilla-acces-direct`** (client `veilla-dev-cli`) : identifiant, mot de passe, puis OTP seulement si
  l'utilisateur en a configuré un. Les comptes de test existants n'en ont pas : le flux mot de passe reste inchangé pour eux.
  Keycloak n'évalue pas les niveaux dans ce flux (`acr=password`) ; `amr=["pwd","otp"]` suffit à rendre le jeton fort en local.
- **Utilisateur `cpmt.mfa`** : OTP de démonstration, secret **fictif et public**, réservé au realm local
  (voir [`deploy/local/README.md`](../../deploy/local/README.md#authentification-forte-nf-01)).

Vérification (2026-11, Keycloak 26.4) : comptes de test en flux mot de passe → `acr=password`, `amr=["pwd"]` ;
`cpmt.mfa` sans `totp` → refus, avec `totp` → `amr=["pwd","otp"]` ; navigateur `acr_values=mfa` → page OTP puis
`acr=mfa`, `amr=["pwd","otp"]` (pour `cpmt`, enrôlement OTP proposé). Service en `Enforce` : lecture du dossier et signature
→ 401 avec le défi pour un jeton `pwd`, signature acceptée avec `pwd`+`otp`.

## Entra ID (Azure)

Les jetons d'accès v2.0 d'Entra ID ne portent pas `amr` de façon fiable ; le mécanisme retenu est le **contexte
d'authentification** (claim `acrs`), lié à une stratégie d'accès conditionnel. `amr` contenant `mfa` reste accepté (jetons v1).

1. **Contexte d'authentification** : Entra ID > Protection > Accès conditionnel > Contexte d'authentification : créer `c1`
   « Données médicales et signature (NF-01) », cocher « Publier sur les applications ».
2. **Stratégie d'accès conditionnel** ciblant le contexte `c1` (Ressources cibles > Contexte d'authentification), pour les groupes
   `sepp-role-*` internes : accorder l'accès en exigeant une **force d'authentification** MFA résistante à l'hameçonnage, ou la
   fédération CSAM (eID / itsme) pour les profils qui signent (**à valider** : force retenue, profils concernés, fréquence de
   connexion).
3. **Stratégie de base** : MFA exigée pour toute connexion à `sepp-api` (tous les internes, NF-01).
4. **Application cliente** (application interne, MSAL Angular) : sur un 401 `insufficient_user_authentication`, rejouer
   `acquireToken` avec le paramètre `claims` reçu ; l'utilisateur satisfait la stratégie et le nouveau jeton porte `acrs: ["c1"]`.
5. **Vérifier** sur un jeton réel (jwt.ms) la présence de `acrs` et la version du jeton (`accessTokenAcceptedVersion` du manifeste
   de `sepp-api`) ; ajuster `authentication_strength` dans `terraform.tfvars` si un autre identifiant de contexte est retenu.

Terraform (`infra/main.tf`) injecte `Authentication__Strength__Mode = "Enforce"`,
`Authentication__Strength__AcceptedAmr__<i>` et `Authentication__Strength__AcceptedAuthenticationContexts__<i>` à chaque service
depuis `var.authentication_strength` (défaut `mfa` et `c1`). Une liste vide, un contexte hors `c1`…`c99` ou une méthode faible
(`pwd`) sont refusés par la validation de la variable ; `ConfigurationTerraformTests` vérifie l'injection.

## Fédération CSAM (eID / itsme)

La configuration réelle se fait hors dépôt (tenant Entra, contrat CSAM ; suivi dans #282). Étapes :

1. **Adhésion** : demander l'intégration au Service d'authentification fédéral (FAS, BOSA / CSAM) pour l'environnement de test
   (intégration) puis de production ; fournir les URI de redirection et obtenir l'identifiant client et le secret (ou le
   certificat). Le secret va dans Key Vault, jamais dans le dépôt.
2. **Niveaux** : exiger un niveau d'assurance élevé (eID avec lecteur ou itsme ; valeurs `acr` du FAS, par ex.
   `urn:be:fedict:iam:fas:Level500` / `Level450`, **à valider** auprès de BOSA) pour la signature et la zone médicale.
3. **Internes** : ajouter le FAS comme fournisseur d'identité OIDC externe dans Entra ID (fédération B2B / « Identity providers »
   ou Entra External ID), ou l'utiliser comme méthode exigée par la force d'authentification de la stratégie `c1` ; vérifier que le
   jeton final d'Entra porte `acrs: ["c1"]` après une connexion eID / itsme.
4. **Externes** (employeur, SIPP, travailleur, POR-01, POR-10) : fédération CSAM de l'annuaire externe, avec correspondance vers le
   claim `roles` et les claims `affilie_id` / `personne_id` (ADR 0005).
5. **Keycloak local** : la redirection vers un fournisseur d'identité est déjà prévue dans le flux `veilla-navigateur`
   (`identity-provider-redirector`) ; on peut y brancher l'environnement d'intégration du FAS, sans secret versionné.
6. **Recette** : connexion eID et itsme, défi 401 puis montée en niveau, signature acceptée ; scénario rattaché à NF-01 (REC-01).
