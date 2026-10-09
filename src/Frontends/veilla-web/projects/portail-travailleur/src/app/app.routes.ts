import { Routes } from '@angular/router';
import { authGuard, SHARED_PUBLIC_ROUTES } from '@veilla/shared';
import { Dashboard } from './dashboard/dashboard';

// Écrans secondaires chargés à la demande (budget du bundle initial).
export const routes: Routes = [
  ...SHARED_PUBLIC_ROUTES,
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', component: Dashboard, title: 'dashboard.title' },
      {
        path: 'rendez-vous',
        loadComponent: () => import('./rendez-vous/rendez-vous-page').then((m) => m.RendezVousPage),
        title: 'nav.appointments',
      },
      {
        path: 'questionnaires',
        loadComponent: () => import('./questionnaires/questionnaires-page').then((m) => m.QuestionnairesPage),
        title: 'nav.questionnaires',
      },
      {
        path: 'demande',
        loadComponent: () => import('./questionnaires/demande-page').then((m) => m.DemandePage),
        title: 'nav.request',
      },
      {
        path: 'documents',
        loadComponent: () => import('./documents/documents-page').then((m) => m.DocumentsPage),
        title: 'nav.documents',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
