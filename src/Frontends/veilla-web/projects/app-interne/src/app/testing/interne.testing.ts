/*
 * Utilitaires des tests de l'application interne : BFF interne simulé par HttpTestingController (aucun appel réseau),
 * langues FR/NL/DE, rôles simulés. Les clés de traduction sont rendues telles quelles (sauf `translations`).
 */
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Routes } from '@angular/router';
import { Translation } from '@jsverse/transloco';
import { AuthService, INTERNAL_LANGS, provideCodesErreurApi, TableCodesErreur } from '@veilla/shared';
import { FakeAuthService, provideVeillaTesting, TEST_RUNTIME_CONFIG } from '@veilla/shared/testing';
import { ServiceAval } from '../api/interne-api';

/** Racine des écrans composites : `${apiBaseUrl}/api/v1/ecrans`. */
export const ECRANS = `${TEST_RUNTIME_CONFIG.apiBaseUrl}/api/v1/ecrans`;

/** Adresse d'une route relayée d'un service : `${apiBaseUrl}/{service}/api/v1/{chemin}`. */
export function urlService(service: ServiceAval, chemin: string): string {
  return `${TEST_RUNTIME_CONFIG.apiBaseUrl}/${service}/api/v1/${chemin}`;
}

export interface InterneTestingOptions {
  readonly routes?: Routes;
  /** Rôles de l'utilisateur simulé (claim `roles`) ; aucun par défaut. */
  readonly roles?: readonly string[];
  /** Traductions de test par langue (ou `portée/langue`). */
  readonly translations?: Record<string, Translation>;
  /** Table des codes d'erreur traduits (voir `provideCodesErreurApi`). */
  readonly codesErreur?: TableCodesErreur;
}

/**
 * Fournisseurs communs des tests de l'application interne. Les rôles se changent ensuite avec `definirRoles`.
 */
export function provideInterneTesting(options: InterneTestingOptions = {}): (Provider | EnvironmentProviders)[] {
  const fournisseurs: (Provider | EnvironmentProviders)[] = [
    ...provideVeillaTesting({ langs: INTERNAL_LANGS, routes: options.routes, translations: options.translations }),
    provideHttpClient(),
    provideHttpClientTesting(),
  ];
  if (options.codesErreur) fournisseurs.push(provideCodesErreurApi(options.codesErreur));
  if (options.roles) {
    const roles = options.roles;
    fournisseurs.push({
      provide: AuthService,
      useFactory: () => {
        const auth = new FakeAuthService();
        auth.roles.set(roles);
        return auth;
      },
    });
  }
  return fournisseurs;
}

/** Change les rôles de l'utilisateur simulé (le double d'AuthService doit être celui de `provideVeillaTesting`). */
export function definirRoles(...roles: string[]): void {
  (TestBed.inject(AuthService) as unknown as FakeAuthService).roles.set(roles);
}

export function bff(): HttpTestingController {
  return TestBed.inject(HttpTestingController);
}

/** Attend l'unique requête vers `url` (sans les paramètres) et la renvoie pour `flush`. */
export function attendre(url: string): TestRequest {
  return bff().expectOne((r) => r.url === url);
}

export async function stable(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await fixture.whenStable();
  await fixture.whenStable();
}
