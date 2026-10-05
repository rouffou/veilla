import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { BffEmployeurService } from '../api/bff-employeur.service';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

/**
 * Postes et risques de l'affilié (POR-03, AFF-10, AFF-11) en lecture ; chaque poste actif peut faire
 * l'objet d'une proposition de modification soumise au CPMT (AFF-14).
 */
@Component({
  selector: 'app-postes-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    EtatAffilie,
    Chargement,
    ErreurApiMessage,
    DateLocalePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('positions.title') }}</h1>
      <p>{{ t('positions.intro') }}</p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        @let etat = postes.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api
              [erreur]="etat.erreur"
              [reessayable]="true"
              (reessayer)="postes.recharger()"
            />
          }
          @case ('ok') {
            @if (etat.valeur.length === 0) {
              <p class="vl-empty">{{ t('positions.empty') }}</p>
            } @else {
              <div class="app-table-wrapper">
                <table class="app-table">
                  <caption class="vl-visually-hidden">
                    {{
                      t('positions.caption')
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ t('positions.position') }}</th>
                      <th scope="col">{{ t('positions.status') }}</th>
                      <th scope="col">{{ t('positions.risks') }}</th>
                      <th scope="col">
                        <span class="vl-visually-hidden">{{ t('positions.actions') }}</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (p of etat.valeur; track p.id) {
                      <tr>
                        <th scope="row">
                          {{ p.intitule }}
                          @if (p.description) {
                            <span class="app-aide">{{ p.description }}</span>
                          }
                        </th>
                        <td>
                          {{ t('enums.statutPoste.' + p.statut) }}
                          @if (p.expose) {
                            <span class="app-badge">{{ t('positions.exposed') }}</span>
                          }
                        </td>
                        <td>
                          @if (p.risques.length === 0) {
                            {{ t('positions.noRisk') }}
                          } @else {
                            <ul class="app-liste">
                              @for (r of p.risques; track r.code) {
                                <li>
                                  {{ r.libelle }} —
                                  {{ t('enums.niveauExposition.' + r.niveauExposition) }}
                                  <span class="app-aide">{{
                                    t('positions.since', {
                                      date: (r.exposeDepuis | dateLocale: langue()),
                                    })
                                  }}</span>
                                </li>
                              }
                            </ul>
                          }
                        </td>
                        <td>
                          @if (p.statut === 'Actif') {
                            <a
                              class="vl-button vl-button--secondary"
                              [routerLink]="['/postes', p.id, 'proposition']"
                            >
                              {{ t('positions.propose')
                              }}<span class="vl-visually-hidden"> — {{ p.intitule }}</span>
                            </a>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          }
        }
      }
    </ng-container>
  `,
})
export class PostesPage {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  protected readonly langue = inject(LanguageService).current;

  // Les libellés des risques dépendent de la langue : rechargés quand elle change.
  protected readonly postes = parAffilie((id) => this.api.postes(id), this.langue);
}
