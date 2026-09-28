import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DashboardCard } from '@veilla/shared';

/**
 * Tableau de bord employeur (POR-02). Les cartes affichent un état vide tant que le
 * BFF employeur n'est pas branché : aucune donnée fictive n'est présentée.
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
        <vl-dashboard-card titleKey="dashboard.examsDue" requirement="POR-02" />
        <vl-dashboard-card titleKey="dashboard.examsOverdue" requirement="POR-02" />
        <vl-dashboard-card titleKey="dashboard.examsPlanned" requirement="POR-02" />
        <vl-dashboard-card titleKey="dashboard.missions" requirement="POR-02" />
        <vl-dashboard-card titleKey="dashboard.actionPlan" requirement="POR-02" />
        <vl-dashboard-card titleKey="dashboard.unitsBalance" requirement="POR-02" />
      </div>
    </ng-container>
  `,
})
export class Dashboard {}
