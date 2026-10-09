import { TestBed } from '@angular/core/testing';
import { provideVeillaTesting } from '@veilla/shared/testing';
import de from '../../public/i18n/de.json';
import en from '../../public/i18n/en.json';
import fr from '../../public/i18n/fr.json';
import nl from '../../public/i18n/nl.json';
import { App } from './app';

describe('App (portail travailleur)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideVeillaTesting()],
    }).compileComponents();
  });

  it('crée l’application', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('propose les quatre langues du portail (POR-14)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const options = Array.from(root.querySelectorAll('select option')).map((o) => o.getAttribute('lang'));
    expect(options).toEqual(['fr', 'nl', 'de', 'en']);
  });
});

/** POR-14 : mêmes clés dans les quatre langues, aucune traduction vide. */
describe('Libellés du portail travailleur (POR-14)', () => {
  function cles(objet: unknown, prefixe = ''): string[] {
    return Object.entries(objet as Record<string, unknown>).flatMap(([cle, valeur]) =>
      typeof valeur === 'object' && valeur !== null ? cles(valeur, `${prefixe}${cle}.`) : [`${prefixe}${cle}`],
    );
  }

  function valeurs(objet: unknown): string[] {
    return Object.values(objet as Record<string, unknown>).flatMap((v) =>
      typeof v === 'object' && v !== null ? valeurs(v) : [String(v)],
    );
  }

  const traductions: Record<string, unknown> = { fr, nl, de, en };
  const lire = (langue: string): unknown => traductions[langue];

  it.each(['nl', 'de', 'en'])('la langue %s a exactement les clés du français', (langue) => {
    expect(cles(lire(langue)).sort()).toEqual(cles(lire('fr')).sort());
  });

  it.each(['fr', 'nl', 'de', 'en'])('la langue %s n’a aucun libellé vide', (langue) => {
    expect(valeurs(lire(langue)).filter((v) => v.trim() === '')).toEqual([]);
  });
});
