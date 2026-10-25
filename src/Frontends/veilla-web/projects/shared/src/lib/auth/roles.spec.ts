import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { FakeAuthService, provideVeillaTesting } from '@veilla/shared/testing';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { ACCES_REFUSE_PATH, roleGuard } from './role.guard';
import { extraireRoles, possedeUnRole } from './roles';
import { SiRole } from './si-role';
import { filtrerNavigation } from '../layout/shell';

describe('extraireRoles', () => {
  it('lit le claim roles (tableau ou chaîne) et à défaut realm_access.roles', () => {
    expect(extraireRoles({ roles: ['cpmt', 'offline_access'] })).toEqual(['cpmt', 'offline_access']);
    expect(extraireRoles({ roles: 'cpmt' })).toEqual(['cpmt']);
    expect(extraireRoles({ realm_access: { roles: ['infirmier'] } })).toEqual(['infirmier']);
    expect(extraireRoles({ roles: [1, 'dpo'] })).toEqual(['dpo']);
    expect(extraireRoles(null)).toEqual([]);
    expect(extraireRoles({})).toEqual([]);
  });

  it('possedeUnRole : aucun rôle exigé = ouvert', () => {
    expect(possedeUnRole([], undefined)).toBe(true);
    expect(possedeUnRole([], [])).toBe(true);
    expect(possedeUnRole(['cpmt'], ['infirmier', 'cpmt'])).toBe(true);
    expect(possedeUnRole(['cpmt'], ['infirmier'])).toBe(false);
  });
});

describe('roleGuard', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: provideVeillaTesting() }));

  function executer(...roles: string[]) {
    return TestBed.runInInjectionContext(() => roleGuard(...roles)({} as never, {} as never));
  }

  it('laisse passer un utilisateur ayant un des rôles', async () => {
    (TestBed.inject(AuthService) as unknown as FakeAuthService).roles.set(['cpmt']);
    expect(await firstValueFrom(executer('infirmier', 'cpmt') as never)).toBe(true);
  });

  it('redirige vers la page d’accès refusé sinon', async () => {
    const resultat = await firstValueFrom(executer('dpo') as never);
    expect(TestBed.inject(Router).serializeUrl(resultat as never)).toBe(ACCES_REFUSE_PATH);
  });
});

@Component({
  imports: [SiRole],
  template: `<p id="cpmt" *vlSiRole="'cpmt'">A</p><p id="liste" *vlSiRole="['dpo', 'infirmier']">B</p>`,
})
class Hote {
  readonly inutile = signal(0);
}

describe('*vlSiRole', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [Hote], providers: provideVeillaTesting() }));

  it('affiche ou masque selon les rôles, et réagit à leur changement', async () => {
    const auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
    auth.roles.set(['infirmier']);
    const fixture = TestBed.createComponent(Hote);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('#cpmt')).toBeNull();
    expect(root.querySelector('#liste')).not.toBeNull();

    auth.roles.set(['cpmt']);
    await fixture.whenStable();
    expect(root.querySelector('#cpmt')).not.toBeNull();
    expect(root.querySelector('#liste')).toBeNull();
  });
});

describe('filtrerNavigation', () => {
  it('ne garde que les entrées visibles pour les rôles', () => {
    const items = [
      { path: '/', labelKey: 'a' },
      { path: '/x', labelKey: 'b', roles: ['cpmt'] },
      { path: '/y', labelKey: 'c', roles: ['dpo'] },
    ];
    expect(filtrerNavigation(items, ['dpo']).map((i) => i.path)).toEqual(['/', '/y']);
  });
});
