# BFF interne

Backend for frontend de l'**application interne** (profils du SEPP, §3.1). Port local **5100** (`apiBaseUrl` de
l'application interne en local). Projet `Sepp.Bff.Interne`, sans base de données ni domaine propre. Décision :
[ADR 0013](../../../docs/adr/0013-bff-interne-relais-ecrans-composites.md).

## Deux usages

1. **Relais** vers une **liste fermée** de services : `/{service}/api/v1/{reste}` → `{adresse du service}/api/v1/{reste}`
   (requête comprise). Services : `referentiels`, `audit`, `affilies`, `personnes`, `postes-risques`, `obligations`,
   `integrations`, `surveillance-medicale`, `planification`, `documents`, `communications`, `reintegration`
   (`Relais/ServicesRelayes.cs`). Le corps et le statut de la réponse du service sont relayés tels quels (ProblemDetails
   compris), sans retraduction.
2. **Écrans composites** sous `/api/v1/ecrans/…`, un fichier par écran (`Ecrans/Ecran*.cs`), possédé par le lot de
   l'épique #389 qui le réalise. Tant qu'un écran n'est pas écrit, il répond **501** `ecran.non-implemente`.

| Route | Écran | Lot |
|---|---|---|
| `GET /api/v1/ecrans/recherche?q=` | recherche de travailleurs et d'affiliés | A |
| `GET /api/v1/ecrans/travailleurs/{personneId}` | dossier travailleur | A |
| `GET /api/v1/ecrans/affilies/{affilieId}` | fiche affilié | A |
| `GET /api/v1/ecrans/agenda` | agenda jour et semaine | B |
| `GET /api/v1/ecrans/consultation/{personneId}` | vue de consultation | C |
| `GET /api/v1/ecrans/tableau-de-bord` | tableau de bord par rôle, file de travail du CPMT | D |

Outils des écrans composites :

- `Annuaire` (scoped) : personne → « Prénom Nom », affilié → dénomination ; cache limité à la requête, un identifiant n'est
  demandé qu'une fois même en parallèle ; libellé refusé, introuvable ou indisponible → `null` (l'écran affiche l'identifiant).
- `SectionTolerante<T>` : `{ disponible, raison, valeur }` avec `raison` = `acces-refuse` (401/403 du service) ou
  `service-indisponible` (injoignable, 5xx, contrat invalide). `SectionTolerante.ChargerAsync(() => …)` charge une section
  sans faire échouer l'écran ; les autres erreurs (400, 404, 409, 422) remontent à `ErreursAvalHandler`.

## Sécurité

- **Authentification** : jeton validé par `AddSeppServiceDefaults` (émetteur, audience `sepp-api`, signature, expiration),
  comme les autres BFF ; sans jeton valide → **401**.
- **Profils internes seulement** : politique `application-interne` sur le relais et les écrans. Il faut un des 15 rôles
  internes du §3.1 et aucun rôle externe ; `employeur`, `sipp`, `travailleur`, les rôles techniques des comptes de service et
  `exploitation` reçoivent **403**, sans appel aval. Chaque service revalide le jeton et applique sa matrice de permissions
  (défense en profondeur) : le BFF ne décide d'aucun droit métier.
- **Liste fermée, chemins bornés** : un service hors liste, ou un chemin hors `/{service}/api/v1/…` (sondes, OpenAPI,
  `/admin/republications`, autre version d'API) n'a pas de route (**404**). Un chemin qui tente de sortir de `/api/v1`
  (`..`, `%2e`, `%2f`, `%5c`, `\`) est refusé (**400** `relais.chemin-invalide`).
- **En-têtes sur liste blanche** : seuls `Authorization`, `Accept`, `Accept-Language`, `X-Motif-Acces`, `X-Bris-De-Glace`
  et les en-têtes du corps (`Content-Type`, `Content-Length`) partent vers le service, avec un `X-Correlation-Id` normalisé
  par le BFF. Ni `Cookie`, ni `Host` d'origine, ni `X-Forwarded-*`, ni aucun en-tête d'identité ajouté par le BFF ; un
  `Set-Cookie` du service est retiré.
- **Pas de reprise automatique** : le relais (YARP) ne rejoue aucune requête ; les clients typés des écrans ne rejouent que
  les lectures. Délais : connexion 5 s, inactivité 30 s (504 `service-aval.delai-depasse`) ; service injoignable → 503
  `service-aval.indisponible`.
- **CORS** : origines exactes (`Cors:OriginesAutorisees`, 8081 et 4200 en local), méthodes GET, POST, PUT et DELETE,
  en-têtes communs plus `X-Motif-Acces` et `X-Bris-De-Glace`, sans credentials.

## Configuration

| Clé | Rôle |
|---|---|
| `ServicesAval:<Service>` | adresse de chaque service de la liste (compose, poste local) ; sinon `Sepp:Services:<Service>` injecté par Terraform (`SEPP__SERVICES__*`). Toutes obligatoires : le démarrage échoue sinon. |
| `Authentication:*` | émetteur et audience, comme les autres services |
| `Cors:OriginesAutorisees` | origines de l'application interne (Terraform : `portal_origins["bff-interne"]`) |

```bash
docker compose -f deploy/local/compose.yaml up -d --build bff-interne referentiels
dotnet run --project src/Bff/Interne/src/Sepp.Bff.Interne   # hors conteneur, port 5100
```

OpenAPI : `/openapi/v1.json` ; Scalar : `/scalar` (Development).
