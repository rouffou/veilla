# BFF travailleur

Backend for frontend du **portail travailleur** (§10.2, POR-10 à POR-14, epic #18). Port local **5201** (`apiBaseUrl` du
portail en local). Projet `Sepp.Bff.Travailleur`, sans base de données ni domaine propre. Il suit la structure du
[BFF employeur](../Employeur/README.md).

## Rôle et limites (ARC-43, ARC-35, ARC-40)

- **Un BFF par canal, sans logique métier** (ARC-43) : il compose des écrans à partir des API des services, traduit les
  erreurs et borne les requêtes à la personne du jeton. Créneaux ouverts, obligations couvertes, délai d'annulation (24 h),
  validation des réponses d'un questionnaire et droits de lecture restent aux services propriétaires.
- **Aucune référence au code des services** : les contrats JSON utiles sont recopiés dans `Aval/ContratsAval.cs` (seuls les
  champs utiles sont déclarés, donc seuls ceux-là peuvent être relayés). `ArchitectureTests` vérifie que ni l'assembly ni le
  `.csproj` ne référencent un service.
- **Jamais de lecture du dossier médical** (§3.3 : « copie sur demande », SAN-43) : `ArchitectureTests` interdit toute route
  de dossier, de décision complète, d'export ou de « dernières réponses » dans les clients aval. Le questionnaire de santé est
  en **écriture seule** (SAN-22) : le BFF liste les modèles, envoie les réponses et n'en relit aucune.
- **Propagation du jeton (ARC-40)** : `PropagationJetonHandler` recopie sur chaque appel aval `Authorization`,
  `X-Correlation-Id` (ARC-47) et `Accept-Language`. Pas d'identité technique : chaque service revalide le jeton (émetteur,
  audience `sepp-api`) et réapplique le périmètre de la personne (**défense en profondeur**).
- **Résilience (ARC-30)** : délai, reprises et disjoncteur du socle ; les écritures (réservation, annulation, questionnaire,
  demande) ne sont **jamais rejouées** automatiquement.
- **Écrans composites (ARC-35)** : `GET /accueil` appelle en parallèle Planification, Documents et Surveillance médicale ; une
  section dont le service est indisponible vaut `null` et figure dans `indisponibles`, sans faire échouer l'écran.

## Sécurité (POR-10)

- Jeton du portail validé par `AddSeppServiceDefaults` ; politique `portail-travailleur` : rôle **`travailleur`** obligatoire
  (employeur et profils internes reçoivent 403).
- Périmètre : claim **`personne_id`** (valeur unique, UUID du service Personnes). Un filtre sur tout le groupe de routes renvoie
  403 `perimetre.interdit`, **sans appel aval**, si le claim est absent, invalide ou porte deux personnes. L'identifiant de la
  personne n'est lu **que** dans le jeton : aucune route, requête ou corps ne le contient, et celui d'un corps est ignoré.
- Défense en profondeur côté BFF : rendez-vous et documents d'une autre personne sont écartés ; le contenu d'un document dont le
  destinataire n'est pas la personne du jeton (ou non publié) est traité comme inconnu (404) **avant** toute lecture.
- Aucun NISS n'est relayé. CORS : origines explicites (`Cors:Origines`), méthodes GET/POST.

## Endpoints (`/api/v1`)

| Route | Écran / exigence | Service aval |
|---|---|---|
| `GET /accueil?langue=` | accueil : prochains rendez-vous, documents récents, nombre de questionnaires | Planification, Documents, Surveillance médicale |
| `GET /rendez-vous` | POR-11 : mes rendez-vous (sans ressource, salle ni obligations) | Planification `GET /reservations/rendez-vous` |
| `GET /rendez-vous/creneaux?affilieId=&typeActe=&du=&au=&lieuId=` | POR-11 : créneaux ouverts (SAN-12) | Planification `GET /reservations/creneaux` |
| `POST /rendez-vous` `{creneauId, affilieId, obligationIds?}` | POR-11 : réservation pour soi-même (201) | Planification `POST /reservations` |
| `POST /rendez-vous/{id}/annulation` | POR-11 : annulation jusqu'à 24 h avant (204) | Planification `POST /reservations/rendez-vous/{id}/annulation` |
| `GET /questionnaires?langue=` | POR-12 : modèles à remplir | Surveillance médicale `GET /protocoles/questionnaires` |
| `POST /questionnaires/{code}/reponses` `{reponses[]}` | POR-12 : remplissage à l'avance, écriture seule (201 `{id}`) | Surveillance médicale `POST /questionnaires/pre-remplissage` |
| `POST /demandes` `{affilieId, type}` | POR-12 : consultation spontanée ou visite de pré-reprise (`CONSULTATION_SPONTANEE`, `VISITE_PRE_REPRISE`), sans motif | Obligations `POST /demandes-travailleur` |
| `GET /documents` | POR-13 : documents publiés pour la personne | Documents `GET /documents?typeDestinataire=Personne&destinataireId=` |
| `GET /documents/{id}/contenu` | POR-13 : PDF, lecture journalisée par Documents (NF-04) | Documents `GET /documents/{id}` puis `/contenu` |

OpenAPI : `/openapi/v1.json` ; Scalar : `/scalar` (Development).

### Erreurs (RFC 9457)

Identiques au BFF employeur : refus fonctionnel du service (400, 403, 404, 409, 422) relayé avec son `code` et l'extension
`service` ; service injoignable ou 502/503/504 → 503 `service-aval.indisponible` ; 5xx ou réponse illisible → 502
`service-aval.reponse-inattendue` sans détail technique ; personne absente du jeton → 403 `perimetre.interdit`.

## Authentification forte (POR-10)

**itsme et eID ne sont pas disponibles dans cet environnement** : seule la fédération est documentée, rien n'est implémenté.

### Local : Keycloak

Realm `veilla` (`deploy/local/keycloak/veilla-realm.json`) : utilisateur `travailleur` (rôle `travailleur`, attribut
`personne_id` exposé dans le jeton d'accès par un mappeur), client public `veilla-portail-travailleur` (code + PKCE, redirections
`http://localhost:4202` et `http://localhost:8083`). Pour un test de bout en bout, remplacer `personne_id` par l'identifiant d'une
personne créée dans le service Personnes (`deploy/local/README.md`). Les tests d'intégration signent leurs propres jetons
(émetteur, audience, signature, expiration validés comme en production).

### Production : itsme / eID via CSAM ou FAS (à raccorder)

Le BFF et les services ne voient qu'un jeton émis pour l'audience `sepp-api` ; aucun changement de code au passage de Keycloak
à la production (ADR 0005). Fédération prévue :

1. Le portail fait un *authorization code flow + PKCE* vers le courtier d'identité du SEPP ; seul `assets/config.json`
   (`auth.authority`, `auth.clientId`) change entre environnements.
2. Le courtier **fédère en OIDC le service d'authentification fédéral** : **CSAM** (itsme, eID, application mobile) ou, pour les
   parcours qui l'exigent, **FAS** (Federal Authentication Service). Niveau de garantie élevé exigé (eIDAS « substantiel » ou
   « élevé ») pour un accès à des données de santé ; l'accès mobile passe par itsme / l'application, le portail étant responsive
   et installable (manifeste PWA).
3. CSAM/FAS renvoie l'identité (numéro de registre national, nom). Le courtier la rapproche de la **personne** du service
   Personnes (correspondance NISS → `personne_id`, chiffré côté Personnes, DAT-06) et émet un jeton court avec le rôle
   `travailleur` et **un seul** claim `personne_id`. Une personne inconnue n'obtient aucun `personne_id` : le BFF répond alors
   403 (aucun accès sans personne).
4. La déconnexion et le consentement suivent le courtier ; le BFF ne stocke aucune session.

Non couvert (hors de ce BFF) : le contrat d'adhésion à CSAM/FAS, la correspondance NISS → `personne_id` et la gestion du
claim dans le courtier ou un service Identité.

## Configuration

| Clé | Rôle |
|---|---|
| `Authentication:Authority`, `MetadataAddress`, `ValidIssuer`, `Audience` (`sepp-api`) | validation du jeton (socle) |
| `ServicesAval:Planification`, `:SurveillanceMedicale`, `:Obligations`, `:Documents` | URL des services (obligatoires, vérifiées au démarrage) ; en local `http://localhost:5117`, `5118`, `5116`, `5119` ; à défaut `SEPP__SERVICES__PLANIFICATION`, `…__SURVEILLANCE_MEDICALE`, `…__OBLIGATIONS`, `…__DOCUMENTS` injectées par Terraform |
| `Cors:Origines` | origines du portail |

En compose (`deploy/local/compose.yaml`, service `bff-travailleur`, port **5201**), les services sont joints par leur nom de
conteneur (`http://planification:8080`…). Image publiée par la matrice de `.github/workflows/service-images.yml`.

## Lacunes des services aval (à traiter dans les services propriétaires)

Rien n'a été implémenté dans un autre service. Ce qui manque :

1. **Demandes du travailleur (POR-12)** : `POST /api/v1/demandes-travailleur` d'Obligations exige `obligation:gerer` (profils
   internes) et le groupe de routes `obligation:lire` ; le rôle `travailleur` n'a ni l'une ni l'autre dans `Security.cs`, et
   Obligations n'a **aucun périmètre `personne_id`** (le service accepte n'importe quelle `personneId` du corps). Accorder
   `obligation:gerer` au travailleur serait dangereux (changement de statut, recalcul). Il faut une permission dédiée (par exemple
   `demande-travailleur:creer`) et la vérification que la personne du corps est celle du jeton. **Tant que ce n'est pas fait, la
   route `POST /demandes` du BFF est relayée mais Obligations répond 403** (testé avec le service simulé).
2. **Employeurs du travailleur** : aucune route accessible au rôle `travailleur` (Personnes : `personne:lire` ; Obligations :
   `obligation:lire` et pas de périmètre personne) ne donne ses employeurs ni ses obligations à planifier. Le portail déduit les
   employeurs de ses rendez-vous existants ou d'un lien de convocation (`?affilieId=`) : une première réservation sans lien
   n'est pas possible. Souhaitable : route « mes obligations à planifier » dans Obligations ou Planification.
3. **Déplacer et confirmer un rendez-vous (POR-11)** : Planification n'a ni route de déplacement ni de confirmation côté
   externe. Le portail propose « annuler puis réserver un autre créneau » ; la réservation exige une obligation encore à
   planifier, donc l'ordre est imposé (annuler d'abord, délai de 24 h).
4. **Motif d'annulation (POR-11)** : `POST …/annulation` ne reçoit aucun motif (il fixe `DemandeTravailleur`). Le portail n'en
   collecte donc pas plutôt que de le jeter.
5. **Carnet de vaccination et demande de copie du dossier (POR-13)** : Documents n'a pas de document « carnet de vaccination »
   et aucun service n'enregistre une demande de copie (SAN-43 : `GET /dossiers/{id}/export` exige `dossier-sante:lire`). Le
   portail l'indique et renvoie vers le médecin du travail.
6. Les libellés de lieu et la dénomination des employeurs ne sont pas exposés au travailleur : le portail ne les affiche pas.

## Tests

- `tests/Sepp.Bff.Travailleur.Tests` : périmètre `personne_id`, projections (rendez-vous d'autrui écartés, personne du jeton
  utilisée pour réserver, remplir un questionnaire et demander), documents (publiés et du destinataire seulement, contenu
  jamais lu pour autrui), accueil dégradé, propagation du jeton, architecture (aucune référence à un service, aucune route de
  dossier médical).
- `tests/Sepp.Bff.Travailleur.Integration.Tests` : BFF complet (`WebApplicationFactory`, validation réelle de JWT, services aval
  simulés au niveau HTTP) : 401/403, claim invalide sans appel aval, propagation du jeton et de la corrélation, personne du
  jeton plutôt que celle du corps, informations internes absentes, écriture seule, POST non rejoués, 503/502, PDF, CORS.

```bash
dotnet test --solution src/Bff/Travailleur/Travailleur.slnx
docker build -f src/Bff/Travailleur/Dockerfile -t sepp/bff-travailleur .
```
