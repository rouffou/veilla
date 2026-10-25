/*
 * API publique de la bibliothèque partagée @veilla/shared.
 */

// Configuration d'exécution (CTR-05)
export * from './lib/config/runtime-config';

// Authentification OIDC (code flow + PKCE)
export * from './lib/auth/auth.providers';
export * from './lib/auth/auth.service';
export * from './lib/auth/auth.guard';
export * from './lib/auth/auth.interceptor';
export * from './lib/auth/roles';
export * from './lib/auth/role.guard';
export * from './lib/auth/si-role';

// Appels d'API : états de chargement, erreurs, codes traduits, dates localisées
export * from './lib/api/erreur-api';
export * from './lib/api/chargeable';
export * from './lib/api/codes-erreur';
export * from './lib/api/etats';
export * from './lib/api/date-locale.pipe';

// Internationalisation (NF-40, POR-14)
export * from './lib/i18n/langs';
export * from './lib/i18n/i18n.providers';
export * from './lib/i18n/language.service';
export * from './lib/i18n/language-selector';
export * from './lib/i18n/translated-title.strategy';

// Accessibilité et mise en page (NF-50)
export * from './lib/a11y/skip-link';
export * from './lib/layout/shell';
export * from './lib/layout/dashboard-card';
export * from './lib/pages/placeholder-page';
export * from './lib/pages/accessibility-page';
export * from './lib/pages/auth-error-page';
export * from './lib/pages/access-denied-page';
export * from './lib/routes';
