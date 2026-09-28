import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Page d'attente pour une fonctionnalité prévue mais pas encore disponible.
 * Les clés sont passées via `data` de la route (liaison des entrées de composant).
 */
@Component({
  selector: 'vl-placeholder-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t(titleKey()) }}</h1>
      <p class="vl-empty">{{ t('placeholder.notYetAvailable') }}</p>
      @if (requirement(); as req) {
        <p class="vl-card__ref">{{ t('common.requirement') }} : {{ req }}</p>
      }
    </ng-container>
  `,
})
export class PlaceholderPage {
  readonly titleKey = input('placeholder.title');
  readonly requirement = input<string>();
}
