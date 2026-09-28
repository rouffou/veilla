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
        path: 'travailleurs',
        component: PlaceholderPage,
        title: 'nav.workers',
        data: { titleKey: 'nav.workers', requirement: 'POR-03' },
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
      {
        path: 'documents',
        component: PlaceholderPage,
        title: 'nav.documents',
        data: { titleKey: 'nav.documents', requirement: 'POR-06' },
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
