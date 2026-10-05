import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { BffEmployeurService } from '../api/bff-employeur.service';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

export const TAILLE_PAGE = 20;

/**
 * Travailleurs de l'affilié (POR-03, AFF-20 à AFF-23) : recherche par nom ou prénom et pagination côté BFF.
 * Le NISS n'est jamais affiché ni même reçu (DAT-06).
 */
@Component({
  selector: 'app-travailleurs-page',
  imports: [TranslocoDirective, EtatAffilie, Chargement, ErreurApiMessage, DateLocalePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('workers.title') }}</h1>
      <p>{{ t('workers.intro') }}</p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        <form role="search" class="app-recherche" (submit)="rechercher($event, champ.value)">
          <label for="travailleurs-recherche">{{ t('workers.search') }}</label>
          <input
            #champ
            id="travailleurs-recherche"
            type="search"
            class="app-input"
            autocomplete="off"
            aria-describedby="travailleurs-recherche-aide"
            [value]="recherche()"
          />
          <button type="submit" class="vl-button">{{ t('workers.searchButton') }}</button>
          <p id="travailleurs-recherche-aide" class="app-aide">{{ t('workers.searchHint') }}</p>
        </form>

        @let etat = travailleurs.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api
              [erreur]="etat.erreur"
              [reessayable]="true"
              (reessayer)="travailleurs.recharger()"
            />
          }
          @case ('ok') {
            @let p = etat.valeur;
            <p role="status" class="app-aide">{{ t('ui.results', { total: p.total }) }}</p>
            @if (p.elements.length === 0) {
              <p class="vl-empty">{{ t('workers.empty') }}</p>
            } @else {
              <div class="app-table-wrapper">
                <table class="app-table">
                  <caption class="vl-visually-hidden">
                    {{
                      t('workers.caption')
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ t('workers.lastName') }}</th>
                      <th scope="col">{{ t('workers.firstName') }}</th>
                      <th scope="col">{{ t('workers.birthDate') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (w of p.elements; track w.id) {
                      <tr>
                        <th scope="row">{{ w.nom }}</th>
                        <td>{{ w.prenom }}</td>
                        <td>{{ w.dateNaissance | dateLocale: langue() }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              @let nbPages = pages(p.total, p.taille);
              @if (nbPages > 1) {
                <nav class="app-pagination" [attr.aria-label]="t('ui.pagination')">
                  <button
                    type="button"
                    class="vl-button vl-button--secondary"
                    [disabled]="p.page <= 1"
                    (click)="allerA(p.page - 1)"
                  >
                    {{ t('ui.previous') }}
                  </button>
                  <span aria-current="page">{{
                    t('ui.pageInfo', { page: p.page, pages: nbPages })
                  }}</span>
                  <button
                    type="button"
                    class="vl-button vl-button--secondary"
                    [disabled]="p.page >= nbPages"
                    (click)="allerA(p.page + 1)"
                  >
                    {{ t('ui.next') }}
                  </button>
                </nav>
              }
            }
          }
        }
      }
    </ng-container>
  `,
})
export class TravailleursPage {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  protected readonly langue = inject(LanguageService).current;

  protected readonly recherche = signal('');
  private readonly page = signal(1);
  private readonly parametres = computed(() => ({
    recherche: this.recherche(),
    page: this.page(),
  }));

  protected readonly travailleurs = parAffilie(
    (id, p: { recherche: string; page: number }) =>
      this.api.travailleurs(id, p.recherche, p.page, TAILLE_PAGE),
    this.parametres,
  );

  protected rechercher(event: Event, terme: string): void {
    event.preventDefault();
    this.page.set(1);
    this.recherche.set(terme.trim());
  }

  protected allerA(page: number): void {
    this.page.set(page);
  }

  protected pages(total: number, taille: number): number {
    return Math.max(1, Math.ceil(total / taille));
  }
}
