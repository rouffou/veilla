import { computed, inject, Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { catchError, map, Observable, of, shareReplay } from 'rxjs';

export type AuthStatus = 'authenticated' | 'anonymous' | 'error';

const RETURN_URL_KEY = 'veilla.auth.returnUrl';

/** Façade au-dessus d'angular-auth-oidc-client, exposée sous forme de signaux. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly router = inject(Router);

  readonly isAuthenticated = toSignal(
    this.oidc.isAuthenticated$.pipe(map((result) => result.isAuthenticated)),
    { initialValue: false },
  );

  private readonly userData = toSignal(
    this.oidc.userData$.pipe(map((result) => result.userData as Record<string, unknown> | null)),
    { initialValue: null },
  );

  readonly userName = computed<string | null>(() => {
    const data = this.userData();
    const name = data?.['name'] ?? data?.['preferred_username'];
    return typeof name === 'string' ? name : null;
  });

  /** Vérification unique de la session (traite aussi le retour du fournisseur d'identité). */
  private readonly status$: Observable<AuthStatus> = this.oidc.checkAuth().pipe(
    map((result): AuthStatus => (result.isAuthenticated ? 'authenticated' : 'anonymous')),
    catchError((error: unknown) => {
      console.error("Échec de la vérification d'authentification", error);
      return of<AuthStatus>('error');
    }),
    shareReplay(1),
  );

  checkStatus(): Observable<AuthStatus> {
    return this.status$;
  }

  login(returnUrl?: string): void {
    if (returnUrl) {
      try {
        sessionStorage.setItem(RETURN_URL_KEY, returnUrl);
      } catch {
        // Stockage indisponible : l'utilisateur reviendra sur l'accueil.
      }
    }
    // Le document de découverte est chargé d'abord : si le fournisseur d'identité est
    // injoignable, l'utilisateur est dirigé vers une page d'erreur explicite.
    this.oidc.preloadAuthWellKnownDocument().subscribe({
      next: () => this.oidc.authorize(),
      error: (error: unknown) => {
        console.error("Fournisseur d'identité injoignable", error);
        void this.router.navigateByUrl('/erreur-connexion');
      },
    });
  }

  logout(): void {
    this.oidc.logoff().subscribe();
  }

  /** Récupère (et efface) l'URL demandée avant la redirection vers le fournisseur d'identité. */
  consumeReturnUrl(): string | null {
    try {
      const url = sessionStorage.getItem(RETURN_URL_KEY);
      sessionStorage.removeItem(RETURN_URL_KEY);
      return url;
    } catch {
      return null;
    }
  }

  accessToken(): Observable<string | null> {
    return this.oidc.getAccessToken().pipe(catchError(() => of(null)));
  }
}
