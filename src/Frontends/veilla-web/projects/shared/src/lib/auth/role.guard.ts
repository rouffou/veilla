import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';
import { possedeUnRole } from './roles';

/** Chemin de la page d'accès refusé (route partagée, voir `SHARED_PUBLIC_ROUTES`). */
export const ACCES_REFUSE_PATH = '/acces-refuse';

/**
 * Route réservée aux utilisateurs possédant au moins un des rôles donnés (à placer après `authGuard`).
 * Sans rôle exigé, la route reste ouverte. Sinon, redirection vers la page d'accès refusé.
 * Le contrôle d'interface ne remplace pas celui des services : il évite seulement d'afficher un écran inutilisable.
 */
export function roleGuard(...roles: readonly string[]): CanActivateFn {
  return () => {
    const router = inject(Router);
    return inject(AuthService)
      .rolesApresVerification()
      .pipe(map((possedes) => (possedeUnRole(possedes, roles) ? true : router.parseUrl(ACCES_REFUSE_PATH))));
  };
}
