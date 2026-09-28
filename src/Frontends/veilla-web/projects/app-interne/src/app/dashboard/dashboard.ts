import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DashboardCard } from '@veilla/shared';

/** Tableau de bord interne (conseillers, médecins). États vides tant que le BFF interne n'est pas branché. */
@Component({
  selector: 'app-dashboard',
  imports: [TranslocoDirective, DashboardCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('dashboard.title') }}</h1>
      <p>{{ t('dashboard.intro') }}</p>
      <div class="vl-dashboard">
        <vl-dashboard-card titleKey="dashboard.agenda" />
        <vl-dashboard-card titleKey="dashboard.tasks" />
        <vl-dashboard-card titleKey="dashboard.recent" />
      </div>
    </ng-container>
  `,
})
export class Dashboard {}
