import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideVeillaTesting } from '@veilla/shared/testing';
import { INTERNAL_LANGS } from './langs';
import { LanguageService } from './language.service';

describe('LanguageService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideVeillaTesting({ langs: INTERNAL_LANGS })] });
  });

  it('change la langue active, l’attribut lang du document et mémorise le choix', () => {
    const service = TestBed.inject(LanguageService);
    service.use('nl');
    expect(service.current()).toBe('nl');
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('nl');
    expect(document.documentElement.lang).toBe('nl');
    expect(localStorage.getItem('veilla.lang')).toBe('nl');
  });

  it('ignore une langue non proposée (EN pour l’application interne)', () => {
    const service = TestBed.inject(LanguageService);
    service.use('fr');
    service.use('en');
    expect(service.current()).toBe('fr');
  });

  it('reprend la langue mémorisée au démarrage', async () => {
    localStorage.setItem('veilla.lang', 'de');
    const service = TestBed.inject(LanguageService);
    await service.init();
    expect(service.current()).toBe('de');
  });
});
