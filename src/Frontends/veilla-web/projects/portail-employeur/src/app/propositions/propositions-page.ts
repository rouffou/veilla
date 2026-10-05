import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { BffEmployeurService } from '../api/bff-employeur.service';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

/** POR-03 : suivi des propositions soumises au CPMT (soumise, validée, refusée avec motif). */
@Component({
  selector: 'app-propositions-page',
  imports: [TranslocoDirective, EtatAffilie, Chargement, ErreurApiMessage, DateLocalePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('proposals.title') }}</h1>
      <p>{{ t('proposals.intro') }}</p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        @let etat = propositions.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api
              [erreur]="etat.erreur"
              [reessayable]="true"
              (reessayer)="propositions.recharger()"
            />
          }
          @case ('ok') {
            @if (etat.valeur.length === 0) {
              <p class="vl-empty">{{ t('proposals.empty') }}</p>
            } @else {
              <div class="app-table-wrapper">
                <table class="app-table">
                  <caption class="vl-visually-hidden">
                    {{
                      t('proposals.caption')
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ t('proposals.date') }}</th>
                      <th scope="col">{{ t('proposals.nature') }}</th>
                      <th scope="col">{{ t('proposals.target') }}</th>
                      <th scope="col">{{ t('proposals.reason') }}</th>
                      <th scope="col">{{ t('proposals.status') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (p of etat.valeur; track p.id) {
                      <tr>
                        <th scope="row">{{ p.dateProposition | dateLocale: langue() : true }}</th>
                        <td>{{ t('enums.natureProposition.' + p.nature) }}</td>
                        <td>{{ p.cible }}</td>
                        <td>{{ p.motif }}</td>
                        <td>
                          <span
                            class="app-badge"
                            [class.app-badge--refus]="p.statut === 'Refusee'"
                            >{{ t('enums.statutProposition.' + p.statut) }}</span
                          >
                          @if (p.dateDecision) {
                            <span class="app-aide">{{
                              p.dateDecision | dateLocale: langue()
                            }}</span>
                          }
                          @if (p.motifRefus) {
                            <span class="app-aide">{{
                              t('proposals.refusal', { motif: p.motifRefus })
                            }}</span>
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
export class PropositionsPage {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  protected readonly langue = inject(LanguageService).current;

  protected readonly propositions = parAffilie((id) => this.api.propositions(id));
}
