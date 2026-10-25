import { TestBed } from '@angular/core/testing';
import { ECRANS, attendre, bff, provideInterneTesting, urlService } from '../testing/interne.testing';
import { InterneApi } from './interne-api';

describe('InterneApi', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: provideInterneTesting() }));

  it('construit les adresses des services relayés et des écrans composites', () => {
    const api = TestBed.inject(InterneApi);
    expect(api.urlService('personnes', '/personnes/42')).toBe(urlService('personnes', 'personnes/42'));
    expect(api.urlEcran('travailleurs/42')).toBe(`${ECRANS}/travailleurs/42`);
  });

  it('appelle un service avec ses paramètres (valeurs vides ignorées) et l’en-tête X-Motif-Acces', () => {
    TestBed.inject(InterneApi)
      .service('surveillance-medicale')
      .get('dossiers/1/consultation', { params: { page: 2, q: '', x: null }, motifAcces: 'consultation' })
      .subscribe();
    const requete = bff().expectOne((r) => r.url === urlService('surveillance-medicale', 'dossiers/1/consultation'));
    expect(requete.request.params.get('page')).toBe('2');
    expect(requete.request.params.has('q')).toBe(false);
    expect(requete.request.params.has('x')).toBe(false);
    expect(requete.request.headers.get('X-Motif-Acces')).toBe('consultation');
    requete.flush({});
  });

  it('n’ajoute pas l’en-tête sans motif et appelle un écran composite', () => {
    TestBed.inject(InterneApi).ecran('recherche', { params: { q: 'dupont' } }).subscribe();
    const requete = attendre(`${ECRANS}/recherche`);
    expect(requete.request.headers.has('X-Motif-Acces')).toBe(false);
    expect(requete.request.params.get('q')).toBe('dupont');
    requete.flush({});
  });
});
