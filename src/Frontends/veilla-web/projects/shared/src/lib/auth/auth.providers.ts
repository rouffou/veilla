import { EnvironmentProviders, isDevMode } from '@angular/core';
import { LogLevel, OpenIdConfiguration, provideAuth } from 'angular-auth-oidc-client';
import { VeillaRuntimeConfig } from '../config/runtime-config';

/** Construit la configuration OIDC (code flow + PKCE) à partir de la configuration d'exécution. */
export function buildOidcConfig(config: VeillaRuntimeConfig, origin: string): OpenIdConfiguration {
  return {
    authority: config.auth.authority,
    clientId: config.auth.clientId,
    scope: config.auth.scope,
    // Authorization Code + PKCE (PKCE est systématique pour le code flow dans cette bibliothèque).
    responseType: 'code',
    redirectUrl: origin,
    postLogoutRedirectUri: origin,
    silentRenew: true,
    useRefreshToken: true,
    renewTimeBeforeTokenExpiresInSeconds: 30,
    ignoreNonceAfterRefresh: true,
    // Le jeton est ajouté par notre propre intercepteur (apiAuthInterceptor).
    secureRoutes: [],
    logLevel: isDevMode() ? LogLevel.Warn : LogLevel.Error,
  };
}

export function provideVeillaAuth(
  config: VeillaRuntimeConfig,
  origin: string = globalThis.location?.origin ?? '',
): EnvironmentProviders {
  return provideAuth({ config: buildOidcConfig(config, origin) });
}
