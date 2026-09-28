import { TestBed } from '@angular/core/testing';
import { INTERNAL_LANGS } from '@veilla/shared';
import { provideVeillaTesting } from '@veilla/shared/testing';
import { App } from './app';
import { resolveShortcut } from './shortcuts/keyboard-shortcuts';

describe('App (application interne)', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideVeillaTesting({ langs: INTERNAL_LANGS })],
    }).compileComponents();
  });

  it('crée l’application', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('propose FR, NL et DE uniquement (NF-40)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const langs = Array.from(root.querySelectorAll('select option')).map((o) => o.getAttribute('lang'));
    expect(langs).toEqual(['fr', 'nl', 'de']);
  });

  it('place le focus dans la recherche globale avec Ctrl+K (NF-51)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    document.body.appendChild(root);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true }));
    expect(document.activeElement?.id).toBe('app-global-search');
    expect(root.querySelector('form[role="search"]')).not.toBeNull();
    root.remove();
  });
});

describe('resolveShortcut', () => {
  const key = (init: KeyboardEventInit, target?: HTMLElement) => {
    const event = new KeyboardEvent('keydown', init);
    if (target) Object.defineProperty(event, 'target', { value: target });
    return event;
  };

  it('reconnaît Ctrl+K même pendant la saisie', () => {
    expect(resolveShortcut(key({ key: 'k', ctrlKey: true }, document.createElement('input')), true)).toBe('focus-search');
  });

  it('ignore les raccourcis à une touche dans un champ ou lorsqu’ils sont désactivés (WCAG 2.1.4)', () => {
    expect(resolveShortcut(key({ key: '/' }), true)).toBe('focus-search');
    expect(resolveShortcut(key({ key: '?' }), true)).toBe('show-help');
    expect(resolveShortcut(key({ key: '/' }, document.createElement('textarea')), true)).toBeNull();
    expect(resolveShortcut(key({ key: '/' }), false)).toBeNull();
  });
});
