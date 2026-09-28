import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LANGUAGE_NAMES, LanguageService } from './language.service';

let nextId = 0;

/** Sélecteur de langue accessible : liste native étiquetée, noms de langue dans leur propre langue. */
@Component({
  selector: 'vl-language-selector',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="vl-lang" *transloco="let t">
      <label class="vl-lang__label" [for]="selectId">{{ t('lang.label') }}</label>
      <select
        class="vl-lang__select"
        [id]="selectId"
        [value]="languages.current()"
        (change)="onChange($event)"
      >
        @for (lang of languages.available; track lang) {
          <option [value]="lang" [attr.lang]="lang" [selected]="lang === languages.current()">
            {{ names[lang] }}
          </option>
        }
      </select>
    </div>
  `,
})
export class LanguageSelector {
  protected readonly languages = inject(LanguageService);
  protected readonly names = LANGUAGE_NAMES;
  protected readonly selectId = `vl-lang-${nextId++}`;

  protected onChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (this.languages.isAvailable(value)) {
      this.languages.use(value);
    }
  }
}
