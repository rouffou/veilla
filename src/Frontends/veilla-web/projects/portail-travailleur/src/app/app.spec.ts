import { TestBed } from '@angular/core/testing';
import { provideVeillaTesting } from '@veilla/shared/testing';
import { App } from './app';
import { Dashboard } from './dashboard/dashboard';

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

describe('Dashboard (POR-11 à POR-13)', () => {
  it('affiche des cartes à l’état vide', async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideVeillaTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('section.vl-card').length).toBe(4);
  });
});
