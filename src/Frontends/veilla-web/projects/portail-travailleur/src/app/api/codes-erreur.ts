import { TableCodesErreur } from '@veilla/shared';

/**
 * Codes fonctionnels des services aval dont le message est traduit dans le portail (FR, NL, DE, EN) ; les autres erreurs de
 * validation affichent le détail (français) renvoyé par le service.
 */
export const CODES_ERREUR_TRAVAILLEUR: TableCodesErreur = {
  'rendez-vous.annulation-tardive': 'errors.annulationTardive',
  'creneau.indisponible': 'errors.creneauIndisponible',
  'rendez-vous.deplacement-tardif': 'errors.deplacementTardif',
  'rendez-vous.deplacements-epuises': 'errors.deplacementsEpuises',
  'deplacement.creneau-invalide': 'errors.creneauInvalide',
  'reservation.aucune-obligation': 'errors.aucuneObligation',
  'perimetre.interdit': 'errors.perimetre',
};
