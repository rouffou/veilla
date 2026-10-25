import { Routes } from '@angular/router';
import { authGuard, roleGuard, SHARED_PUBLIC_ROUTES } from '@veilla/shared';
import { Dashboard } from './dashboard/dashboard';
import { ROLES_PAR_FONCTION } from './roles';
import { SearchPage } from './search/search-page';

/**
 * Chaque fonction est une route paresseuse (`<feature>/<feature>.routes.ts`) protégée par `roleGuard` selon la matrice
 * de `roles.ts`. Les lots A à F remplissent leur dossier ; les chemins `recherche` et `''` restent à la racine.
 */
export const routes: Routes = [
  ...SHARED_PUBLIC_ROUTES,
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', component: Dashboard, title: 'dashboard.title' },
      { path: 'recherche', component: SearchPage, title: 'search.title' },
      {
        path: 'travailleurs',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.travailleurs)],
        loadChildren: () => import('./travailleurs/travailleurs.routes'),
      },
      {
        path: 'affilies',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.affilies)],
        loadChildren: () => import('./affilies/affilies.routes'),
      },
      {
        path: 'planification',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.planification)],
        loadChildren: () => import('./planification/planification.routes'),
      },
      {
        path: 'consultation',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.consultation)],
        loadChildren: () => import('./consultation/consultation.routes'),
      },
      {
        path: 'vaccinations',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.vaccinations)],
        loadChildren: () => import('./vaccinations/vaccinations.routes'),
      },
      {
        path: 'reintegration',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.reintegration)],
        loadChildren: () => import('./reintegration/reintegration.routes'),
      },
      {
        path: 'travail',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.travail)],
        loadChildren: () => import('./travail/travail.routes'),
      },
      {
        path: 'admin',
        canActivate: [roleGuard(...ROLES_PAR_FONCTION.admin)],
        loadChildren: () => import('./admin/admin.routes'),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
