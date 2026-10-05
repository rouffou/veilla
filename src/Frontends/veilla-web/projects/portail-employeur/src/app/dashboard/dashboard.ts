import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DashboardCard } from '@veilla/shared';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { Indicateur } from '../api/bff-employeur.models';
import { BffEmployeurService } from '../api/bff-employeur.service';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

/** Libellé de chaque indicateur POR-02 renvoyé par le BFF. */
const TITRES: Record<string, string> = {
  travailleurs: 'dashboard.workers',
  postesActifs: 'dashboard.activePositions',
  postesExposes: 'dashboard.exposedPositions',
  propositionsEnAttente: 'dashboard.pendingProposals',
  examensDus: 'dashboard.examsDue',
  examensEnRetard: 'dashboard.examsOverdue',
  examensPlanifies: 'dashboard.examsPlanned',
  missionsEnCours: 'dashboard.missions',
  mesuresPlanAction: 'dashboard.actionPlan',
  soldeUnites: 'dashboard.unitsBalance',
};

/**
 * Tableau de bord employeur (POR-02). Les compteurs disponibles aujourd'hui (travailleurs, postes,
 * postes exposés, propositions en attente) sont réels ; les autres sont annoncés « bientôt disponibles »
 * par le BFF lui-même : aucune donnée fictive n'est présentée.
 */
@Component({
  selector: 'app-dashboard',
  imports: [TranslocoDirective, DashboardCard, EtatAffilie, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('dashboard.title') }}</h1>
      <p>{{ t('dashboard.intro') }}</p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        @switch (tableau.etat().statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            @if (erreur(); as e) {
              <app-erreur-api [erreur]="e" [reessayable]="true" (reessayer)="tableau.recharger()" />
            }
          }
          @case ('ok') {
            <div class="vl-dashboard">
              @for (i of disponibles(); track i.code) {
                <vl-dashboard-card [titleKey]="titre(i)" requirement="POR-02">
                  @if (i.disponible) {
                    <p class="app-compteur">{{ i.valeur }}</p>
                  } @else {
                    <p class="vl-card__empty">{{ t('dashboard.unavailable') }}</p>
                  }
                </vl-dashboard-card>
              }
              @for (i of aVenir(); track i.code) {
                <vl-dashboard-card
                  [titleKey]="titre(i)"
                  requirement="POR-02"
                  emptyKey="dashboard.comingSoon"
                />
              }
            </div>
          }
        }
      }
    </ng-container>
  `,
})
export class Dashboard {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);

  protected readonly tableau = parAffilie((id) => this.api.tableauDeBord(id));

  private readonly indicateurs = computed(() => {
    const etat = this.tableau.etat();
    return etat.statut === 'ok' ? etat.valeur.indicateurs : [];
  });

  /** Compteurs servis par un service existant (valeur réelle, ou panne momentanée). */
  protected readonly disponibles = computed(() =>
    this.indicateurs().filter((i) => i.raison !== 'fonctionnalite-a-venir'),
  );
  protected readonly aVenir = computed(() =>
    this.indicateurs().filter((i) => i.raison === 'fonctionnalite-a-venir'),
  );
  protected readonly erreur = computed(() => {
    const etat = this.tableau.etat();
    return etat.statut === 'erreur' ? etat.erreur : null;
  });

  protected titre(i: Indicateur): string {
    return TITRES[i.code] ?? i.code;
  }
}
