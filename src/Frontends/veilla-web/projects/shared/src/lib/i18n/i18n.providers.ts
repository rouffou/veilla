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
import { provideTransloco, Translation, TranslocoLoader } from '@jsverse/transloco';
import { forkJoin, map, Observable } from 'rxjs';
import { AVAILABLE_LANGS, DEFAULT_LANG, VeillaLang } from './langs';
import { LanguageService } from './language.service';
import { TranslatedTitleStrategy } from './translated-title.strategy';

/**
 * Charge et fusionne les traductions communes (`i18n/shared/<lang>.json`, issues de la
 * bibliothèque partagée) et celles de l'application (`i18n/<lang>.json`).
 */
@Injectable({ providedIn: 'root' })
export class VeillaTranslocoLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return forkJoin([
      this.http.get<Translation>(`i18n/shared/${lang}.json`),
      this.http.get<Translation>(`i18n/${lang}.json`),
    ]).pipe(map(([shared, app]) => ({ ...shared, ...app })));
  }
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
