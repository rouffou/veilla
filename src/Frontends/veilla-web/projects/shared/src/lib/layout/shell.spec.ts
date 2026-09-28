import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideVeillaTesting } from '@veilla/shared/testing';
import { Shell } from './shell';

@Component({
  imports: [Shell],
  template: `<vl-shell appNameKey="app.name" [navItems]="[{ path: '/', labelKey: 'nav.home', exact: true }]">
    <h1>Contenu</h1>
  </vl-shell>`,
})
class Host {}

describe('Shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideVeillaTesting()],
    }).compileComponents();
  });

  it('expose les repères (banner, navigation, main, contentinfo) et le lien d’évitement en premier', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    const firstLink = root.querySelector('a');
    expect(firstLink?.classList.contains('vl-skip-link')).toBe(true);
    expect(firstLink?.getAttribute('href')).toBe('#contenu-principal');

    expect(root.querySelector('header')).not.toBeNull();
    expect(root.querySelector('nav')?.getAttribute('aria-label')).toBe('shell.mainNavigation');
    const main = root.querySelector('main');
    expect(main?.id).toBe('contenu-principal');
    expect(main?.getAttribute('tabindex')).toBe('-1');
    expect(main?.textContent).toContain('Contenu');
    expect(root.querySelector('footer')).not.toBeNull();
  });

  it('déplace le focus sur le contenu principal via le lien d’évitement', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    document.body.appendChild(root);

    (root.querySelector('.vl-skip-link') as HTMLAnchorElement).click();
    expect(document.activeElement?.id).toBe('contenu-principal');
    root.remove();
  });

  it('propose un sélecteur de langue étiqueté', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const select = root.querySelector('select');
    const label = root.querySelector(`label[for="${select?.id}"]`);
    expect(label?.textContent).toContain('lang.label');
    expect(select?.querySelectorAll('option').length).toBe(4);
  });
});
