import { TableCodesErreur } from '@veilla/shared';

/**
 * Codes fonctionnels des services aval dont le message est traduit dans le portail (FR, NL, DE, EN) ; les autres erreurs de
 * validation affichent le détail (français) renvoyé par le service.
 */
export const CODES_ERREUR_EMPLOYEUR: TableCodesErreur = {
  'reprise.occupation-inactive': 'errors.repriseOccupationInactive',
};
