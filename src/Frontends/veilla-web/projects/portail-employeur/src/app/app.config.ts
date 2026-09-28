import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import {
  apiAuthInterceptor,
  PORTAL_LANGS,
  provideRuntimeConfig,
  provideVeillaAuth,
  provideVeillaI18n,
  VeillaRuntimeConfig,
} from '@veilla/shared';
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
      provideVeillaI18n({ langs: PORTAL_LANGS }),
    ],
  };
}
