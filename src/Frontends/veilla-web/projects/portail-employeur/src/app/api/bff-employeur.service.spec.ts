import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, toArray } from 'rxjs';
import { AFFILIE, bff, BFF, providePortailTesting } from '../testing/bff.testing';
import { BffEmployeurService, charger, nomDeFichier, versErreurApi } from './bff-employeur.service';

describe('BffEmployeurService', () => {
  let api: BffEmployeurService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: providePortailTesting() });
    api = TestBed.inject(BffEmployeurService);
  });

  afterEach(() => bff().verify());

  it('appelle les routes orientées écran du BFF sous /api/v1', () => {
    api.fiche(AFFILIE.id).subscribe();
    api.tableauDeBord(AFFILIE.id).subscribe();
    bff().expectOne(`${BFF}/affilies/${AFFILIE.id}`).flush({});
    bff().expectOne(`${BFF}/affilies/${AFFILIE.id}/tableau-de-bord`).flush({});
  });

  it('transmet recherche et pagination des travailleurs', () => {
    api.travailleurs(AFFILIE.id, '  dupont ', 2, 20).subscribe();
    const req = bff().expectOne((r) => r.url === `${BFF}/affilies/${AFFILIE.id}/travailleurs`);
    expect(req.request.params.get('recherche')).toBe('dupont');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('taille')).toBe('20');
    req.flush({ elements: [], total: 0, page: 2, taille: 20 });
  });

  it('omet la recherche vide', () => {
    api.travailleurs(AFFILIE.id, ' ', 1, 20).subscribe();
    const req = bff().expectOne((r) => r.url.endsWith('/travailleurs'));
    expect(req.request.params.has('recherche')).toBe(false);
    req.flush({ elements: [], total: 0, page: 1, taille: 20 });
  });

  it('demande les libellés dans la langue active', () => {
    api.postes(AFFILIE.id).subscribe();
    const req = bff().expectOne((r) => r.url === `${BFF}/affilies/${AFFILIE.id}/postes`);
    expect(req.request.params.get('langue')).toBe('fr');
    req.flush([]);
  });

  it('télécharge le CSV avec le nom de fichier de Content-Disposition', async () => {
    const fichier = firstValueFrom(api.telechargerListeCsv(AFFILIE.id, 'liste-1'));
    const req = bff().expectOne(
      (r) => r.url === `${BFF}/affilies/${AFFILIE.id}/listes-nominatives/liste-1/csv`,
    );
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['Nom;Prénom'], { type: 'text/csv' }), {
      headers: {
        'Content-Disposition':
          "attachment; filename=x.csv; filename*=UTF-8''liste-nominative-poste-securite-v2-2026-09-01.csv",
      },
    });
    expect((await fichier).nom).toBe('liste-nominative-poste-securite-v2-2026-09-01.csv');
  });

  it('envoie la proposition de poste au BFF', () => {
    const corps = {
      motif: 'Bruit',
      valideDu: '2026-11-01',
      lignes: [{ type: 'Ajout' as const, risqueCode: 'BRUIT', niveauExposition: 'Eleve' as const }],
    };
    api.proposerPoste(AFFILIE.id, 'poste-1', corps).subscribe();
    const req = bff().expectOne(`${BFF}/affilies/${AFFILIE.id}/postes/poste-1/propositions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(corps);
    req.flush({ id: 'p-1', statut: 'Soumise' }, { status: 201, statusText: 'Created' });
  });
});

describe('versErreurApi', () => {
  const erreur = (status: number, error: unknown = null) =>
    new HttpErrorResponse({ status, error });

  it('distingue indisponibilité, refus, absence, validation et réseau', () => {
    expect(versErreurApi(erreur(503, { code: 'service-aval.indisponible' }))).toEqual({
      type: 'indisponible',
      status: 503,
      code: 'service-aval.indisponible',
      detail: null,
    });
    expect(versErreurApi(erreur(403)).type).toBe('interdit');
    expect(versErreurApi(erreur(404)).type).toBe('introuvable');
    expect(
      versErreurApi(erreur(400, { code: 'proposition.invalide', detail: 'Motif obligatoire.' }))
        .detail,
    ).toBe('Motif obligatoire.');
    expect(versErreurApi(erreur(0)).type).toBe('reseau');
    expect(versErreurApi(new Error('x')).type).toBe('inattendue');
  });
});

describe('charger', () => {
  it('passe par l’état de chargement puis la valeur', async () => {
    const etats = await firstValueFrom(charger(of(42)).pipe(toArray()));
    expect(etats).toEqual([{ statut: 'chargement' }, { statut: 'ok', valeur: 42 }]);
  });
});

describe('nomDeFichier', () => {
  it('lit filename* (RFC 5987) puis filename', () => {
    expect(nomDeFichier("attachment; filename*=UTF-8''liste%20nominative.csv")).toBe(
      'liste nominative.csv',
    );
    expect(nomDeFichier('attachment; filename="liste.csv"')).toBe('liste.csv');
    expect(nomDeFichier(null)).toBeNull();
  });
});
