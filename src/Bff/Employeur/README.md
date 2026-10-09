# BFF employeur

Backend for frontend du **portail employeur** (§10.1, Lot 1 « portail employeur en lecture », §16.1). Port local **5200**
(`apiBaseUrl` du portail en local). Projet `Sepp.Bff.Employeur`, sans base de données ni domaine propre.

## Rôle et limites (ARC-43, ARC-35, ARC-40)

- **Un BFF par canal, sans logique métier** (ARC-43) : il compose des écrans à partir des API des services, traduit
  les erreurs et borne les requêtes au périmètre du jeton. Il ne décide rien : validité d'une proposition, règles de
  périmètre fines et historisation restent aux services propriétaires.
- **Aucune référence au code des services** : les contrats JSON utiles sont recopiés dans `Aval/ContratsAval.cs`
  (énumérations lues comme des chaînes). Un test d'architecture vérifie que ni l'assembly ni le `.csproj` ne
  référencent un service (`ArchitectureTests`).
- **CQRS pour écrans composites (ARC-35)** : pour le Lot 1, les modèles de lecture sont **composés à la volée**
  (appels parallèles puis projection, `Ecrans/EcransEmployeur.cs`) : volumes faibles (un affilié, quelques centaines de
  travailleurs), aucune donnée dupliquée à resynchroniser. Quand le tableau de bord agrégera des sources plus
  coûteuses (obligations, planification, prestations), il basculera vers une **projection matérialisée** alimentée par
  les événements (ARC-31, abonnement `bff-employeur` déjà prévu dans `infra/variables.tf`) ; les routes et les modèles
  d'écran resteront identiques pour le portail.
- **Propagation du jeton (ARC-40)** : `PropagationJetonHandler` recopie sur chaque appel aval l'en-tête
  `Authorization` de l'utilisateur, l'identifiant de corrélation `X-Correlation-Id` (ARC-47) et `Accept-Language`.
  Le BFF n'a pas d'identité technique vis-à-vis des services : chacun revalide le jeton (émetteur, audience
  `sepp-api`) et réapplique le périmètre de l'affilié (**défense en profondeur**).
- **Résilience (ARC-30)** : délai, reprises et disjoncteur du socle (`AddStandardResilienceHandler`) ; les écritures
  (POST de propositions) ne sont **jamais rejouées** automatiquement.

## Sécurité

- Jeton du portail validé par `AddSeppServiceDefaults` (audience `sepp-api`) ; politique `portail-employeur` : rôle
  **`employeur` ou `sipp`** obligatoire (un profil interne reçoit 403).
- Périmètre : claim multivalué **`affilie_id`** (claim répété, tableau JSON ou valeurs séparées par des virgules ; valeurs
  non UUID ignorées, voir le README du service Affiliés). Toute route `/api/v1/affilies/{affilieId}/…` hors de ce
  périmètre est refusée (403 `perimetre.interdit`) **avant tout appel aval**. Un poste ou une liste d'un autre affilié
  est traité comme inconnu (404).
- Aucun NISS n'est relayé, même masqué (DAT-06, minimisation) ; l'export CSV non plus.
- CORS : origines explicites (`Cors:Origines`), méthodes GET/POST, en-têtes exposés `Content-Disposition` et
  `X-Correlation-Id`.

## Endpoints (`/api/v1`)

| Route | Écran / exigence | Services aval |
|---|---|---|
| `GET /affilies` | sélecteur d'affilié (POR-01) | Affiliés (un appel par affilié du jeton) |
| `GET /affilies/{id}` | fiche : identité, sites et contacts en vigueur (AFF-01 à AFF-03) | Affiliés |
| `GET /affilies/{id}/tableau-de-bord` | POR-02 | Personnes, Postes et risques |
| `GET /affilies/{id}/travailleurs?recherche=&page=&taille=` | POR-03 : recherche (casse et accents ignorés), tri, pagination (taille ≤ 100) | Personnes |
| `GET /affilies/{id}/postes?langue=` | POR-03 : postes, risques en vigueur avec libellé, « exposé » | Postes et risques |
| `GET /risques?langue=` | référentiel pour le formulaire de proposition | Postes et risques |
| `POST /affilies/{id}/postes/{posteId}/propositions` | POR-03 → AFF-14 | Postes et risques |
| `GET /affilies/{id}/propositions` | état des propositions (soumise, validée, refusée + motif) | Postes et risques |
| `GET /affilies/{id}/listes-nominatives` | POR-06 : versions, la dernière de chaque type signalée | Postes et risques |
| `GET /affilies/{id}/listes-nominatives/{listeId}` | lignes avec noms des travailleurs et intitulés des postes | Postes et risques, Personnes |
| `GET /affilies/{id}/listes-nominatives/{listeId}/csv?langue=` | POR-06 : téléchargement CSV | idem |
| `POST /affilies/{id}/listes-nominatives/{listeId}/propositions` | POR-03 → AFF-31 | Postes et risques |
| `POST /affilies/{id}/reprises` | POR-04 : annonce d'une reprise du travail (travailleur, date de reprise, début de l'absence) ; 201 à la création, 200 si déjà connue | Obligations (`POST /api/v1/reprises`) |
| `GET /affilies/{id}/reprises` | POR-04 : suivi (statut, date limite, retard) | Obligations (`GET /api/v1/reprises?affilieId=`) |
| `GET /affilies/{id}/reprises/{repriseId}` | POR-04 : une reprise ; celle d'un autre affilié est inconnue (404) | Obligations |

OpenAPI : `/openapi/v1.json` ; Scalar : `/scalar` (Development).

### Tableau de bord (POR-02)

Seuls les compteurs calculables aujourd'hui ont une valeur : `travailleurs`, `postesActifs`, `postesExposes` (actifs
portant au moins un risque en vigueur), `propositionsEnAttente`. Les autres (`examensDus`, `examensEnRetard`,
`examensPlanifies`, `missionsEnCours`, `mesuresPlanAction`, `soldeUnites`) sont renvoyés avec `disponible: false`,
`valeur: null` et `raison: "fonctionnalite-a-venir"` : **aucun chiffre fictif**. Si un service aval est injoignable,
ses compteurs passent à `raison: "service-indisponible"` sans faire échouer l'écran.

### Export CSV des listes nominatives (POR-06)

Aucun service ne produit encore de document : le BFF génère le CSV à partir du JSON du service Postes et risques.
UTF-8 avec BOM, séparateur `;`, en-têtes dans la langue demandée (FR, NL, DE, EN), cellules neutralisées contre
l'injection de formules (`=`, `+`, `-`, `@`), nom `liste-nominative-<type>-v<version>-<date>.csv`. Le **PDF/A**
relèvera du service Documents (DOC-02) ; le BFF n'en produit pas.

### Reprises du travail (POR-04, saga « examen de reprise »)

Le BFF **relaie en synchrone** (ARC-30) l'annonce vers `POST /api/v1/reprises` du service Obligations, comme les propositions de
poste : il ajoute l'`affilieId` de la route (jamais celui du corps), propage le jeton (permission `reprise:annoncer` /
`reprise:lire`, périmètre `affilie_id` revérifié par le service) et **ne publie pas `RepriseAnnoncee`** : l'annonce passe par
l'API d'Obligations, qui persiste le processus et publie `obligations.reprise-enregistree.v1` par son outbox (ADR 0008, plan
`docs/architecture/saga-examen-reprise.md`, écart au §14.6 à valider par l'architecte). L'employeur peut annoncer et lire, pas
modifier ni annuler (`reprise:gerer`).

- Le POST n'est jamais rejoué (l'idempotence est celle du service : même travailleur, affilié et date = même processus) ; les
  GET bénéficient de la résilience standard.
- Les refus 409 et 422 d'Obligations sont traduits en ProblemDetails (même statut, `code`, `detail`, `service: obligations`).
- Le modèle d'écran (`RepriseEcran`) ne porte que le statut du processus, les dates et le retard : ni identifiant d'examen ni de
  décision, ni rendez-vous, ni donnée médicale. **Ce que voit l'employeur reste à valider** (plan, §6).
- Le BFF ne vérifie pas que le travailleur est occupé chez l'affilié : cette règle relève des services propriétaires.

### Erreurs (RFC 9457)

| Situation | Réponse du BFF |
|---|---|
| Refus fonctionnel du service (400, 403, 404, 409, 422) | même statut, `code` et `detail` du service, extension `service` |
| Service injoignable, délai dépassé, disjoncteur ouvert, 502/503/504 aval | **503** `service-aval.indisponible` + `service` |
| 5xx ou 401 du service, réponse illisible | **502** `service-aval.reponse-inattendue`, sans détail technique (journalisé) |
| Affilié hors du jeton | 403 `perimetre.interdit`, sans appel aval |

## Configuration

| Clé | Rôle |
|---|---|
| `Authentication:Authority`, `MetadataAddress`, `ValidIssuer`, `Audience` (`sepp-api`) | validation du jeton (socle) |
| `ServicesAval:Affilies`, `:Personnes`, `:PostesRisques`, `:Obligations` | URL des services (obligatoires, vérifiées au démarrage ; Obligations en local : `http://localhost:5116`) ; à défaut `SEPP__SERVICES__AFFILIES`, `…__PERSONNES`, `…__POSTES_RISQUES`, `…__OBLIGATIONS` injectées par Terraform |
| `Cors:Origines` | origines du portail |

En compose (`deploy/local/compose.yaml`, service `bff-employeur`), les services sont joints par leur nom de conteneur
(`http://affilies:8080`…).

## POR-01 : authentification CSAM

**CSAM n'est pas disponible dans cet environnement.** Le branchement prévu (ADR 0005) :

1. Le portail fait un *authorization code flow + PKCE* (`angular-auth-oidc-client`) vers le fournisseur d'identité
   du SEPP ; seul `assets/config.json` (`auth.authority`, `auth.clientId`) change entre environnements.
2. Ce fournisseur (Keycloak en local et en test, courtier d'identité en production) **fédère CSAM en OIDC** (itsme,
   eID) : l'utilisateur s'authentifie chez CSAM, qui renvoie son identité (numéro de registre national, nom).
3. Le courtier consulte la **gestion des accès de la sécurité sociale** (mandats : employeur, secrétariat social, SIPP
   délégué) et émet un jeton d'accès court pour l'audience `sepp-api`, avec le rôle (`employeur` ou `sipp`) et un claim
   `affilie_id` par affilié représenté (identifiants du service Affiliés, correspondance BCE → affilié).
4. Le BFF et les services ne voient que ce jeton : aucun changement de code au passage de Keycloak à CSAM.

Ce qui est **testé en local** avec Keycloak (realm `veilla`, `deploy/local/README.md`) : l'utilisateur `employeur`
(rôle `employeur`, attribut multivalué `affilie_id` exposé par un mappeur dans le jeton d'accès) ; jeton obtenu par le
client `veilla-dev-cli` (flux mot de passe, local uniquement) ou par le portail (`veilla-portail-employeur`, code + PKCE).
Les tests d'intégration du BFF signent eux-mêmes des jetons (émetteur, audience, signature, expiration validés comme en
production) et vérifient la propagation exacte du jeton aux services simulés.

Non couvert : la délégation à un secrétariat social, la consultation des mandats et la correspondance BCE → affilié
(à réaliser dans le courtier d'identité ou un service Identité, hors de ce BFF).

## Tests

- `tests/Sepp.Bff.Employeur.Tests` : agrégation (fiche, pagination et recherche, postes exposés, tableau de bord sans
  chiffre fictif et dégradé), bornage (`affilie_id`), CSV (échappement, injection, BOM, traductions), propagation du
  jeton, architecture (aucune référence à un service).
- `tests/Sepp.Bff.Employeur.Integration.Tests` : BFF complet (`WebApplicationFactory`) avec validation réelle de JWT
  et services aval simulés au niveau HTTP (`ServicesAvalSimules`) : 401/403, propagation du jeton et de la corrélation,
  bornage sans appel aval, NISS absent, 503 explicite, relais des refus, 502 sans fuite, CSV, propositions non rejouées,
  CORS.

```bash
dotnet test --project src/Bff/Employeur/tests/Sepp.Bff.Employeur.Tests/Sepp.Bff.Employeur.Tests.csproj
dotnet test --project src/Bff/Employeur/tests/Sepp.Bff.Employeur.Integration.Tests/Sepp.Bff.Employeur.Integration.Tests.csproj
docker build -f src/Bff/Employeur/Dockerfile -t sepp/bff-employeur .
```

## Limites connues

- Les lignes des listes nominatives viennent des projections du service Postes et risques (événements
  `AffectationModifiee`) : sans bus d'événements en local, une liste générée en compose est vide (ADR 0004).
- Le formulaire de proposition de liste propose au plus 100 travailleurs (taille maximale d'une page).
- Pas de cache : chaque écran interroge les services (voir ARC-35 ci-dessus).
