import { RendezVous } from '../api/bff-travailleur.models';

/**
 * Employeurs (affiliés) connus du travailleur : ceux de ses rendez-vous. Aucun service n'expose au rôle travailleur la liste
 * de ses employeurs ni leur dénomination (lacune documentée dans le README du BFF) : ils sont donc désignés par leur rang.
 */
export function affiliesConnus(rendezVous: readonly RendezVous[], parametre?: string | null): readonly string[] {
  const ids = new Set(rendezVous.map((r) => r.affilieId));
  if (parametre) ids.add(parametre);
  return [...ids];
}
