import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { Adresse } from '../api/bff-employeur.models';
import { BffEmployeurService } from '../api/bff-employeur.service';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';
import { AffilieContext } from './affilie-context';
import { EtatAffilie } from './affilie-selector';

/** Fiche de l'affilié (AFF-01 à AFF-03) : identité, sites et contacts en vigueur, en lecture seule. */
@Component({
  selector: 'app-fiche-affilie',
  imports: [TranslocoDirective, DateLocalePipe, EtatAffilie, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('affiliate.title') }}</h1>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        @let etat = fiche.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api
              [erreur]="etat.erreur"
              [reessayable]="true"
              (reessayer)="fiche.recharger()"
            />
          }
          @case ('ok') {
            @let f = etat.valeur;
            <section aria-labelledby="fiche-identite">
              <h2 id="fiche-identite">{{ f.denomination }}</h2>
              <dl class="app-fiche">
                <dt>{{ t('affiliate.bce') }}</dt>
                <dd>{{ f.numeroBce }}</dd>
                <dt>{{ t('affiliate.legalForm') }}</dt>
                <dd>{{ f.formeJuridique }}</dd>
                <dt>{{ t('affiliate.nace') }}</dt>
                <dd>{{ f.codeNace }}</dd>
                <dt>{{ t('affiliate.jointCommittee') }}</dt>
                <dd>{{ f.commissionParitaire }}</dd>
                <dt>{{ t('affiliate.category') }}</dt>
                <dd>{{ f.categorieTarifaire }}</dd>
                <dt>{{ t('affiliate.affiliationDate') }}</dt>
                <dd>{{ f.dateAffiliation | dateLocale: langue() }}</dd>
                @if (f.dateFin) {
                  <dt>{{ t('affiliate.endDate') }}</dt>
                  <dd>{{ f.dateFin | dateLocale: langue() }}</dd>
                }
                <dt>{{ t('affiliate.status') }}</dt>
                <dd>{{ t('enums.statutAffilie.' + f.statut) }}</dd>
              </dl>
            </section>

            <section aria-labelledby="fiche-sites">
              <h2 id="fiche-sites">{{ t('affiliate.sites') }}</h2>
              @if (f.sites.length === 0) {
                <p class="vl-empty">{{ t('affiliate.noSites') }}</p>
              } @else {
                <div class="app-table-wrapper">
                  <table class="app-table">
                    <caption class="vl-visually-hidden">
                      {{
                        t('affiliate.sitesCaption')
                      }}
                    </caption>
                    <thead>
                      <tr>
                        <th scope="col">{{ t('affiliate.siteName') }}</th>
                        <th scope="col">{{ t('affiliate.establishmentUnit') }}</th>
                        <th scope="col">{{ t('affiliate.address') }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (s of f.sites; track s.id) {
                        <tr>
                          <th scope="row">{{ s.nom }}</th>
                          <td>{{ s.uniteEtablissement }} ({{ s.numeroUniteEtablissement }})</td>
                          <td>{{ adresse(s.adresse) }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </section>

            <section aria-labelledby="fiche-contacts">
              <h2 id="fiche-contacts">{{ t('affiliate.contacts') }}</h2>
              @if (f.contacts.length === 0) {
                <p class="vl-empty">{{ t('affiliate.noContacts') }}</p>
              } @else {
                <div class="app-table-wrapper">
                  <table class="app-table">
                    <caption class="vl-visually-hidden">
                      {{
                        t('affiliate.contactsCaption')
                      }}
                    </caption>
                    <thead>
                      <tr>
                        <th scope="col">{{ t('affiliate.contactName') }}</th>
                        <th scope="col">{{ t('affiliate.contactRole') }}</th>
                        <th scope="col">{{ t('affiliate.contactFunction') }}</th>
                        <th scope="col">{{ t('affiliate.contactEmail') }}</th>
                        <th scope="col">{{ t('affiliate.contactPhone') }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (c of f.contacts; track c.id) {
                        <tr>
                          <th scope="row">{{ c.nom }}</th>
                          <td>{{ t('enums.roleContact.' + c.role) }}</td>
                          <td>{{ c.fonction ?? '—' }}</td>
                          <td>
                            @if (c.email) {
                              <a [href]="'mailto:' + c.email">{{ c.email }}</a>
                            } @else {
                              —
                            }
                          </td>
                          <td>{{ c.telephone ?? '—' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </section>
          }
        }
      }
    </ng-container>
  `,
})
export class FicheAffilie {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  protected readonly langue = inject(LanguageService).current;

  protected readonly fiche = parAffilie((id) => this.api.fiche(id));

  protected adresse(a: Adresse): string {
    const rue = [a.rue, a.numero, a.boite ? `bte ${a.boite}` : null].filter(Boolean).join(' ');
    return `${rue}, ${a.codePostal} ${a.localite}${a.codePays && a.codePays !== 'BE' ? ` (${a.codePays})` : ''}`;
  }
}
