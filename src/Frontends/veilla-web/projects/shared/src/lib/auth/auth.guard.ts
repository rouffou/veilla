import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';

/** Route réservée aux utilisateurs authentifiés ; sinon redirection OIDC (code flow + PKCE). */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.checkStatus().pipe(
    map((status) => {
      if (status === 'authenticated') {
        const returnUrl = auth.consumeReturnUrl();
        return returnUrl && returnUrl !== state.url && returnUrl.startsWith('/') && !returnUrl.startsWith('//')
          ? router.parseUrl(returnUrl)
          : true;
      }
      if (status === 'error') {
        return router.parseUrl('/erreur-connexion');
      }
      auth.login(state.url);
      return false;
    }),
  );
};
