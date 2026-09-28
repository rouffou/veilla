/*
 * Utilitaires de test de @veilla/shared (point d'entrée secondaire @veilla/shared/testing).
 */
import { EnvironmentProviders, importProvidersFrom, Provider, signal } from '@angular/core';
import { provideRouter, Routes } from '@angular/router';
import { Translation, TranslocoTestingModule } from '@jsverse/transloco';
import {
  AuthService,
  AuthStatus,
  AVAILABLE_LANGS,
  PORTAL_LANGS,
  RUNTIME_CONFIG,
  VeillaLang,
  VeillaRuntimeConfig,
} from '@veilla/shared';
import { Observable, of } from 'rxjs';

export const TEST_RUNTIME_CONFIG: VeillaRuntimeConfig = {
  apiBaseUrl: 'https://api.example.test/bff',
  auth: { authority: 'https://idp.example.test/realms/veilla', clientId: 'test', scope: 'openid profile' },
};

/** Double de test d'AuthService : aucun appel au fournisseur d'identité. */
export class FakeAuthService implements Pick<AuthService, 'checkStatus' | 'login' | 'logout' | 'consumeReturnUrl' | 'accessToken'> {
  readonly isAuthenticated = signal(false);
  readonly userName = signal<string | null>(null);
  status: AuthStatus = 'authenticated';
  loginCalls: (string | undefined)[] = [];

  checkStatus(): Observable<AuthStatus> {
    return of(this.status);
  }
  login(returnUrl?: string): void {
    this.loginCalls.push(returnUrl);
  }
  logout(): void {
    this.isAuthenticated.set(false);
  }
  consumeReturnUrl(): string | null {
    return null;
  }
  accessToken(): Observable<string | null> {
    return of(null);
  }
}

export interface VeillaTestingOptions {
  readonly routes?: Routes;
  readonly langs?: readonly VeillaLang[];
  /** Traductions de test ; par défaut vides (les clés sont alors rendues telles quelles). */
  readonly translations?: Record<string, Translation>;
}

/** Fournisseurs communs pour tester les composants des applications. */
export function provideVeillaTesting(options: VeillaTestingOptions = {}): (Provider | EnvironmentProviders)[] {
  const langs = options.langs ?? PORTAL_LANGS;
  return [
    provideRouter(options.routes ?? []),
    importProvidersFrom(
      TranslocoTestingModule.forRoot({
        langs: options.translations ?? { fr: {} },
        translocoConfig: { availableLangs: [...langs], defaultLang: 'fr', missingHandler: { logMissingKey: false } },
        preloadLangs: true,
      }),
    ),
    { provide: AVAILABLE_LANGS, useValue: langs },
    { provide: RUNTIME_CONFIG, useValue: TEST_RUNTIME_CONFIG },
    { provide: AuthService, useClass: FakeAuthService },
  ];
}
