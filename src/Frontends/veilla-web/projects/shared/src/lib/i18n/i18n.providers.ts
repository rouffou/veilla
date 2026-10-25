import { HttpClient } from '@angular/common/http';
import {
  EnvironmentProviders,
  inject,
  Injectable,
  isDevMode,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { TitleStrategy } from '@angular/router';
import { provideTransloco, provideTranslocoScope, Translation, TranslocoLoader } from '@jsverse/transloco';
import { forkJoin, map, Observable } from 'rxjs';
import { AVAILABLE_LANGS, DEFAULT_LANG, VeillaLang } from './langs';
import { LanguageService } from './language.service';
import { TranslatedTitleStrategy } from './translated-title.strategy';

/** Fusion profonde : les clés de `surcharge` l'emportent, sans écraser les sous-objets voisins. */
function fusionner(base: Translation, surcharge: Translation): Translation {
  const resultat: Translation = { ...base };
  for (const [cle, valeur] of Object.entries(surcharge)) {
    const existant = resultat[cle];
    resultat[cle] =
      valeur && typeof valeur === 'object' && existant && typeof existant === 'object'
        ? fusionner(existant as Translation, valeur as Translation)
        : valeur;
  }
  return resultat;
}

/**
 * Charge les traductions :
 * - `fr` (langue seule) : traductions communes (`i18n/shared/<lang>.json`, issues de la bibliothèque partagée)
 *   fusionnées avec celles de l'application (`i18n/<lang>.json`) ;
 * - `<portée>/fr` (portée Transloco, voir `provideVeillaScope`) : `i18n/<portée>/<lang>.json` seul.
 */
@Injectable({ providedIn: 'root' })
export class VeillaTranslocoLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    if (lang.includes('/')) {
      return this.http.get<Translation>(`i18n/${lang}.json`);
    }
    return forkJoin([
      this.http.get<Translation>(`i18n/shared/${lang}.json`),
      this.http.get<Translation>(`i18n/${lang}.json`),
    ]).pipe(map(([shared, app]) => fusionner(shared, app)));
  }
}

/**
 * Déclare une portée i18n (fichiers `i18n/<portée>/{fr,nl,de}.json`) : à placer dans les `providers` d'une route
 * paresseuse. Les fichiers ne sont chargés qu'à l'entrée de la route ; leurs clés sont préfixées par le nom de
 * la portée (`t('planification.titre')`). Mécanisme natif de Transloco (`provideTranslocoScope`).
 */
export function provideVeillaScope(portee: string): EnvironmentProviders {
  return makeEnvironmentProviders([provideTranslocoScope(portee)]);
}

export interface VeillaI18nOptions {
  readonly langs: readonly VeillaLang[];
}

export function provideVeillaI18n(options: VeillaI18nOptions): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        availableLangs: [...options.langs],
        defaultLang: DEFAULT_LANG,
        fallbackLang: DEFAULT_LANG,
        missingHandler: { useFallbackTranslation: true, logMissingKey: isDevMode() },
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: VeillaTranslocoLoader,
    }),
    { provide: AVAILABLE_LANGS, useValue: options.langs },
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideAppInitializer(() => inject(LanguageService).init()),
  ]);
}
