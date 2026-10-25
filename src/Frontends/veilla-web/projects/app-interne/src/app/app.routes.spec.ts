import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ROLES_PAR_FONCTION, ROLES } from './roles';
import { routes } from './app.routes';
import { App } from './app';
import { provideInterneTesting } from './testing/interne.testing';

const FONCTIONS = Object.keys(ROLES_PAR_FONCTION);

describe('routes de l’application interne', () => {
  function configurer(roles: string[]) {
    // Les routes de l’application remplacent le routeur vide de provideVeillaTesting (dernier fournisseur gagnant).
    TestBed.configureTestingModule({
      imports: [App],
      providers: [...provideInterneTesting({ roles }), provideRouter(routes, withComponentInputBinding())],
    });
  }

  it('déclare une route paresseuse par fonction', () => {
    const racine = routes.find((r) => r.path === '' && r.children)?.children ?? [];
    for (const f of FONCTIONS) {
      const route = racine.find((r) => r.path === f);
      expect(route?.loadChildren, f).toBeDefined();
      expect(route?.canActivate?.length, f).toBe(1);
    }
  });

  it('affiche la page « à venir » d’une fonction autorisée, avec sa portée i18n', async () => {
    configurer([ROLES.planificateur]);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/planification');
    expect(TestBed.inject(Router).url).toBe('/planification');
    const h1 = (harness.routeNativeElement as HTMLElement).querySelector('h1');
    expect(h1?.textContent).toContain('planification.title');
  });

  it('redirige vers « accès refusé » sans le rôle requis', async () => {
    configurer([ROLES.planificateur]);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin');
    expect(TestBed.inject(Router).url).toBe('/acces-refuse');
  });

  it('filtre la navigation par rôle', async () => {
    configurer([ROLES.infirmier]);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const liens = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('nav a')).map((a) =>
      a.getAttribute('href'),
    );
    expect(liens).toContain('/consultation');
    expect(liens).toContain('/travail');
    expect(liens).not.toContain('/admin');
    expect(liens).not.toContain('/affilies/x');
  });
});
