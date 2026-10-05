import { TestBed } from '@angular/core/testing';
import { AffilieContext } from './affilie/affilie-context';
import { AffilieSelector } from './affilie/affilie-selector';
import { App } from './app';
import { Dashboard } from './dashboard/dashboard';
import { TableauDeBord } from './api/bff-employeur.models';
import { AFFILIE, bff, BFF, providePortailTesting, repondreAffilies } from './testing/bff.testing';

beforeEach(() => {
  try {
    localStorage.clear();
  } catch {
    // jsdom sans stockage : sans objet.
  }
});

describe('App (portail employeur)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: providePortailTesting(),
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
    expect(root.querySelectorAll('nav a').length).toBe(8);
  });
});

describe('AffilieSelector (POR-01)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [AffilieSelector],
      providers: providePortailTesting(),
    }),
  );

  it('propose un sélecteur étiqueté quand l’utilisateur représente plusieurs affiliés', async () => {
    const autre = { ...AFFILIE, id: 'autre', denomination: 'Atelier Lambert' };
    TestBed.inject(AffilieContext).charger();
    repondreAffilies([AFFILIE, autre]);
    const fixture = TestBed.createComponent(AffilieSelector);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const select = root.querySelector('select')!;
    expect(root.querySelector(`label[for="${select.id}"]`)).not.toBeNull();
    expect(select.options.length).toBe(2);

    select.value = 'autre';
    select.dispatchEvent(new Event('change'));
    expect(TestBed.inject(AffilieContext).affilieId()).toBe('autre');
    expect(localStorage.getItem('veilla.employeur.affilie')).toBe('autre');
  });

  it('affiche simplement la dénomination s’il n’y a qu’un affilié', async () => {
    TestBed.inject(AffilieContext).charger();
    repondreAffilies();
    const fixture = TestBed.createComponent(AffilieSelector);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('select')).toBeNull();
    expect(root.textContent).toContain('Boulangerie Dupont');
  });
});

describe('Dashboard (POR-02)', () => {
  const tableau: TableauDeBord = {
    affilieId: AFFILIE.id,
    date: '2026-10-05',
    indicateurs: [
      { code: 'travailleurs', disponible: true, valeur: 12, raison: null },
      { code: 'postesExposes', disponible: true, valeur: 3, raison: null },
      {
        code: 'propositionsEnAttente',
        disponible: false,
        valeur: null,
        raison: 'service-indisponible',
      },
      { code: 'examensDus', disponible: false, valeur: null, raison: 'fonctionnalite-a-venir' },
      { code: 'soldeUnites', disponible: false, valeur: null, raison: 'fonctionnalite-a-venir' },
    ],
  };

  beforeEach(() =>
    TestBed.configureTestingModule({ imports: [Dashboard], providers: providePortailTesting() }),
  );

  it('affiche les compteurs réels et annonce les autres comme bientôt disponibles', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    repondreAffilies();
    await fixture.whenStable();
    bff().expectOne(`${BFF}/affilies/${AFFILIE.id}/tableau-de-bord`).flush(tableau);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('h1')?.textContent).toContain('dashboard.title');
    expect(root.querySelectorAll('section.vl-card').length).toBe(5);
    expect([...root.querySelectorAll('.app-compteur')].map((e) => e.textContent?.trim())).toEqual([
      '12',
      '3',
    ]);
    expect(root.textContent).toContain('dashboard.unavailable');
    expect(root.querySelectorAll('.vl-card__empty').length).toBe(3);
    bff().verify();
  });

  it('signale une erreur du BFF et permet de réessayer', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    repondreAffilies();
    await fixture.whenStable();
    bff()
      .expectOne(`${BFF}/affilies/${AFFILIE.id}/tableau-de-bord`)
      .flush(
        { code: 'service-aval.indisponible' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('errors.indisponible');
    (root.querySelector('[role="alert"] button') as HTMLButtonElement).click();
    await fixture.whenStable();
    bff().expectOne(`${BFF}/affilies/${AFFILIE.id}/tableau-de-bord`).flush(tableau);
  });

  it('indique l’absence d’affilié rattaché au compte', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    repondreAffilies([]);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('affiliate.none');
    bff().verify();
  });
});
