import { TestBed } from '@angular/core/testing';
import { provideVeillaTesting } from '@veilla/shared/testing';
import { App } from './app';
import { Dashboard } from './dashboard/dashboard';

describe('App (portail employeur)', () => {
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

  it('rend la coquille accessible avec la navigation du portail', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.vl-skip-link')).not.toBeNull();
    expect(root.querySelector('main#contenu-principal')).not.toBeNull();
    expect(root.querySelectorAll('nav a').length).toBe(5);
  });
});

describe('Dashboard (POR-02)', () => {
  it('affiche six indicateurs à l’état vide', async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideVeillaTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('h1')?.textContent).toContain('dashboard.title');
    expect(root.querySelectorAll('section.vl-card').length).toBe(6);
    expect(root.querySelectorAll('.vl-card__empty').length).toBe(6);
  });
});
