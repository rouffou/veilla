import { TestBed } from '@angular/core/testing';
import { DemandeCopie } from '../api/bff-travailleur.models';
import { bff, BFF, provideTravailleurTesting, stable } from '../testing/bff.testing';
import { CopieDossier } from './copie-dossier';

function demande(statut: DemandeCopie['statut'], extra: Partial<DemandeCopie> = {}): DemandeCopie {
  return {
    id: `dc-${statut}`,
    deposeeLe: '2026-10-01T08:00:00Z',
    dateLimite: '2026-10-16',
    statut,
    enRetard: false,
    motifRefus: null,
    documentId: null,
    publieeLe: null,
    ...extra,
  };
}

describe('CopieDossier (POR-13, SAN-43)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [CopieDossier], providers: provideTravailleurTesting() }));

  async function ouvrir(demandes: DemandeCopie[]) {
    const fixture = TestBed.createComponent(CopieDossier);
    await stable(fixture);
    bff().expectOne(`${BFF}/demandes-copie-dossier`).flush(demandes);
    await stable(fixture);
    return fixture;
  }

  it('dépose une demande sans corps ni identifiant, puis recharge le suivi', async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('documents.copy.empty');

    root.querySelector<HTMLButtonElement>('button')!.click();
    await stable(fixture);
    const post = bff().expectOne((r) => r.method === 'POST' && r.url === `${BFF}/demandes-copie-dossier`);
    expect(post.request.body).toBeNull();
    post.flush(demande('Deposee'), { status: 201, statusText: 'Created' });
    await stable(fixture);
    bff().expectOne((r) => r.method === 'GET' && r.url === `${BFF}/demandes-copie-dossier`).flush([demande('Deposee')]);
    await stable(fixture);

    expect(root.querySelector('[role="status"]')?.textContent).toContain('documents.copy.success');
    expect(root.textContent).toContain('documents.copy.statuses.Deposee');
    expect(root.textContent).toContain('documents.copy.inProgress');
    expect(root.querySelector('button')).toBeNull();
    bff().verify();
  });

  it('affiche le retard, le refus codé et la copie publiée, dans un tableau accessible', async () => {
    const fixture = await ouvrir([
      demande('Refusee', { id: 'a', motifRefus: 'AucunDossier' }),
      demande('Publiee', { id: 'b', documentId: 'doc-1' }),
    ]);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('table caption')?.textContent).toContain('documents.copy.caption');
    expect(root.textContent).toContain('documents.copy.refusals.AucunDossier');
    expect(root.textContent).toContain('documents.copy.statuses.Publiee');
    expect(root.querySelector('button')).not.toBeNull();
  });

  it('signale un délai dépassé', async () => {
    const fixture = await ouvrir([demande('Deposee', { enRetard: true })]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('documents.copy.late');
  });

  it('traduit le refus d’une seconde demande en cours', async () => {
    const fixture = await ouvrir([]);
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('button')!.click();
    await stable(fixture);
    bff()
      .expectOne((r) => r.method === 'POST')
      .flush({ code: 'demande-copie.en-cours', detail: 'Message du service.' }, { status: 409, statusText: 'Conflict' });
    await stable(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('documents.copy.inProgress');
    expect(root.textContent).not.toContain('Message du service.');
  });
});
