import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { switchMap, take } from 'rxjs';
import { RUNTIME_CONFIG } from '../config/runtime-config';
import { AuthService } from './auth.service';

/** Indique si `url` cible l'API (BFF) configurée. Les URL relatives sont résolues sur `base`. */
export function isApiUrl(
  url: string,
  apiBaseUrl: string,
  base: string = globalThis.location?.href ?? 'http://localhost/',
): boolean {
  try {
    const target = new URL(url, base);
    const api = new URL(apiBaseUrl, base);
    if (target.origin !== api.origin) return false;
    const apiPath = api.pathname.replace(/\/+$/, '');
    return apiPath === '' || target.pathname === apiPath || target.pathname.startsWith(`${apiPath}/`);
  } catch {
    return false;
  }
}

/** Ajoute le jeton d'accès (Bearer) aux seules requêtes destinées au BFF. */
export const apiAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const config = inject(RUNTIME_CONFIG);
  const auth = inject(AuthService);

  if (!isApiUrl(req.url, config.apiBaseUrl)) {
    return next(req);
  }
  return auth.accessToken().pipe(
    take(1),
    switchMap((token) =>
      next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req),
    ),
  );
};
