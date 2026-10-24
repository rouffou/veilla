import { Employeur } from '../api/bff-travailleur.models';
import { affiliesConnus, denominationsConnues } from './affilies';

const employeur = (affilieId: string, denomination: string | null): Employeur => ({
  affilieId,
  denomination,
  langue: denomination ? 'Fr' : null,
  typeTravailleur: 'Salarie',
  dateDebut: '2025-01-01',
  dateFin: null,
});

describe('affiliesConnus et denominationsConnues (POR-11)', () => {
  it('ne retient que les dénominations fournies, sans inventer celles des affiliés issus d’un lien', () => {
    const noms = denominationsConnues([employeur('a', 'Boulangerie Dupont'), employeur('b', null), employeur('c', '')]);

    expect([...noms.entries()]).toEqual([['a', 'Boulangerie Dupont']]);
    expect(noms.get('lien')).toBeUndefined();
  });

  it('liste chaque affilié une fois : employeurs, examens, rendez-vous puis lien', () => {
    const ids = affiliesConnus([employeur('a', 'Dupont')], [{ affilieId: 'a' } as never, { affilieId: 'b' } as never], 'c');

    expect(ids).toEqual(['a', 'b', 'c']);
  });
});
