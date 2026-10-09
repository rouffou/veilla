import { TestBed } from '@angular/core/testing';
import { Accueil, Questionnaire } from './api/bff-travailleur.models';
import { Dashboard } from './dashboard/dashboard';
import { DocumentsPage } from './documents/documents-page';
import { DemandePage } from './questionnaires/demande-page';
import { QuestionnairesPage } from './questionnaires/questionnaires-page';
import { RendezVousPage } from './rendez-vous/rendez-vous-page';
import { AFFILIE, bff, BFF, provideTravailleurTesting, rendezVous, stable } from './testing/bff.testing';

describe('Dashboard (POR-11 à POR-13)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [Dashboard], providers: provideTravailleurTesting() }));

  it('affiche les quatre cartes, et signale une section indisponible sans inventer de donnée', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    await stable(fixture);
    const accueil: Accueil = {
      personneId: 'p-1',
      prochainsRendezVous: [rendezVous('r1', '2026-12-01T09:00:00Z')],
      documentsRecents: null,
      questionnairesDisponibles: 2,
      indisponibles: ['documents'],
    };
    bff().expectOne((r) => r.url === `${BFF}/accueil`).flush(accueil);
    await stable(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('section.vl-card').length).toBe(4);
    expect(root.textContent).toContain('actes.EVALUATION_PERIODIQUE');
    expect(root.textContent).toContain('dashboard.unavailable');
    expect(root.querySelector('h1')?.textContent).toContain('dashboard.title');
  });

  it('propose de réessayer quand le BFF est indisponible', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    await stable(fixture);
    bff().expectOne((r) => r.url === `${BFF}/accueil`).flush({}, { status: 503, statusText: 'Unavailable' });
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('errors.indisponible');
  });
});

describe('RendezVousPage (POR-11)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [RendezVousPage], providers: provideTravailleurTesting() }));

  async function ouvrir(rdv = [rendezVous('r1', '2026-12-01T09:00:00Z'), rendezVous('r2', '2026-09-01T09:00:00Z', 'Termine')]) {
    const fixture = TestBed.createComponent(RendezVousPage);
    await stable(fixture);
    bff().expectOne(`${BFF}/rendez-vous`).flush(rdv);
    await stable(fixture);
    return fixture;
  }

  it('liste les rendez-vous dans un tableau accessible et ne propose l’annulation que pour les planifiés', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('table caption')).not.toBeNull();
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
    expect(root.querySelectorAll('tbody button').length).toBe(1);
    expect(root.textContent).toContain('appointments.statuses.Termine');
  });

  it('annule un rendez-vous puis recharge la liste', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('tbody button')!.click();
    await stable(fixture);

    const post = bff().expectOne((r) => r.method === 'POST' && r.url === `${BFF}/rendez-vous/r1/annulation`);
    post.flush(null, { status: 204, statusText: 'No Content' });
    await stable(fixture);
    bff().expectOne((r) => r.method === 'GET' && r.url === `${BFF}/rendez-vous`).flush([]);
    await stable(fixture);

    expect(root.querySelector('[role="status"]')?.textContent).toContain('appointments.cancelled');
    expect(root.querySelector('[role="status"]')?.textContent).toContain('appointments.moveHint');
  });

  it('traduit le refus d’une annulation tardive', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('tbody button')!.click();
    await stable(fixture);
    bff()
      .expectOne((r) => r.method === 'POST')
      .flush(
        { code: 'rendez-vous.annulation-tardive', detail: 'Message du service.' },
        { status: 409, statusText: 'Conflict' },
      );
    await stable(fixture);

    const alerte = root.querySelector('[role="alert"]')?.textContent;
    expect(alerte).toContain('errors.annulationTardive');
    expect(alerte).not.toContain('Message du service.');
    bff().verify();
  });

  it('cherche les créneaux de l’employeur connu puis réserve celui choisi', async () => {
    const fixture = await ouvrir();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
    await stable(fixture);

    const recherche = bff().expectOne((r) => r.url === `${BFF}/rendez-vous/creneaux`);
    expect(recherche.request.params.get('affilieId')).toBe(AFFILIE);
    expect(recherche.request.params.get('typeActe')).toBe('EVALUATION_PERIODIQUE');
    recherche.flush([{ id: 'c1', lieuId: 'l1', debut: '2026-12-10T09:00:00Z', fin: '2026-12-10T09:30:00Z', typeActe: 'EVALUATION_PERIODIQUE' }]);
    await stable(fixture);

    const boutons = root.querySelectorAll<HTMLButtonElement>('ul button');
    expect(boutons.length).toBe(1);
    boutons[0].click();
    await stable(fixture);
    const post = bff().expectOne((r) => r.method === 'POST' && r.url === `${BFF}/rendez-vous`);
    expect(post.request.body).toEqual({ creneauId: 'c1', affilieId: AFFILIE });
    post.flush({ id: 'r9' }, { status: 201, statusText: 'Created' });
    await stable(fixture);
    bff().expectOne((r) => r.method === 'GET' && r.url === `${BFF}/rendez-vous`).flush([]);
    await stable(fixture);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('appointments.booked');
  });

  it('n’affiche pas de formulaire de réservation quand aucun employeur n’est connu', async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('appointments.noEmployer');
  });
});

const MODELE: Questionnaire = {
  code: 'SANTE-GENERAL',
  version: 1,
  titre: 'Questionnaire de santé',
  questions: [
    { code: 'Q1', libelle: 'Fumez-vous ?', typeReponse: 'OuiNon', obligatoire: true, choix: [] },
    { code: 'Q2', libelle: 'Remarques', typeReponse: 'Texte', obligatoire: false, choix: [] },
  ],
};

describe('QuestionnairesPage (POR-12)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [QuestionnairesPage], providers: provideTravailleurTesting() }));

  async function ouvrir() {
    const fixture = TestBed.createComponent(QuestionnairesPage);
    await stable(fixture);
    bff().expectOne((r) => r.url === `${BFF}/questionnaires`).flush([MODELE]);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('ul button')!.click();
    await stable(fixture);
    return { fixture, root };
  }

  it('signale les réponses obligatoires manquantes sans rien envoyer', async () => {
    const { fixture, root } = await ouvrir();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Fumez-vous ?');
    bff().expectNone((r) => r.method === 'POST');
  });

  it('envoie uniquement les réponses saisies, puis efface le formulaire et accuse réception', async () => {
    const { fixture, root } = await ouvrir();
    const non = root.querySelector<HTMLInputElement>('input[type="radio"][value="NON"]')!;
    non.checked = true;
    non.dispatchEvent(new Event('change'));
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    const post = bff().expectOne((r) => r.method === 'POST' && r.url === `${BFF}/questionnaires/SANTE-GENERAL/reponses`);
    expect(post.request.body).toEqual({ reponses: [{ codeQuestion: 'Q1', valeur: 'NON' }] });
    post.flush({ id: 'q1' }, { status: 201, statusText: 'Created' });
    await stable(fixture);

    expect(root.querySelector('[role="status"]')?.textContent).toContain('questionnaires.success');
    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).not.toContain('NON');
  });
});

describe('DemandePage (POR-12)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [DemandePage], providers: provideTravailleurTesting() }));

  it('envoie le type choisi sans motif pour l’employeur connu', async () => {
    const fixture = TestBed.createComponent(DemandePage);
    await stable(fixture);
    bff().expectOne(`${BFF}/rendez-vous`).flush([rendezVous('r1', '2026-12-01T09:00:00Z')]);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    const pre = root.querySelector<HTMLInputElement>('input[type="radio"][value="VISITE_PRE_REPRISE"]')!;
    pre.checked = true;
    pre.dispatchEvent(new Event('change'));
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);

    const post = bff().expectOne((r) => r.method === 'POST' && r.url === `${BFF}/demandes`);
    expect(post.request.body).toEqual({ affilieId: AFFILIE, type: 'VISITE_PRE_REPRISE' });
    post.flush({ id: 'd1' }, { status: 201, statusText: 'Created' });
    await stable(fixture);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('request.success');
  });

  it('affiche le refus de droits du service sans perdre le formulaire', async () => {
    const fixture = TestBed.createComponent(DemandePage);
    await stable(fixture);
    bff().expectOne(`${BFF}/rendez-vous`).flush([rendezVous('r1', '2026-12-01T09:00:00Z')]);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await stable(fixture);
    bff().expectOne((r) => r.method === 'POST').flush({ code: 'obligation.interdit' }, { status: 403, statusText: 'Forbidden' });
    await stable(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('errors.interdit');
    expect(root.querySelector('form')).not.toBeNull();
  });
});

describe('DocumentsPage (POR-13)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [DocumentsPage], providers: provideTravailleurTesting() }));

  it('liste les documents, sans zone ni empreinte, et télécharge le PDF', async () => {
    const fixture = TestBed.createComponent(DocumentsPage);
    await stable(fixture);
    bff()
      .expectOne(`${BFF}/documents`)
      .flush([{ id: 'd1', categorie: 'evaluation-sante', codeModele: 'SANTE.EVALUATION.TRAVAILLEUR', langue: 'Fr', date: '2026-09-02T08:00:00Z', format: 'PDF/A-1a', taille: 1024 }]);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('table caption')).not.toBeNull();
    expect(root.querySelector('tbody th')?.textContent).toContain('documents.categories.evaluation-sante');
    expect(root.textContent).not.toContain('SANTE.EVALUATION');
    expect(root.textContent).toContain('documents.notYet');

    const crees: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => {
      crees.push(b as Blob);
      return 'blob:test';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    root.querySelector<HTMLButtonElement>('tbody button')!.click();
    await stable(fixture);
    bff()
      .expectOne(`${BFF}/documents/d1/contenu`)
      .flush(new Blob(['%PDF'], { type: 'application/pdf' }), { headers: { 'Content-Disposition': 'attachment; filename=eval.pdf' } });
    await stable(fixture);
    expect(crees.length).toBe(1);
  });

  it('affiche l’état vide', async () => {
    const fixture = TestBed.createComponent(DocumentsPage);
    await stable(fixture);
    bff().expectOne(`${BFF}/documents`).flush([]);
    await stable(fixture);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('documents.empty');
  });
});
