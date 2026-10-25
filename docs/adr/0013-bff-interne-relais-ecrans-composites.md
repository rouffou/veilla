# ADR 0013 — BFF interne : relais vers une liste fermée de services et écrans composites

## Statut
Proposée (à valider par l'architecte), 2026-10-25. Ticket #390, épique #389 ; débloque #206 et #203.

## Contexte
ARC-43 et l'ADR 0006 prévoient un BFF par canal. Les portails employeur et travailleur ont le leur, qui compose des écrans
fermés à partir de quelques routes. L'application interne, elle, couvre presque toute l'API des douze services métier
(plusieurs centaines de routes : dossiers, planification, consultation, administration). Elle pointait sur
`http://localhost:5100`, où rien n'écoutait. Les services n'exposent pas de CORS et ne sont pas joignables depuis l'extérieur
en Azure (seuls les BFF le sont, derrière API Management).

Recopier chaque route des services dans un BFF orienté écran doublerait le travail de chaque lot, sans rien apporter
pour la sécurité : chaque service revalide déjà le jeton et applique la matrice des droits (ARC-40, ARC-41). À l'inverse,
certains écrans (dossier travailleur, tableau de bord, consultation) agrègent cinq à dix services, et un aller-retour par
section depuis le navigateur coûte cher.

## Décision
`src/Bff/Interne` (`bff-interne`, port local 5100, zone plateforme) remplit deux rôles.

1. **Relais** (YARP) `/{service}/api/v1/{reste}` → `{service}/api/v1/{reste}`.
   - **Liste fermée** de douze services, dans le code (`ServicesRelayes`) : referentiels, audit, affilies, personnes,
     postes-risques, obligations, integrations, surveillance-medicale, planification, documents, communications, reintegration.
   - Un service hors liste ou un chemin hors `/api/v1` n'a pas de route (404) : sondes, OpenAPI, `/admin/republications`,
     autre version. Les formes encodées de sortie de chemin (`..`, `%2e`, `%2f`, `%5c`, `\`) sont refusées (400).
   - **En-têtes sur liste blanche** : `Authorization`, `Accept`, `Accept-Language`, `X-Motif-Acces`, `X-Bris-De-Glace`,
     les en-têtes du corps et un `X-Correlation-Id` normalisé. Ni `Cookie`, ni `Host` d'origine, ni `X-Forwarded-*`. Le
     BFF n'ajoute **aucun en-tête d'identité** : le service ne connaît l'utilisateur que par son jeton, qu'il revalide.
     `Set-Cookie` est retiré des réponses.
   - Statut et corps des réponses relayés tels quels. **Aucune reprise automatique** : YARP ne rejoue pas les requêtes.
     Délais : connexion 5 s, inactivité 30 s (504). Service injoignable : 503 explicite.
2. **Écrans composites** sous `/api/v1/ecrans/*`.
   - Un fichier par écran (recherche, dossier travailleur, affilié, agenda, consultation, tableau de bord), possédé par
     le lot qui le réalise. Tant qu'il n'est pas réalisé, il répond 501.
   - Briques communes : `Annuaire` (identifiant → libellé, cache limité à la requête) et `SectionTolerante<T>`
     `{ disponible, raison: acces-refuse | service-indisponible, valeur }`. Une section refusée par la matrice des droits
     ou indisponible n'empêche pas l'affichage du reste de l'écran.
   - Clients typés du socle : délai, disjoncteur, reprises des seules lectures.

**Accès** : jeton validé comme dans les autres BFF (émetteur, audience `sepp-api`). La politique `application-interne`
exige un des 15 profils internes du §3.1 et aucun rôle externe. Les jetons employeur, SIPP et travailleur, les comptes
techniques et l'exploitation reçoivent 403, sans appel aval. Sans jeton, la réponse est 401.

**CORS** : nouvelle surcharge `AddSeppPortailCors(methodes, entetesSupplementaires)` : GET, POST, PUT, DELETE, plus
`X-Motif-Acces` et `X-Bris-De-Glace`. Mêmes règles d'origine, sans credentials. Les BFF de portail sont inchangés.

**Déploiement** : `bff-interne` figurait déjà au catalogue Terraform et reçoit `SEPP__SERVICES__*` comme les autres BFF.
`portal_origins` exige désormais aussi ses origines. Image construite par `service-images.yml`. En local, service compose
`bff-interne` (5100:8080).

## Alternatives écartées
- **BFF entièrement orienté écran** (comme les portails) : chaque route de chaque service serait recopiée et testée deux
  fois, sans gain de sécurité, puisque le service applique déjà les droits. Retenu seulement pour les écrans qui agrègent.
- **CORS sur chaque service et appel direct depuis le navigateur** : cela expose les douze services hors du réseau interne
  (contraire à ARC-43 et au modèle réseau de l'ADR 0003) et multiplie les configurations CORS.
- **Relais ouvert** (`/{service}/{**chemin}` avec une adresse calculée) : le relais devient un proxy générique vers des
  routes non prévues (sondes, administration, autres versions) ou des hôtes arbitraires.
- **Liste fermée en configuration** : un ajout de service doit être une décision revue (code, compose, Terraform), pas
  une variable d'environnement.
- **`documents-medicale`** n'est pas relayé : en local, l'instance `documents` sert les trois zones. L'accès de l'application
  interne aux documents de la zone médicale en déploiement par zone reste à décider (ADR 0009).

## Conséquences
- L'application interne appelle `${apiBaseUrl}/{service}/api/v1/...` et `${apiBaseUrl}/api/v1/ecrans/...`. Un service
  ajouté au relais exige une modification de `ServicesRelayes`, de compose et du test d'architecture.
- La sécurité métier reste dans les services. Le BFF n'est qu'un premier filtre de canal, et une route de service mal
  protégée le reste derrière le relais.
- Le relais ne retraduit pas les erreurs des services : l'application interne lit les ProblemDetails (`code`) des services.
- Un jeton de 5 minutes traverse le BFF tel quel. Le rafraîchissement reste à la charge de l'application.
- À valider : délais (30 s d'inactivité, suffisant pour les imports CSV et les pièces jointes ?), liste des profils internes,
  refus des comptes qui cumulent un rôle interne et un rôle externe.
