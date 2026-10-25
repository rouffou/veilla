import { TableCodesErreur } from '@veilla/shared';

/**
 * Codes fonctionnels des services dont le message est traduit : `code → clé de traduction globale`
 * (fichiers `public/i18n/<lang>.json`, section `errors`). Les lots y ajoutent leurs lignes ; un code absent
 * affiche le détail (français) renvoyé par le service.
 */
export const CODES_ERREUR_INTERNE: TableCodesErreur = {};
