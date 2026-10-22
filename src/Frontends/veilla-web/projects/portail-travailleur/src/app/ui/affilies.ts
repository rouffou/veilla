import { Employeur, ExamenAPlanifier, RendezVous } from '../api/bff-travailleur.models';

/**
 * Employeurs (affiliés) du travailleur : ses occupations actives (BFF `/employeurs`), puis ceux de ses examens à planifier,
 * de ses rendez-vous et d'un éventuel lien de convocation. Aucun service n'expose au rôle travailleur la dénomination de
 * l'employeur : ils sont désignés par leur rang.
 */
export function affiliesConnus(
  employeurs: readonly Employeur[],
  rendezVous: readonly RendezVous[],
  parametre?: string | null,
  examens: readonly ExamenAPlanifier[] = [],
): readonly string[] {
  const ids = new Set<string>([
    ...employeurs.map((e) => e.affilieId),
    ...examens.map((e) => e.affilieId),
    ...rendezVous.map((r) => r.affilieId),
  ]);
  if (parametre) ids.add(parametre);
  return [...ids];
}
