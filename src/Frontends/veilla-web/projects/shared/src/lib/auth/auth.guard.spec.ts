import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { FakeAuthService, provideVeillaTesting } from '@veilla/shared/testing';
import { firstValueFrom, Observable } from 'rxjs';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

function runGuard(url: string): Promise<boolean | UrlTree> {
  const result = TestBed.runInInjectionContext(() =>
    authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
  ) as Observable<boolean | UrlTree>;
  return firstValueFrom(result);
}

describe('authGuard', () => {
  let auth: FakeAuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideVeillaTesting()] });
    auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
  });

  it('laisse passer un utilisateur authentifié', async () => {
    auth.status = 'authenticated';
    expect(await runGuard('/')).toBe(true);
  });

  it('déclenche la connexion OIDC en mémorisant la page demandée', async () => {
    auth.status = 'anonymous';
    expect(await runGuard('/demandes')).toBe(false);
    expect(auth.loginCalls).toEqual(['/demandes']);
  });

  it('redirige vers la page d’erreur si le fournisseur d’identité est injoignable', async () => {
    auth.status = 'error';
    const result = await runGuard('/');
    expect(result instanceof UrlTree && result.toString()).toBe('/erreur-connexion');
  });
});
