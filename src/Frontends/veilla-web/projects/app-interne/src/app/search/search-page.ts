import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Résultats de la recherche globale. Le service de recherche n'est pas encore branché :
 * la page l'indique explicitement plutôt que d'afficher des résultats fictifs.
 */
@Component({
  selector: 'app-search-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('search.title') }}</h1>
      @if (q(); as query) {
        <p>{{ t('search.resultsFor', { query }) }}</p>
        <p class="vl-empty" role="status">{{ t('search.notConnected') }}</p>
      } @else {
        <p class="vl-empty">{{ t('search.emptyQuery') }}</p>
      }
    </ng-container>
  `,
})
export class SearchPage {
  /** Paramètre de requête `q` (liaison des entrées de composant). */
  readonly q = input<string | undefined>();
}
