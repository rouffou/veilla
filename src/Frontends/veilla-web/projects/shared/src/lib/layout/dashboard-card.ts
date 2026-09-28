import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

let nextId = 0;

/**
 * Carte de tableau de bord. Tant qu'aucune donnée n'est branchée, elle affiche un état vide
 * explicite (aucune donnée fictive n'est présentée comme réelle).
 */
@Component({
  selector: 'vl-dashboard-card',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="vl-card" [attr.aria-labelledby]="headingId" *transloco="let t">
      <h2 class="vl-card__title" [id]="headingId">{{ t(titleKey()) }}</h2>
      @if (descriptionKey(); as description) {
        <p class="vl-card__description">{{ t(description) }}</p>
      }
      <ng-content>
        <p class="vl-card__empty">{{ t(emptyKey()) }}</p>
      </ng-content>
      @if (requirement(); as req) {
        <p class="vl-card__ref"><span class="vl-visually-hidden">{{ t('common.requirement') }} </span>{{ req }}</p>
      }
    </section>
  `,
})
export class DashboardCard {
  readonly titleKey = input.required<string>();
  readonly descriptionKey = input<string>();
  readonly emptyKey = input('common.noDataYet');
  /** Référence d'exigence du cahier des charges (ex. POR-02), affichée discrètement. */
  readonly requirement = input<string>();
  protected readonly headingId = `vl-card-${nextId++}`;
}
