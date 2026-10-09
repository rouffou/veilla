import { TestBed } from '@angular/core/testing';
import { Reprise } from '../api/bff-employeur.models';
import { AFFILIE, bff, BFF, providePortailTesting, repondreAffilies } from '../testing/bff.testing';
import { ReprisesPage } from './reprises-page';

const AFF = `${BFF}/affilies/${AFFILIE.id}`;

const REPRISES: Reprise[] = [
  {
    id: 'r1',
    personneId: 't1',
    dateReprise: '2026-10-12',
    debutAbsence: '2026-08-01',
    statut: 'Convoquee',
    dateLimite: '2026-10-26',
    enRetard: false,
    horsDelai: false,
  },
  {
    id: 'r2',
    personneId: 't-inconnu',
    dateReprise: '2026-09-01',
    debutAbsence: '2026-07-01',
    statut: 'NouveauStatutDuService',
    dateLimite: '2026-09-15',
    enRetard: true,
    horsDelai: false,
  },
];

async function stable(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await fixture.whenStable();
  await fixture.whenStable();
}

beforeEach(() => {
  try {
    localStorage.clear();
  } catch {
    // sans objet
  }
});

describe('ReprisesPage (POR-04)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({ imports: [ReprisesPage], providers: providePortailTesting() }),
  );

  async function ouvrir(reprises: readonly Reprise[] = REPRISES) {
    const fixture = TestBed.createComponent(ReprisesPage);
    await stable(fixture);
    repondreAffilies();
    await stable(fixture);
    bff().expectOne(`${AFF}/reprises`).flush(reprises);
    bff()
      .expectOne((r) => r.url === `${AFF}/travailleurs`)
      .flush({
        elements: [{ id: 't1', nom: 'Dupont', prenom: 'Jean', dateNaissance: '1990-01-01' }],
        total: 1,
        page: 1,
        taille: 100,
      });
    await stable(fixture);
    return fixture;
  }

  function saisir(root: HTMLElement, selecteur: string, valeur: string): void {
    const champ = root.querySelector<HTMLInputElement | HTMLSelectElement>(selecteur)!;
    champ.value = valeur;
    champ.dispatchEvent(new Event(champ instanceof HTMLSelectElement ? 'change' : 'input'));
  }

  it('affiche le suivi avec statut compréhensible et date limite, sans donnée médicale', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;

    const lignes = root.querySelectorAll('tbody tr');
    expect(lignes.length).toBe(2);
    expect(root.querySelector('table caption')).not.toBeNull();
    expect(
      [...root.querySelectorAll('thead th[scope="col"]')].map((th) => th.textContent?.trim()),
    ).toEqual([
      'resumptions.worker',
      'resumptions.resumptionDate',
      'resumptions.absenceStart',
      'resumptions.status',
      'resumptions.deadline',
    ]);
    expect(lignes[0].querySelector('th')?.textContent).toContain('Dupont');
    expect(lignes[0].textContent).toContain('enums.statutReprise.Convoquee');
    expect(lignes[0].textContent).not.toContain('resumptions.overdue');
    // Travailleur hors des 100 premiers et statut inconnu : repli sans clé brute ni identifiant.
    expect(lignes[1].querySelector('th')?.textContent).toContain('—');
    expect(lignes[1].textContent).toContain('enums.statutReprise.Inconnu');
    expect(lignes[1].textContent).toContain('resumptions.overdue');
    expect(root.textContent).not.toContain('NouveauStatutDuService');
    expect(root.textContent).not.toContain('t-inconnu');
    bff().verify();
  });

  it('signale les champs manquants sans rien envoyer', async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('resumptions.empty');

    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    const alerte = root.querySelector('[role="alert"]')?.textContent;
    expect(alerte).toContain('resumptions.workerMissing');
    expect(alerte).toContain('resumptions.absenceStartMissing');
    expect(root.querySelector('#reprise-travailleur')?.getAttribute('aria-invalid')).toBe('true');
    bff().expectNone(`${AFF}/reprises`);
  });

  it('annonce la reprise, affiche l’accusé puis recharge le suivi', async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    saisir(root, '#reprise-travailleur', 't1');
    saisir(root, '#reprise-date', '2026-10-12');
    saisir(root, '#reprise-absence', '2026-08-01');
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    const post = bff().expectOne((r) => r.method === 'POST' && r.url === `${AFF}/reprises`);
    expect(post.request.body).toEqual({
      personneId: 't1',
      dateReprise: '2026-10-12',
      debutAbsence: '2026-08-01',
    });
    post.flush(
      { repriseId: 'r1', cree: true, statut: 'ObligationOuverte' },
      { status: 201, statusText: 'Created' },
    );
    await stable(fixture);

    // Le suivi est rechargé après l'annonce.
    bff()
      .expectOne((r) => r.method === 'GET' && r.url === `${AFF}/reprises`)
      .flush(REPRISES);
    bff()
      .expectOne((r) => r.url === `${AFF}/travailleurs`)
      .flush({ elements: [], total: 0, page: 1, taille: 100 });
    await stable(fixture);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('resumptions.success');
    expect(root.querySelector('[role="status"]')?.textContent).toContain(
      'enums.statutReprise.ObligationOuverte',
    );
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
  });

  it('affiche le refus (409/422) du service sans perdre la saisie', async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    saisir(root, '#reprise-travailleur', 't1');
    saisir(root, '#reprise-absence', '2026-08-01');
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);
    bff()
      .expectOne((r) => r.method === 'POST' && r.url === `${AFF}/reprises`)
      .flush(
        { code: 'reprise.invalide', detail: 'La date de reprise est invalide.' },
        { status: 422, statusText: 'Unprocessable Entity' },
      );
    await stable(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'La date de reprise est invalide.',
    );
    expect(root.querySelector<HTMLInputElement>('#reprise-absence')!.value).toBe('2026-08-01');
    bff().verify();
  });

  it("affiche un message traduit quand le travailleur n'a pas d'occupation active (reprise.occupation-inactive)", async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    saisir(root, '#reprise-travailleur', 't1');
    saisir(root, '#reprise-absence', '2026-08-01');
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);
    bff()
      .expectOne((r) => r.method === 'POST' && r.url === `${AFF}/reprises`)
      .flush(
        { code: 'reprise.occupation-inactive', detail: 'Message français du service.' },
        { status: 422, statusText: 'Unprocessable Entity' },
      );
    await stable(fixture);

    const alerte = root.querySelector('[role="alert"]');
    expect(alerte?.textContent).toContain('errors.repriseOccupationInactive');
    expect(alerte?.textContent).not.toContain('Message français du service.');
    expect(root.querySelector<HTMLInputElement>('#reprise-absence')!.value).toBe('2026-08-01');
    bff().verify();
  });
});
