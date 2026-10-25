import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import {
  apiAuthInterceptor,
  INTERNAL_LANGS,
  provideCodesErreurApi,
  provideRuntimeConfig,
  provideVeillaAuth,
  provideVeillaI18n,
  VeillaRuntimeConfig,
} from '@veilla/shared';
import { CODES_ERREUR_INTERNE } from './api/codes-erreur';
import { routes } from './app.routes';

export function buildAppConfig(runtimeConfig: VeillaRuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideRouter(
        routes,
        withComponentInputBinding(),
        withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
      ),
      provideHttpClient(withFetch(), withInterceptors([apiAuthInterceptor])),
      provideRuntimeConfig(runtimeConfig),
      provideVeillaAuth(runtimeConfig),
      provideVeillaI18n({ langs: INTERNAL_LANGS }),
      provideCodesErreurApi(CODES_ERREUR_INTERNE),
    ],
  };
}
