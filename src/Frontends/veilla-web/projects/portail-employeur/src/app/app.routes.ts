import { Routes } from '@angular/router';
import { authGuard, PlaceholderPage, SHARED_PUBLIC_ROUTES } from '@veilla/shared';
import { Dashboard } from './dashboard/dashboard';

// Écrans secondaires chargés à la demande : le bundle initial reste sous le budget de performance.
export const routes: Routes = [
  ...SHARED_PUBLIC_ROUTES,
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', component: Dashboard, title: 'dashboard.title' },
      {
        path: 'affiliation',
        loadComponent: () => import('./affilie/fiche-affilie').then((m) => m.FicheAffilie),
        title: 'affiliate.title',
      },
      {
        path: 'travailleurs',
        loadComponent: () =>
          import('./travailleurs/travailleurs-page').then((m) => m.TravailleursPage),
        title: 'workers.title',
      },
      {
        path: 'postes',
        loadComponent: () => import('./postes/postes-page').then((m) => m.PostesPage),
        title: 'positions.title',
      },
      {
        path: 'postes/:posteId/proposition',
        loadComponent: () => import('./postes/proposition-poste').then((m) => m.PropositionPoste),
        title: 'proposal.title',
      },
      {
        path: 'listes-nominatives',
        loadComponent: () => import('./listes/listes-page').then((m) => m.ListesPage),
        title: 'lists.title',
      },
      {
        path: 'listes-nominatives/:listeId/proposition',
        loadComponent: () => import('./listes/proposition-liste').then((m) => m.PropositionListe),
        title: 'listProposal.title',
      },
      {
        path: 'propositions',
        loadComponent: () =>
          import('./propositions/propositions-page').then((m) => m.PropositionsPage),
        title: 'proposals.title',
      },
      {
        path: 'reprises',
        loadComponent: () => import('./reprises/reprises-page').then((m) => m.ReprisesPage),
        title: 'resumptions.title',
      },
      {
        path: 'demandes',
        component: PlaceholderPage,
        title: 'nav.requests',
        data: { titleKey: 'nav.requests', requirement: 'POR-04' },
      },
      {
        path: 'decisions',
        component: PlaceholderPage,
        title: 'nav.decisions',
        data: { titleKey: 'nav.decisions', requirement: 'POR-05' },
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
