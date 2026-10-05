import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { AffilieContext } from './affilie-context';

/**
 * Sélecteur de l'affilié consulté (en-tête), affiché seulement si l'utilisateur en représente plusieurs.
 * `<select>` natif étiqueté : utilisable au clavier et annoncé correctement (WCAG 1.3.1, 2.1.1, 4.1.2).
 */
@Component({
  selector: 'app-affilie-selector',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      @if (contexte.affilies(); as affilies) {
        @if (affilies.length > 1) {
          <div class="vl-lang">
            <label for="app-affilie-select">{{ t('affiliate.selectorLabel') }}</label>
            <select
              id="app-affilie-select"
              class="vl-lang__select"
              [value]="contexte.affilieId()"
              (change)="choisir($event)"
            >
              @for (a of affilies; track a.id) {
                <option [value]="a.id" [selected]="a.id === contexte.affilieId()">
                  {{ a.denomination }} ({{ a.numeroBce }})
                </option>
              }
            </select>
          </div>
        } @else if (affilies.length === 1) {
          <span class="vl-header__user">{{ affilies[0].denomination }}</span>
        }
      }
    </ng-container>
  `,
})
export class AffilieSelector {
  protected readonly contexte = inject(AffilieContext);

  protected choisir(event: Event): void {
    this.contexte.choisir((event.target as HTMLSelectElement).value);
  }
}

/** États communs des écrans liés à un affilié : chargement de la liste, erreur, aucun affilié rattaché. */
@Component({
  selector: 'app-etat-affilie',
  imports: [TranslocoDirective, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      @if (contexte.erreur(); as erreur) {
        <app-erreur-api [erreur]="erreur" [reessayable]="true" (reessayer)="contexte.charger()" />
      } @else if (!contexte.charge()) {
        <app-chargement />
      } @else if (!contexte.affilieId()) {
        <p class="vl-empty">{{ t('affiliate.none') }}</p>
      }
    </ng-container>
  `,
})
export class EtatAffilie {
  protected readonly contexte = inject(AffilieContext);

  constructor() {
    this.contexte.charger();
  }
}
