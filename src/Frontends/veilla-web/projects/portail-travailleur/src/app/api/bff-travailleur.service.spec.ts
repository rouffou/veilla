import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, toArray } from 'rxjs';
import { AFFILIE, bff, BFF, provideTravailleurTesting } from '../testing/bff.testing';
import { BffTravailleurService, charger, nomDeFichier, versErreurApi } from './bff-travailleur.service';

describe('BffTravailleurService', () => {
  let api: BffTravailleurService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideTravailleurTesting() });
    api = TestBed.inject(BffTravailleurService);
  });

  afterEach(() => bff().verify());

  it('appelle les routes orientées écran du BFF sous /api/v1, sans identifiant de personne', () => {
    api.rendezVous().subscribe();
    api.documents().subscribe();
    bff().expectOne(`${BFF}/rendez-vous`).flush([]);
    bff().expectOne(`${BFF}/documents`).flush([]);
  });

  it('demande les libellés dans la langue active', () => {
    api.questionnaires().subscribe();
    const req = bff().expectOne((r) => r.url === `${BFF}/questionnaires`);
    expect(req.request.params.get('langue')).toBe('fr');
    req.flush([]);
  });

  it('cherche les créneaux pour l’employeur et le type demandés', () => {
    api
      .creneaux(AFFILIE, 'EVALUATION_PERIODIQUE', new Date('2026-12-01T00:00:00Z'), new Date('2026-12-31T00:00:00Z'))
      .subscribe();
    const req = bff().expectOne((r) => r.url === `${BFF}/rendez-vous/creneaux`);
    expect(req.request.params.get('affilieId')).toBe(AFFILIE);
    expect(req.request.params.get('typeActe')).toBe('EVALUATION_PERIODIQUE');
    expect(req.request.params.get('du')).toBe('2026-12-01T00:00:00.000Z');
    req.flush([]);
  });

  it('réserve et annule par POST', () => {
    api.reserver('c-1', AFFILIE).subscribe();
    const reservation = bff().expectOne(`${BFF}/rendez-vous`);
    expect(reservation.request.method).toBe('POST');
    expect(reservation.request.body).toEqual({ creneauId: 'c-1', affilieId: AFFILIE });
    reservation.flush({ id: 'r-1' }, { status: 201, statusText: 'Created' });

    api.annuler('r-1').subscribe();
    const annulation = bff().expectOne(`${BFF}/rendez-vous/r-1/annulation`);
    expect(annulation.request.method).toBe('POST');
    annulation.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('envoie les réponses du questionnaire sans identifiant de personne', () => {
    api.remplirQuestionnaire('SANTE-GENERAL', [{ codeQuestion: 'Q1', valeur: 'NON' }]).subscribe();
    const req = bff().expectOne(`${BFF}/questionnaires/SANTE-GENERAL/reponses`);
    expect(req.request.body).toEqual({ reponses: [{ codeQuestion: 'Q1', valeur: 'NON' }] });
    req.flush({ id: 'q-1' }, { status: 201, statusText: 'Created' });
  });

  it('envoie une demande sans motif', () => {
    api.demander(AFFILIE, 'VISITE_PRE_REPRISE').subscribe();
    const req = bff().expectOne(`${BFF}/demandes`);
    expect(req.request.body).toEqual({ affilieId: AFFILIE, type: 'VISITE_PRE_REPRISE' });
    req.flush({ id: 'd-1' }, { status: 201, statusText: 'Created' });
  });

  it('télécharge un document avec le nom de fichier de Content-Disposition', async () => {
    const fichier = firstValueFrom(api.telechargerDocument('d-1'));
    const req = bff().expectOne(`${BFF}/documents/d-1/contenu`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF'], { type: 'application/pdf' }), {
      headers: { 'Content-Disposition': "attachment; filename*=UTF-8''evaluation-sant%C3%A9.pdf" },
    });
    expect((await fichier).nom).toBe('evaluation-santé.pdf');
  });
});

describe('versErreurApi', () => {
  const erreur = (status: number, error: unknown = null) => new HttpErrorResponse({ status, error });

  it('distingue indisponibilité, refus, absence, validation et réseau', () => {
    expect(versErreurApi(erreur(503)).type).toBe('indisponible');
    expect(versErreurApi(erreur(403)).type).toBe('interdit');
    expect(versErreurApi(erreur(404)).type).toBe('introuvable');
    expect(versErreurApi(erreur(409)).type).toBe('invalide');
    expect(versErreurApi(erreur(0)).type).toBe('reseau');
    expect(versErreurApi(new Error('x')).type).toBe('inattendue');
  });

  it('relaie le code et le détail du service', () => {
    const e = versErreurApi(erreur(409, { code: 'creneau.indisponible', detail: 'Indisponible.' }));
    expect(e.code).toBe('creneau.indisponible');
    expect(e.detail).toBe('Indisponible.');
  });
});

describe('charger', () => {
  it('émet chargement puis la valeur', async () => {
    expect(await firstValueFrom(charger(of(1)).pipe(toArray()))).toEqual([
      { statut: 'chargement' },
      { statut: 'ok', valeur: 1 },
    ]);
  });
});

describe('nomDeFichier', () => {
  it('lit filename simple et absence d’en-tête', () => {
    expect(nomDeFichier('attachment; filename="a.pdf"')).toBe('a.pdf');
    expect(nomDeFichier(null)).toBeNull();
  });
});
