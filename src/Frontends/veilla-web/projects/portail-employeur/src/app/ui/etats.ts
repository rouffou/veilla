import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ErreurApi } from '../api/bff-employeur.service';

/** Indication de chargement annoncée aux technologies d'assistance (WCAG 4.1.3). */
@Component({
  selector: 'app-chargement',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="vl-empty" role="status" *transloco="let t">{{ t('ui.loading') }}</p>`,
})
export class Chargement {}

/**
 * Codes fonctionnels des services aval dont le message est traduit dans le portail (FR, NL, DE, EN) ; les autres erreurs de
 * validation affichent le détail (français) renvoyé par le service.
 */
const MESSAGES_PAR_CODE: Readonly<Record<string, string>> = {
  'reprise.occupation-inactive': 'errors.repriseOccupationInactive',
};

/**
 * Message d'erreur d'un appel au BFF (rôle alert) : texte selon la catégorie (service indisponible,
 * accès refusé, introuvable…) et, pour une erreur de validation, le détail renvoyé par le service.
 */
@Component({
  selector: 'app-erreur-api',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-alerte" role="alert" *transloco="let t">
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
  protected readonly cleCode = computed(() => MESSAGES_PAR_CODE[this.erreur().code ?? ''] ?? null);
  readonly reessayable = input(false);
  readonly reessayer = output();
}
