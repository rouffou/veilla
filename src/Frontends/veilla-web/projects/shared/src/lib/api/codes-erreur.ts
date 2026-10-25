import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';

/** Table `code fonctionnel → clé de traduction` (ex. `'perimetre.interdit' → 'errors.perimetre'`). */
export type TableCodesErreur = Readonly<Record<string, string>>;

/**
 * Tables des codes fonctionnels dont le message est traduit. Chaque application déclare la sienne
 * avec `provideCodesErreurApi` ; les autres erreurs de validation affichent le détail (français) du service.
 * Plusieurs tables peuvent être fournies au même niveau d'injecteur : elles sont fusionnées (la dernière l'emporte).
 */
export const CODES_ERREUR_API = new InjectionToken<readonly TableCodesErreur[]>('VEILLA_CODES_ERREUR_API');

export function provideCodesErreurApi(table: TableCodesErreur): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: CODES_ERREUR_API, multi: true, useValue: table }]);
}
