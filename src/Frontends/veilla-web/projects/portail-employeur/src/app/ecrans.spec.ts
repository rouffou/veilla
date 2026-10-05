import { TestBed } from '@angular/core/testing';
import { Poste } from './api/bff-employeur.models';
import { ListesPage } from './listes/listes-page';
import { PostesPage } from './postes/postes-page';
import { PropositionPoste } from './postes/proposition-poste';
import { AFFILIE, bff, BFF, providePortailTesting, repondreAffilies } from './testing/bff.testing';
import { TravailleursPage } from './travailleurs/travailleurs-page';

const AFF = `${BFF}/affilies/${AFFILIE.id}`;

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

describe('TravailleursPage (POR-03)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [TravailleursPage],
      providers: providePortailTesting(),
    }),
  );

  it('affiche un tableau à en-têtes, recherche et pagine via le BFF', async () => {
    const fixture = TestBed.createComponent(TravailleursPage);
    await stable(fixture);
    repondreAffilies();
    await stable(fixture);
    const premiere = bff().expectOne((r) => r.url === `${AFF}/travailleurs`);
    expect(premiere.request.params.get('page')).toBe('1');
    premiere.flush({
      elements: [{ id: 't1', nom: 'Dupont', prenom: 'Jean', dateNaissance: '1990-01-01' }],
      total: 45,
      page: 1,
      taille: 20,
    });
    await stable(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(
      [...root.querySelectorAll('thead th[scope="col"]')].map((th) => th.textContent?.trim()),
    ).toEqual(['workers.lastName', 'workers.firstName', 'workers.birthDate']);
    expect(root.querySelector('tbody th[scope="row"]')?.textContent).toContain('Dupont');
    expect(root.querySelector('table caption')).not.toBeNull();
    expect(root.querySelector('label[for="travailleurs-recherche"]')).not.toBeNull();
    expect(root.querySelector('nav[aria-label]')).not.toBeNull();

    const boutons = root.querySelectorAll<HTMLButtonElement>('nav button');
    expect(boutons[0].disabled).toBe(true);
    boutons[1].click();
    await stable(fixture);
    const page2 = bff().expectOne((r) => r.url === `${AFF}/travailleurs`);
    expect(page2.request.params.get('page')).toBe('2');
    page2.flush({ elements: [], total: 45, page: 2, taille: 20 });
    await stable(fixture);

    const champ = root.querySelector<HTMLInputElement>('#travailleurs-recherche')!;
    champ.value = 'hélène';
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    await stable(fixture);
    const recherche = bff().expectOne((r) => r.url === `${AFF}/travailleurs`);
    expect(recherche.request.params.get('recherche')).toBe('hélène');
    expect(recherche.request.params.get('page')).toBe('1');
    recherche.flush({ elements: [], total: 0, page: 1, taille: 20 });
    await stable(fixture);
    expect(root.textContent).toContain('workers.empty');
    bff().verify();
  });
});

const POSTES: Poste[] = [
  {
    id: 'poste-1',
    intitule: 'Soudeur',
    description: null,
    statut: 'Actif',
    expose: true,
    risques: [
      {
        code: 'BRUIT',
        libelle: 'Bruit',
        categorie: 'Physique',
        niveauExposition: 'Eleve',
        exposeDepuis: '2026-01-15',
      },
    ],
  },
  {
    id: 'poste-2',
    intitule: 'Ancien cariste',
    description: null,
    statut: 'Archive',
    expose: false,
    risques: [],
  },
];

describe('PostesPage (POR-03)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({ imports: [PostesPage], providers: providePortailTesting() }),
  );

  it('liste les postes et risques et ne propose la modification que des postes actifs', async () => {
    const fixture = TestBed.createComponent(PostesPage);
    await stable(fixture);
    repondreAffilies();
    await stable(fixture);
    bff()
      .expectOne((r) => r.url === `${AFF}/postes`)
      .flush(POSTES);
    await stable(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
    expect(root.textContent).toContain('Bruit');
    const liens = root.querySelectorAll<HTMLAnchorElement>('a.vl-button');
    expect(liens.length).toBe(1);
    expect(liens[0].getAttribute('href')).toBe('/postes/poste-1/proposition');
  });
});

describe('PropositionPoste (POR-03, AFF-14)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [PropositionPoste],
      providers: providePortailTesting(),
    }),
  );

  async function ouvrir() {
    const fixture = TestBed.createComponent(PropositionPoste);
    fixture.componentRef.setInput('posteId', 'poste-1');
    await stable(fixture);
    bff()
      .expectOne((r) => r.url === `${BFF}/risques`)
      .flush([{ code: 'BRUIT', libelle: 'Bruit', categorie: 'Physique' }]);
    repondreAffilies();
    await stable(fixture);
    bff()
      .expectOne((r) => r.url === `${AFF}/postes`)
      .flush(POSTES);
    await stable(fixture);
    return fixture;
  }

  it('signale les champs manquants, associés à leur message, sans rien envoyer', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('proposal.reasonMissing');
    const motif = root.querySelector<HTMLTextAreaElement>('#proposition-motif')!;
    expect(motif.getAttribute('aria-invalid')).toBe('true');
    const erreurId = motif
      .getAttribute('aria-describedby')!
      .split(' ')
      .find((id) => id.endsWith('erreur'))!;
    expect(root.querySelector(`#${erreurId}`)?.textContent).toContain('form.required');
    expect(root.querySelector('#ligne-risque-0')?.getAttribute('aria-invalid')).toBe('true');
    bff().expectNone(`${AFF}/postes/poste-1/propositions`);
  });

  it('envoie la proposition et affiche l’accusé de soumission', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;
    const choisir = (selecteur: string, valeur: string) => {
      const champ = root.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
        selecteur,
      )!;
      champ.value = valeur;
      champ.dispatchEvent(new Event(champ instanceof HTMLSelectElement ? 'change' : 'input'));
    };
    choisir('#proposition-motif', 'Nouvelle presse bruyante');
    choisir('#proposition-date', '2026-11-01');
    choisir('#ligne-risque-0', 'BRUIT');
    choisir('#ligne-niveau-0', 'Eleve');
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    const req = bff().expectOne(`${AFF}/postes/poste-1/propositions`);
    expect(req.request.body).toEqual({
      motif: 'Nouvelle presse bruyante',
      valideDu: '2026-11-01',
      lignes: [{ type: 'Ajout', risqueCode: 'BRUIT', niveauExposition: 'Eleve' }],
    });
    req.flush({ id: 'prop-1', statut: 'Soumise' }, { status: 201, statusText: 'Created' });
    await stable(fixture);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('proposal.success');
    expect(root.querySelector('form')).toBeNull();
  });

  it('affiche le refus du service sans perdre la saisie', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;
    const motif = root.querySelector<HTMLTextAreaElement>('#proposition-motif')!;
    motif.value = 'x';
    motif.dispatchEvent(new Event('input'));
    const risque = root.querySelector<HTMLSelectElement>('#ligne-risque-0')!;
    risque.value = 'BRUIT';
    risque.dispatchEvent(new Event('change'));
    const niveau = root.querySelector<HTMLSelectElement>('#ligne-niveau-0')!;
    niveau.value = 'Moyen';
    niveau.dispatchEvent(new Event('change'));
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);
    bff()
      .expectOne(`${AFF}/postes/poste-1/propositions`)
      .flush(
        { code: 'proposition.invalide', detail: 'Risque inconnu.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await stable(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Risque inconnu.');
    expect(root.querySelector<HTMLTextAreaElement>('#proposition-motif')!.value).toBe('x');
  });
});

describe('ListesPage (POR-06)', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({ imports: [ListesPage], providers: providePortailTesting() }),
  );

  it('télécharge le CSV d’une version et l’annonce', async () => {
    const creer = vi.fn(() => 'blob:liste');
    const revoquer = vi.fn();
    Object.assign(URL, { createObjectURL: creer, revokeObjectURL: revoquer });
    const clic = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    const fixture = TestBed.createComponent(ListesPage);
    await stable(fixture);
    repondreAffilies();
    await stable(fixture);
    bff()
      .expectOne(`${AFF}/listes-nominatives`)
      .flush([
        {
          id: 'l2',
          type: 'PosteSecurite',
          version: 2,
          dateReference: '2026-09-01',
          dateGeneration: '2026-09-01T08:00:00Z',
          nombreLignes: 4,
          derniere: true,
        },
        {
          id: 'l1',
          type: 'PosteSecurite',
          version: 1,
          dateReference: '2026-01-01',
          dateGeneration: '2026-01-01T08:00:00Z',
          nombreLignes: 3,
          derniere: false,
        },
      ]);
    await stable(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
    expect(root.querySelectorAll('a[href$="/proposition"]').length).toBe(1);
    root.querySelector<HTMLButtonElement>('tbody button')!.click();
    await stable(fixture);
    bff()
      .expectOne((r) => r.url === `${AFF}/listes-nominatives/l2/csv`)
      .flush(new Blob(['Nom;Prénom'], { type: 'text/csv' }), {
        headers: {
          'Content-Disposition':
            "attachment; filename*=UTF-8''liste-nominative-poste-securite-v2-2026-09-01.csv",
        },
      });
    await stable(fixture);

    expect(creer).toHaveBeenCalled();
    expect(clic).toHaveBeenCalled();
    expect(root.querySelector('[aria-live="polite"]')?.textContent).toContain('lists.downloaded');
    clic.mockRestore();
  });
});
