import { Routes } from '@angular/router';
import { authGuard, PlaceholderPage, SHARED_PUBLIC_ROUTES } from '@veilla/shared';
import { Dashboard } from './dashboard/dashboard';

export const routes: Routes = [
  ...SHARED_PUBLIC_ROUTES,
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', component: Dashboard, title: 'dashboard.title' },
      {
        path: 'rendez-vous',
        component: PlaceholderPage,
        title: 'nav.appointments',
        data: { titleKey: 'nav.appointments', requirement: 'POR-11' },
      },
      {
        path: 'questionnaires',
        component: PlaceholderPage,
        title: 'nav.questionnaires',
        data: { titleKey: 'nav.questionnaires', requirement: 'POR-12' },
      },
      {
        path: 'documents',
        component: PlaceholderPage,
        title: 'nav.documents',
        data: { titleKey: 'nav.documents', requirement: 'POR-13' },
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
