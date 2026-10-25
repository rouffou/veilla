import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CODES_ERREUR_API } from './codes-erreur';
import { ErreurApi } from './erreur-api';

/** Indication de chargement annoncée aux technologies d'assistance (WCAG 4.1.3). */
@Component({
  selector: 'vl-chargement',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="vl-empty" role="status" *transloco="let t">{{ t('ui.loading') }}</p>`,
})
export class Chargement {}

/**
 * Message d'erreur d'un appel d'API (rôle alert) : texte du code fonctionnel connu (table injectée, voir
 * `provideCodesErreurApi`) ou de la catégorie (service indisponible, accès refusé, introuvable…) et, pour
 * une erreur de validation, le détail renvoyé par le service.
 */
@Component({
  selector: 'vl-erreur-api',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="vl-alerte" role="alert" *transloco="let t">
      @if (cleCode(); as cle) {
        <p>{{ t(cle) }}</p>
      } @else {
        <p>{{ t('errors.' + erreur().type) }}</p>
        @if (erreur().type === 'invalide' && erreur().detail; as detail) {
          <p lang="fr">{{ detail }}</p>
        }
      }
      @if (reessayable()) {
        <button type="button" class="vl-button vl-button--secondary" (click)="reessayer.emit()">
          {{ t('ui.retry') }}
        </button>
      }
    </div>
  `,
})
export class ErreurApiMessage {
  readonly erreur = input.required<ErreurApi>();
  readonly reessayable = input(false);
  readonly reessayer = output();

  private readonly codes: Record<string, string> = Object.assign(
    {},
    ...(inject(CODES_ERREUR_API, { optional: true }) ?? []),
  );
  protected readonly cleCode = computed(() => this.codes[this.erreur().code ?? ''] ?? null);
}
