import { Routes } from '@angular/router';
import { authGuard, SHARED_PUBLIC_ROUTES } from '@veilla/shared';
import { Dashboard } from './dashboard/dashboard';
import { SearchPage } from './search/search-page';

export const routes: Routes = [
  ...SHARED_PUBLIC_ROUTES,
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', component: Dashboard, title: 'dashboard.title' },
      { path: 'recherche', component: SearchPage, title: 'search.title' },
    ],
  },
  { path: '**', redirectTo: '' },
];
