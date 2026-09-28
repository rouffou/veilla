# veilla-web — fronts Angular de Veilla

Workspace Angular (ADR 0006) regroupant les trois fronts du logiciel métier SEPP et leur bibliothèque partagée. Chaque application est servie par sa propre image et parle à son propre BFF (§14.2, ARC-43).

| Projet | Public | Langues | Exigences principales |
| --- | --- | --- | --- |
| `app-interne` | conseillers en prévention, médecins du travail | FR, NL, DE | NF-40, NF-51 (raccourcis clavier, recherche globale) |
| `portail-employeur` | employeurs, secrétariats sociaux, SIPP | FR, NL, DE, EN | POR-01 à POR-06, NF-50 |
| `portail-travailleur` | travailleurs (mobile, prêt pour une PWA) | FR, NL, DE, EN | POR-10 à POR-14, NF-50 |
| `shared` (bibliothèque) | — | — | configuration, OIDC, i18n, coquille accessible |

## Versions

- Angular 22 (CLI 22.2) : composants autonomes, signaux, **sans zone.js** (zoneless, défaut du CLI), SCSS, TypeScript 6 strict (`strict`, `strictTemplates`).
- Tests unitaires : **Vitest** + jsdom (runner par défaut du CLI, builder `@angular/build:unit-test`).
- Lint : ESLint 10 + angular-eslint 22 (règles d'accessibilité des templates activées).
- i18n : `@jsverse/transloco` 8 (chargement à l'exécution de fichiers JSON).
- OIDC : `angular-auth-oidc-client` 22 (Authorization Code + PKCE).
- Node 24, npm 11.

## Structure

```
veilla-web/
├── angular.json, package.json, tsconfig.json, eslint.config.js
├── Dockerfile                    # image unique paramétrée par ARG APP
├── docker/
│   ├── nginx/default.conf        # fallback SPA, cache, /healthz
│   ├── nginx/security-headers.conf.template  # CSP, nosniff, Referrer-Policy…
│   ├── config.template.json      # modèle de assets/config.json
│   └── entrypoint/40-veilla-runtime-config.sh
└── projects/
    ├── shared/
    │   ├── src/lib/config/       # chargement/validation de assets/config.json
    │   ├── src/lib/auth/         # provideVeillaAuth, AuthService, authGuard, apiAuthInterceptor
    │   ├── src/lib/i18n/         # provideVeillaI18n, LanguageService, sélecteur, titres traduits
    │   ├── src/lib/layout/       # Shell (landmarks, navigation), DashboardCard
    │   ├── src/lib/a11y/         # SkipLink
    │   ├── src/lib/pages/        # accessibilité, erreur de connexion, page « à venir »
    │   ├── i18n/{fr,nl,de,en}.json  # libellés communs (copiés dans i18n/shared/)
    │   ├── styles/_veilla.scss   # styles globaux accessibles (contrastes, focus, cibles)
    │   └── testing/              # @veilla/shared/testing : provideVeillaTesting, FakeAuthService
    ├── app-interne/
    │   ├── public/assets/config.json   # configuration de développement local
    │   ├── public/i18n/{fr,nl,de}.json
    │   └── src/app/{dashboard,search,shortcuts}
    ├── portail-employeur/        # public/i18n/{fr,nl,de,en}.json, src/app/dashboard (POR-02)
    └── portail-travailleur/      # idem + manifest.webmanifest et icône (PWA)
```

La bibliothèque est consommée **depuis les sources** grâce aux alias `@veilla/shared` et `@veilla/shared/testing` (`tsconfig.json`) : pas besoin de la construire avant les applications.

## Commandes

```bash
npm ci                          # installation reproductible
npm run start:interne           # http://localhost:4200
npm run start:employeur         # http://localhost:4201
npm run start:travailleur       # http://localhost:4202
npx ng build portail-employeur  # build de production d'une application (dist/<app>/browser)
npm run build                   # build des trois applications
npm run test:ci                 # tous les tests unitaires (Vitest, sans surveillance)
npx ng test app-interne --watch=false
npm run lint                    # ESLint sur les quatre projets
npm run build:shared            # paquet de la bibliothèque (facultatif)
```

En développement, `ng serve` affiche la page « Connexion impossible » tant qu'aucun fournisseur d'identité n'écoute sur l'`authority` de `public/assets/config.json` (Keycloak local attendu sur `http://localhost:8180/realms/veilla`, voir ADR 0005). Les URL de BFF de ces fichiers (`http://localhost:5100`, `5200`, `5300`) sont provisoires.

## Configuration d'exécution (CTR-05)

Aucune valeur d'environnement n'est compilée : `main.ts` charge `assets/config.json` **avant** l'amorçage d'Angular, le valide (`parseRuntimeConfig`) puis le fournit via le jeton `RUNTIME_CONFIG`. En cas d'échec, un message trilingue accessible est affiché.

```json
{
  "apiBaseUrl": "https://api.example.be/bff-employeur",
  "auth": {
    "authority": "https://login.example.be/realms/veilla",
    "clientId": "veilla-portail-employeur",
    "scope": "openid profile email offline_access"
  }
}
```

## Authentification (POR-01, POR-10, ADR 0005)

- `provideVeillaAuth(config)` configure `angular-auth-oidc-client` en **code flow + PKCE**, renouvellement par jeton d'actualisation (`offline_access`), redirection et déconnexion vers l'origine de l'application. Entra ID pour l'application interne, fédération CSAM (itsme, eID) pour les portails : seul `config.json` change.
- `AuthService` : façade à signaux (`isAuthenticated`, `userName`), `checkStatus()` (vérification unique, traite le retour du fournisseur), `login(returnUrl)`, `logout()`. Si le document de découverte est injoignable, l'utilisateur est dirigé vers `/erreur-connexion`.
- `authGuard` : protège toutes les routes applicatives, mémorise la page demandée et y revient après connexion.
- `apiAuthInterceptor` : ajoute `Authorization: Bearer …` **uniquement** aux requêtes dont l'origine et le chemin correspondent à `apiBaseUrl` (BFF) ; les fichiers de traduction et autres ressources ne reçoivent jamais le jeton.

## Internationalisation (NF-40, POR-14)

- `provideVeillaI18n({ langs })` : `INTERNAL_LANGS` (FR, NL, DE) pour l'application interne, `PORTAL_LANGS` (FR, NL, DE, EN) pour les portails. Langue par défaut et de repli : FR.
- Libellés en JSON chargés à l'exécution : `i18n/shared/<lang>.json` (bibliothèque, copiés par `angular.json`) fusionnés avec `i18n/<lang>.json` (application). Les clés doivent être identiques dans toutes les langues.
- Langue initiale : choix mémorisé (`localStorage`), sinon langue du navigateur, sinon FR. Le changement met à jour `<html lang>` (WCAG 3.1.1) et le titre de page traduit (`TranslatedTitleStrategy`, WCAG 2.4.2 : la propriété `title` des routes est une clé).
- Sélecteur : `<select>` natif étiqueté, noms de langue dans leur propre langue avec attribut `lang`.
- Dans un template : `<ng-container *transloco="let t">{{ t('dashboard.title') }}</ng-container>`.

## Accessibilité (NF-50, EN 301 549 / WCAG 2.1 AA)

- Lien d'évitement en premier élément focalisable, repères `header`/`nav` (étiquetée)/`main`/`footer`, un `h1` par page.
- Focus toujours visible (contour 3 px, jaune sur l'en-tête sombre), déplacé sur le contenu principal après chaque navigation.
- Contrastes calculés ≥ 4,5:1 (texte) et ≥ 3:1 (composants), cibles ≥ 44 px, tailles en `rem`, `prefers-reduced-motion`, mode contraste forcé.
- Page `/accessibilite` : déclaration à compléter après audit (aucune conformité n'est revendiquée avant audit).
- ESLint `templateAccessibility` sur tous les templates. Un audit outillé (axe) et manuel reste à mener.

### Application interne (NF-51)

- Recherche globale dans l'en-tête (`role="search"`) ; page `/recherche?q=…` qui indique explicitement que le service n'est pas encore connecté.
- Raccourcis : `Ctrl+K` / `⌘+K` (recherche), `/` (recherche), `?` (aide), `Échap` (fermer). Les raccourcis à une touche sont ignorés pendant la saisie et **désactivables** depuis l'aide (WCAG 2.1.4).

## Tableaux de bord

Chaque application a une page d'accueil protégée avec des cartes à **l'état vide** (« Aucune donnée à afficher pour le moment ») : aucune donnée fictive n'est présentée comme réelle.

- Portail employeur (POR-02) : examens dus, en retard, planifiés ; missions en cours ; mesures du plan d'action ; solde d'unités. Pages « à venir » : travailleurs/postes/risques (POR-03), demandes (POR-04), décisions et rapports (POR-05), documents légaux (POR-06).
- Portail travailleur : rendez-vous (POR-11), questionnaires et demande de consultation (POR-12), documents (POR-13).
- Application interne : agenda du jour, tâches, dossiers récents.

## Conteneurisation (CTR-01 à CTR-07)

Un seul `Dockerfile` multi-stage, paramétré par `ARG APP` (une image par application) :

```bash
docker build --build-arg APP=portail-employeur -t veilla/portail-employeur:dev .
docker run --rm -p 8080:8080 \
  -e VEILLA_API_BASE_URL=https://api.example.be/bff-employeur \
  -e VEILLA_OIDC_AUTHORITY=https://login.example.be/realms/veilla \
  -e VEILLA_OIDC_CLIENT_ID=veilla-portail-employeur \
  --read-only --tmpfs /tmp \
  veilla/portail-employeur:dev
```

- Étape 1 `node:24-alpine` : `npm ci` puis `ng build <APP>` ; étape 2 `nginxinc/nginx-unprivileged` (alpine) : utilisateur 101, port **8080**, aucun outil de build (CTR-03).
- Au démarrage, `/docker-entrypoint.d/40-veilla-runtime-config.sh` écrit dans `/tmp/veilla` :
  - `config.json`, copié depuis `VEILLA_CONFIG_FILE` (défaut `/etc/veilla/runtime/config.json`, à monter) s'il existe, sinon généré par `envsubst` depuis `VEILLA_API_BASE_URL`, `VEILLA_OIDC_AUTHORITY`, `VEILLA_OIDC_CLIENT_ID` (obligatoires) et `VEILLA_OIDC_SCOPE` (défaut `openid profile email offline_access`) ;
  - les en-têtes de sécurité, dont `connect-src` : `CSP_CONNECT_SRC` si fourni, sinon les origines du BFF et du fournisseur d'identité déduites de la configuration.
- nginx sert `/assets/config.json` depuis `/tmp/veilla` (jamais mis en cache), `index.html` sans cache, les ressources hachées avec un cache d'un an, fallback SPA vers `index.html`, sonde `/healthz`.
- En-têtes : `Content-Security-Policy` (`script-src 'self'` sans script inline — l'inlining du CSS critique est désactivé pour cela —, `frame-ancestors 'none'`, `object-src 'none'`…), `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `Cross-Origin-Opener-Policy`, `Permissions-Policy`. HSTS est laissé à la passerelle qui termine TLS.
- Compatible avec un système de fichiers racine en lecture seule à condition de monter un volume temporaire sur `/tmp` (CTR-07).

## Limites connues

- Image Docker non construite localement (Docker indisponible au moment de l'écriture) : Dockerfile, configuration nginx et script d'entrée restent à valider en CI (`docker build` + `nginx -t` + test de fumée). Le script d'entrée a été testé hors conteneur.
- Aucun BFF ni fournisseur d'identité branché : les tableaux de bord sont vides et le parcours de connexion n'a pas été testé de bout en bout (à faire avec le Keycloak local).
- Pas de service worker : le portail travailleur a un manifeste et une icône SVG (installable), le mode hors ligne reste à concevoir (`ng add @angular/pwa`).
- Traductions NL/DE/EN rédigées par l'équipe de développement : relecture par des locuteurs natifs à prévoir.
- Accessibilité : les bonnes pratiques sont en place mais la conformité EN 301 549 devra être établie par un audit.
