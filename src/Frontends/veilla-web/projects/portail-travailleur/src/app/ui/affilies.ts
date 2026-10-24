import { Employeur, ExamenAPlanifier, RendezVous } from '../api/bff-travailleur.models';

/**
 * Employeurs (affiliés) du travailleur : ses occupations actives (BFF `/employeurs`), puis ceux de ses examens à planifier,
 * de ses rendez-vous et d'un éventuel lien de convocation. Un employeur est nommé par sa dénomination quand le BFF la
 * fournit (Affiliés, POR-11) ; sinon (affilié d'un lien, service indisponible), il est désigné par son rang.
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

/** Dénominations connues par affilié, issues des employeurs du BFF (les employeurs sans dénomination sont absents). */
export function denominationsConnues(employeurs: readonly Employeur[]): ReadonlyMap<string, string> {
  const noms = new Map<string, string>();
  for (const e of employeurs) {
    if (e.denomination) noms.set(e.affilieId, e.denomination);
  }
  return noms;
}
