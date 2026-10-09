import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { DashboardCard, LanguageService } from '@veilla/shared';
import { BffTravailleurService } from '../api/bff-travailleur.service';
import { chargeable } from '../ui/chargeable';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';

/**
 * Accueil du travailleur (POR-11 à POR-13) : écran composite du BFF. Une section dont le service est indisponible est
 * signalée à part ; aucune donnée fictive n'est présentée.
 */
@Component({
  selector: 'app-dashboard',
  imports: [TranslocoDirective, DashboardCard, RouterLink, DateLocalePipe, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('dashboard.title') }}</h1>
      <p>{{ t('dashboard.intro') }}</p>
      @let etat = donnees.etat();
      @if (etat.statut === 'chargement') {
        <app-chargement />
      } @else if (etat.statut === 'erreur') {
        <app-erreur-api [erreur]="etat.erreur" [reessayable]="true" (reessayer)="donnees.recharger()" />
      } @else {
        @let a = etat.valeur;
        <div class="vl-dashboard">
          <vl-dashboard-card titleKey="dashboard.appointments" requirement="POR-11">
            @if (a.prochainsRendezVous === null) {
              <p class="vl-card__empty">{{ t('dashboard.unavailable') }}</p>
            } @else if (a.prochainsRendezVous.length === 0) {
              <p class="vl-card__empty">{{ t('dashboard.noAppointments') }}</p>
            } @else {
              <ul class="app-liste">
                @for (r of a.prochainsRendezVous; track r.id) {
                  <li>
                    <time [attr.datetime]="r.debut">{{ r.debut | dateLocale: langue() : true }}</time>
                    — {{ t('actes.' + r.typeActe) }}
                  </li>
                }
              </ul>
            }
            <p><a routerLink="/rendez-vous">{{ t('nav.appointments') }}</a></p>
          </vl-dashboard-card>
          <vl-dashboard-card titleKey="dashboard.questionnaires" requirement="POR-12">
            @if (a.questionnairesDisponibles === null) {
              <p class="vl-card__empty">{{ t('dashboard.unavailable') }}</p>
            } @else {
              <p>{{ t('dashboard.questionnairesCount', { n: a.questionnairesDisponibles }) }}</p>
            }
            <p><a routerLink="/questionnaires">{{ t('nav.questionnaires') }}</a></p>
          </vl-dashboard-card>
          <vl-dashboard-card titleKey="dashboard.documents" requirement="POR-13">
            @if (a.documentsRecents === null) {
              <p class="vl-card__empty">{{ t('dashboard.unavailable') }}</p>
            } @else if (a.documentsRecents.length === 0) {
              <p class="vl-card__empty">{{ t('dashboard.noDocuments') }}</p>
            } @else {
              <ul class="app-liste">
                @for (d of a.documentsRecents; track d.id) {
                  <li>{{ t('documents.categories.' + d.categorie) }} — {{ d.date | dateLocale: langue() }}</li>
                }
              </ul>
            }
            <p><a routerLink="/documents">{{ t('nav.documents') }}</a></p>
          </vl-dashboard-card>
          <vl-dashboard-card
            titleKey="dashboard.consultation"
            descriptionKey="dashboard.consultationText"
            requirement="POR-12"
          >
            <p><a routerLink="/demande">{{ t('nav.request') }}</a></p>
          </vl-dashboard-card>
        </div>
      }
    </ng-container>
  `,
})
export class Dashboard {
  private readonly api = inject(BffTravailleurService);
  protected readonly langue = inject(LanguageService).current;
  protected readonly donnees = chargeable(() => this.api.accueil());
}
