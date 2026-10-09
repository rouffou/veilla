# Service Intégrations

Couche anti-corruption vers les systèmes externes (cahier des charges §12, ARC-05, §14.3, entités §15.3).
Base `integrations`, port local 5115, espace de noms `Sepp.Integrations`.

> **INT-04 — formats à confirmer.** Les services, versions, schémas de message et modes d'échange exacts de la BCE et
> de la BCSS ne sont pas connus à ce stade : ils seront confirmés auprès de chaque organisme lors de l'analyse détaillée.
> Ce service ne reproduit **aucune** structure de message d'un organisme. Le cœur ne manipule qu'un format canonique
> interne ; chaque adaptateur réel traduira le format de son organisme vers ce format, sans toucher au reste du code.

## Exigences couvertes

| Exigence | Réalisation |
|---|---|
| §12 couche d'intégration découplée, journalisée, rejouable | Un port par flux dans `Application/Externe/Ports.cs` (`IRegistreBce`, `IFluxDimona`, `IRegistreNational`) et un adaptateur par organisme dans `Adapters/External` ; chaque message reçu est journalisé avant traitement et rejouable |
| AFF-20 DIMONA / DmfA via la BCSS | Entrées et sorties → identité au registre national → `POST /api/v1/dimona/entrees` et `POST /api/v1/dimona/{reference}/sortie` du service Personnes (HTTP interne, idempotent sur la référence DIMONA) |
| BCSS identification et mutations | Port `IRegistreNational` + simulateur ; mutation (adresse, nom, prénom, langue, décès) → `IPersonnesClient.AppliquerMutationAsync` → `POST /api/v1/registre-national/mutations` du service Personnes, idempotent sur la référence de mutation |
| BCE | Données d'entreprise et unités d'établissement → table `entreprise_bce` et événement `integrations.donnees-bce-recues.v1` vers Affiliés |
| INT-02 tableau de suivi | Table `journal_flux` ; API volumes par flux et par jour, erreurs et rejets, détail, relance manuelle |
| INT-03 / INT-04 autorisations et certificats | Ci-dessous ; adaptateurs réels en squelette documenté |
| §15.3 correspondances d'identifiants | Table `correspondance_identifiant` : numéro BCE ↔ `affilie_id`, référence DIMONA ↔ `occupation_id` ; jamais de NISS |
| ARC-45, DAT-06 | Charge utile chiffrée (`FieldEncryptor`, colonne `charge_utile_chiffree`), purgée après traitement ; messages d'erreur masqués ; aucun NISS dans les événements, URL ni journaux |

## Architecture des flux

```
organisme ──▶ adaptateur (Simulateur… | …AdaptateurReel) ──▶ format canonique ──▶ journal_flux (Recu, charge chiffrée)
                                                                                     │
                     TraitementEchanges (premier passage ou relance manuelle) ◀──────┘
                         ├── BCE      : entreprise_bce + outbox DonneesBceRecues ──▶ Affiliés (événement)
                         ├── DIMONA   : correspondance BCE → affilié, identité RN ──▶ Personnes (HTTP interne)
                         └── mutation : ──▶ Personnes (HTTP interne, idempotent sur la référence de mutation)
```

- **Réception idempotente** : chaque message a une clé unique par flux (`entree:<référence DIMONA>`,
  `sortie:<référence>:<date de fin>`, `mutation:<référence>`, `bce:<numéro>:<date d'extraction>:<empreinte du contenu>`).
  Un message déjà reçu n'est ni rejournalisé ni retraité ; la position de lecture du flux (`position_flux`) avance dans
  la même transaction que la réception du lot.
- **Traitement idempotent** : Personnes déduplique sur la référence DIMONA, l'entreprise BCE n'est republiée que si ses
  données changent, les correspondances sont écrites par clé. Une relance ne duplique donc rien ; relancer un échange
  déjà traité est sans effet et renvoie son état.
- **Statuts** : `Recu` (en attente), `Traite`, `Rejete` (refus métier : employeur non affilié, données invalides, refus
  400/409 de Personnes) et `EnErreur` (échec technique : indisponibilité, 401/403, 404 d'une sortie arrivée avant son
  entrée). Rejets et erreurs se relancent manuellement après correction ; une exécution du flux ne relance pas
  automatiquement un échange en erreur.
- **Sélection de l'adaptateur** : `Integrations:Adaptateurs:Bce|Dimona|RegistreNational` = `Simulateur` ou `Reel`
  (obligatoire : le service refuse de démarrer sans valeur). `appsettings.json` choisit `Reel`, `appsettings.Development.json`
  les simulateurs. Les adaptateurs réels (`BceAdaptateurReel`, `DimonaAdaptateurReel`, `RegistreNationalAdaptateurReel`)
  lèvent `NotImplementedException` avec la documentation à obtenir (`DocumentationAObtenir`).
- **Simulateurs** (`Adapters/External/Simulateurs.cs`) : données fictives et déterministes, jamais de données réelles
  (NF-14). BCE : toute entreprise au numéro valide existe (une ou deux unités d'établissement), sauf les numéros dont la
  base se termine par `999`. DIMONA : un lot unique pour les trois premiers affiliés connus (entrée d'un salarié, entrée
  d'un étudiant, sortie du salarié) et une entrée pour un employeur non affilié (rejet). Registre national : identité
  fictive cohérente avec tout NISS valide (numéro d'ordre 998 : inconnu) et des mutations fictives (voir *Mutations du
  registre national*).

## Déclenchement

- **Planifié** : `PlanificateurFlux` (BackgroundService) exécute les flux de `Integrations:Planification:Flux`
  (par défaut BCE, DIMONA, registre national) toutes les `Intervalle` (1 h), puis purge les charges utiles échues.
  `Actif` vaut `true` dans `appsettings.json`, `false` en développement et en test. Une seule instance doit porter
  les traitements planifiés (une exécution concurrente reste sûre grâce à la clé unique, mais échoue sur conflit).
- **À la demande** (permission `integrations:administrer`, gestionnaire et administrateur fonctionnel) :
  `POST /api/v1/flux/{bce|dimona|registre-national}/executions` et `POST /api/v1/flux/bce/consultations` (`{ "numeroBce": "…" }`).

## API (`/api/v1`)

| Méthode et chemin | Permission | Rôle |
|---|---|---|
| `GET /flux/journal?flux=&statut=&du=&au=&page=&taille=` | `integrations:administrer` | Échanges journalisés (dates belges incluses ; `statut` répétable) |
| `GET /flux/erreurs?flux=&du=&au=` | idem | Échanges rejetés ou en erreur |
| `GET /flux/volumes?du=&au=` | idem | Volumes par flux, par jour et sur la période (30 jours par défaut, 366 au plus) |
| `GET /flux/journal/{id}` | idem | Détail (jamais la charge utile) |
| `POST /flux/journal/{id}/relance` | idem | Relance manuelle (409 si la charge utile a été purgée) |
| `POST /flux/{flux}/executions`, `POST /flux/bce/consultations` | idem | Lancement à la demande |
| `GET /correspondances?type=&valeur=&identifiantInterne=`, `PUT /correspondances` | idem | Correspondances (saisie idempotente pour la reprise de données) |
| `GET /bce/entreprises/{numeroBce}` | `affilie:lire` ou `integrations:administrer` | Dernières données BCE, adresses des unités d'établissement comprises |

## Données et minimisation

- `journal_flux` : flux, sens, type de message, clé d'idempotence, référence externe (jamais un NISS : une référence
  ayant la forme d'un NISS est journalisée comme illisible), horodatages, statut, nombre d'enregistrements, code et
  message d'erreur (toute suite au format NISS masquée), tentatives, charge utile chiffrée, date de purge.
- **Purge** (`Integrations:Conservation`) : charge utile supprimée `ApresTraitement` (1 jour) après un traitement réussi
  et `ApresEchec` (30 jours) après un rejet ou une erreur ; l'entrée du journal reste. Une fois purgé, un échange ne
  peut plus être relancé : il doit être redemandé à l'organisme.
- Les données BCE (`entreprise_bce`, `unite_etablissement_bce`) sont des données d'entreprise, pas personnelles.

## Événements

- **Publié** : `integrations.donnees-bce-recues.v1` (voir `docs/architecture/evenements.md`). Les adresses ne voyagent
  pas dans l'événement (ARC-06) ; le consommateur les lit par `GET /api/v1/bce/entreprises/{numeroBce}`.
- **Consommés** : `affilies.affilie-cree.v1` et `affilies.affilie-modifie.v1` (correspondance numéro BCE ↔ affilié,
  abonnement `integrations` déjà prévu dans `infra/variables.tf`).

## Appel du service Personnes (AFF-20, DAT-06)

Le NISS ne transite entre Intégrations et Personnes que dans le corps d'appels HTTP internes (`HttpClient` typé
`PersonnesHttpClient`, résilience standard du socle), jamais par événement ni dans une URL.

- **Compte technique** : client OIDC confidentiel avec *service account* (flux *client credentials*, authentification
  `client_secret_post`), portant le rôle realm **`integrations`** (`Roles.Integrations`) et l'audience `sepp-api`.
  Ce rôle n'a que `personne:ecrire` : il alimente DIMONA mais ne lit aucune fiche de travailleur.
- **Configuration** `Integrations:Personnes` : `BaseAddress`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scope`
  (facultatif). Le secret vient de Key Vault en Azure, des secrets utilisateur en local
  (`dotnet user-secrets set Integrations:Personnes:ClientSecret … --project src/Services/Integrations/src/Sepp.Integrations.Infrastructure`).
- **À créer** : le client `veilla-integrations` (service account, rôle `integrations`, mappeur d'audience `sepp-api`)
  dans le realm Keycloak local et son équivalent Entra ID (application avec rôle d'application `integrations`).
  Le realm local n'est pas modifié par ce lot.
- Côté Personnes, le groupe `/api/v1/dimona` n'exige plus `personne:lire` (seulement `personne:ecrire`) : le compte
  technique y accède sans pouvoir lire les fiches (test `Le_compte_technique_integrations_alimente_dimona_sans_lire_les_fiches`).

## Mutations du registre national (AFF-20, AFF-22)

Le flux `registre-national` récupère les mutations par `IRegistreNational.RecupererMutationsAsync`, les journalise
(clé `mutation:<référence>`, charge utile chiffrée) puis les transmet à `POST /api/v1/registre-national/mutations` du
service Personnes (même compte technique `integrations` que DIMONA, permission `personne:ecrire` seule).

- **Format canonique** `MutationRegistreNational` : référence de mutation, NISS, type (`ChangementAdresse`,
  `ChangementNom`, `ChangementPrenom`, `ChangementLangue`, `Deces`), date d'effet (pour un décès : la date du décès) et la
  seule valeur propre au type (adresse, nom, prénom ou langue). Le NISS ne voyage que dans le corps de l'appel.
- **Idempotence** : la référence de mutation est la clé, côté journal comme côté Personnes (table
  `mutation_registre_national`, index unique). Une relance ou un rejeu renvoie `DejaAppliquee` sans rien changer.
- **Issues de Personnes** : `Appliquee`, `DejaAppliquee`, ou `PersonneInconnue` (le NISS n'est pas suivi : échange traité,
  rien à mettre à jour ; une entrée DIMONA ultérieure reprend l'identité courante du registre). Un refus 400/409 (valeur
  absente, référence réutilisée pour une autre mutation, décès incohérent avec la naissance ou une occupation) rejette
  l'échange ; une indisponibilité ou un refus d'accès le met en erreur, relançable.
- **Décès** : Personnes clôt à la date du décès les occupations actives (et leurs affectations) et publie les événements
  existants `personnes.occupation-terminee.v1` / `personnes.affectation-modifiee.v1`, qui déclenchent les recalculs
  d'Obligations. Aucun événement de changement d'identité n'est publié (aucun consommateur n'en a besoin, ARC-06).
- **Simulateur** : pour les trois premiers affiliés connus, cinq mutations déterministes sur les travailleurs fictifs du
  simulateur DIMONA (adresse, nom, prénom et langue du salarié, décès de l'étudiant le 15 juillet 2026), plus un changement
  de nom pour une personne que Personnes ne suit pas. Lancer le flux DIMONA avant le flux registre national.
- **Adaptateur réel** : `RegistreNationalAdaptateurReel` reste un squelette (`NotImplementedException` documentée, INT-04).

## INT-03 — Autorisations et certificats à obtenir

Le contenu des délibérations, des formulaires et des certificats n'est pas reproduit ici : il est fourni par les
organismes et tenu à jour par le SEPP (INT-04 : « le SEPP fournit les délibérations à jour »).

**Comité de sécurité de l'information (CSI)** — avant tout accès réel :

1. BCSS : délibération autorisant le SEPP à recevoir les données DIMONA / DmfA des travailleurs de ses affiliés et à
   consulter les données d'identification (registre national ou registres BCSS) et leurs mutations, pour les finalités
   de la surveillance de la santé et de la gestion des risques ; périmètre des données et durée de conservation selon
   la délibération.
2. Intégration du SEPP dans le réseau de la BCSS selon les modalités qu'elle communique (inscription des personnes
   concernées, le cas échéant au répertoire des références, environnement d'acceptation puis de production).
3. eHealth (Lot 3) : délibération et accès aux services de la plateforme eHealth utilisés (authentification des
   prestataires, eHealthBox…), hors périmètre de ce lot.
4. BCE : conditions d'accès au service retenu auprès du SPF Économie.

**Certificats et configuration** (`Integrations:Bcss`, classe `ConfigurationBcss`) :

- `Adresse` : point d'accès communiqué par la BCSS (acceptation puis production) ;
- `NomCertificatKeyVault` : nom du certificat client stocké dans Azure Key Vault ; le certificat et sa clé privée ne
  figurent jamais dans la configuration, le dépôt ni l'image (CTR-05, CTR-16) ; renouvellement avant expiration suivi
  par l'exploitation ;
- `IdentifiantExpediteur` : identifiant du SEPP communiqué par la BCSS lors de l'intégration.

Le service ne lit pas encore ces valeurs : elles seront consommées par les adaptateurs réels, à implémenter une fois la
documentation des services obtenue.

## Lacunes et suites

- **Mutations du registre national** : traitées (AFF-20, AFF-22) par `POST /api/v1/registre-national/mutations` de
  Personnes, voir la section dédiée. Restent à confirmer auprès de la BCSS (INT-04) : les types de mutation réellement
  communiqués (seuls l'adresse, le nom, le prénom, la langue et le décès sont traités), la stabilité de la référence de
  mutation d'un envoi à l'autre (elle sert de clé d'idempotence) et l'ordre d'arrivée (les mutations sont appliquées dans
  l'ordre de réception ; une mutation plus ancienne arrivant après une plus récente du même type l'écraserait).
- **Consommateur Affiliés de `integrations.donnees-bce-recues.v1`** : à écrire dans le service Affiliés — gestionnaire
  idempotent qui met à jour la fiche (dénomination, forme juridique, NACE) et les unités d'établissement de l'affilié
  (lecture des adresses par l'API ci-dessus), en passant par l'historique AFF-05 sous l'identité technique du service.
- Adaptateurs réels BCE et BCSS : à implémenter après INT-03 / INT-04.
