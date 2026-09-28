import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DashboardCard } from '@veilla/shared';

/**
 * Accueil du travailleur (POR-11 à POR-13). États vides tant que le BFF travailleur
 * n'est pas branché : aucune donnée fictive n'est présentée.
 */
@Component({
  selector: 'app-dashboard',
  imports: [TranslocoDirective, DashboardCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('dashboard.title') }}</h1>
      <p>{{ t('dashboard.intro') }}</p>
      <div class="vl-dashboard">
        <vl-dashboard-card titleKey="dashboard.appointments" requirement="POR-11" />
        <vl-dashboard-card titleKey="dashboard.questionnaires" requirement="POR-12" />
        <vl-dashboard-card titleKey="dashboard.documents" requirement="POR-13" />
        <vl-dashboard-card
          titleKey="dashboard.consultation"
          descriptionKey="dashboard.consultationText"
          emptyKey="placeholder.notYetAvailable"
          requirement="POR-12"
        />
      </div>
    </ng-container>
  `,
})
export class Dashboard {}
