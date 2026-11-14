# Standards TypeScript (Node.js back-end et front-end)

> Lu par `software-architect-typescript`, `developer-typescript`, `frontend-developer` et `code-reviewer` sur du code TS/JS. Les conventions existantes du dépôt priment : signaler l'écart, ne pas tout réécrire.

## 1. Socle
- **Runtime** : Node.js LTS active (fixée dans `.nvmrc` / `engines`) ; Bun ou Deno uniquement sur ADR.
- **TypeScript** : `"strict": true`, `"noUncheckedIndexedAccess": true`, `"exactOptionalPropertyTypes": true`, `"noImplicitOverride": true`.
- **Gestionnaire de paquets** : pnpm (workspaces pour monorepo) ; lockfile commité.
- **Qualité** : ESLint (flat config, `typescript-eslint` strict-type-checked) + Prettier, ou Biome ; Husky + lint-staged.
- **Monorepo** : pnpm workspaces + Turborepo ou Nx.

## 2. Architecture de référence — back-end
```
apps/api/src/
├── modules/
│   └── orders/
│       ├── domain/          entités, value objects, erreurs — aucun import framework
│       ├── application/     use cases, ports (interfaces)
│       ├── infrastructure/  repositories (Prisma/Drizzle), clients externes
│       └── http/            routes / controllers, schémas Zod
├── shared/
├── config.ts                validation de l'environnement avec Zod
└── main.ts                  composition root
packages/                    code partagé (types de contrat, UI kit, config eslint/tsconfig)
```
- Règles de dépendance vérifiées par `dependency-cruiser` ou `eslint-plugin-boundaries`.

## 3. Architecture de référence — front-end
```
apps/web/src/
├── app/             routing (Next.js App Router) ou pages
├── features/<x>/    composants, hooks, appels API, état propres à la feature
├── components/ui/   composants génériques (design system)
├── lib/             utilitaires, client API généré
└── styles/
```
- Organisation **par feature**, pas par type de fichier.

## 4. Choix techniques par défaut
| Besoin | Back-end | Front-end |
|---|---|---|
| Framework | NestJS (grandes équipes, structure imposée) ou Fastify / Hono (léger) | React + Next.js ; Angular si contexte entreprise existant ; Vue/Nuxt possible |
| Validation | Zod (ou Valibot) aux frontières | Zod + React Hook Form |
| Accès données | Prisma ou Drizzle | — |
| État serveur | — | TanStack Query |
| État client | — | État local d'abord ; Zustand si nécessaire |
| Styles | — | Tailwind CSS + composants accessibles (shadcn/ui, Radix) |
| Contrats API | OpenAPI généré ou tRPC (monorepo full-TS) | client typé généré (openapi-typescript / orval) |
| Logs | pino (JSON) + OpenTelemetry | — |
| Tests | Vitest, Supertest, Testcontainers | Vitest + Testing Library, Playwright (E2E), MSW |

## 5. Conventions de code
- `camelCase` variables/fonctions, `PascalCase` types/classes/composants, `kebab-case` noms de fichiers (sauf composants React en `PascalCase.tsx` si convention du dépôt).
- **Interdits** : `any` (utiliser `unknown` + narrowing), `@ts-ignore` (préférer `@ts-expect-error` commenté), `!` non-null assertion sans justification.
- Types dérivés des schémas (`z.infer<typeof Schema>`) : une seule source de vérité.
- ESM (`"type": "module"`), imports explicites ; pas d'export default sauf exigence du framework.
- Unions discriminées pour les états ; `Result`/erreurs typées pour le métier.
- `async/await` ; aucune promesse flottante (`no-floating-promises`).
- Immutabilité : `readonly`, `as const`.

## 6. Spécifique front-end
- Accessibilité **WCAG 2.2 AA** : HTML sémantique, labels, focus visible, contraste, navigation clavier.
- Performance : Core Web Vitals (LCP < 2,5 s, INP < 200 ms, CLS < 0,1) ; lazy loading, images optimisées.
- Composants purs et petits ; la logique va dans des hooks testables.
- Pas de secret côté client ; seules les variables explicitement publiques sont exposées.
- Sécurité : pas de `dangerouslySetInnerHTML` sans assainissement (DOMPurify), CSP stricte.

## 7. Sécurité back-end
- Validation de toute entrée (body, query, params, headers) par schéma.
- `helmet` (ou équivalent), rate limiting, CORS restrictif.
- `pnpm audit` / Dependabot / Renovate ; pas de dépendance non maintenue.

## 8. Anti-patterns à refuser en revue
- Logique métier dans les composants React ou les handlers HTTP.
- `useEffect` pour dériver un état ou pour récupérer des données (utiliser TanStack Query / Server Components).
- Types dupliqués entre front et back au lieu d'un contrat partagé/généré.
- `console.log` laissé en code de production.

## 9. Commandes
```bash
pnpm install --frozen-lockfile
pnpm lint && pnpm typecheck
pnpm test -- --coverage
pnpm build
pnpm audit --prod
```
