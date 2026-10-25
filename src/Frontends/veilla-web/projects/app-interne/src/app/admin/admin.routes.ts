import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « admin » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `admin` (public/i18n/admin/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `admin.`.
 */
export const ADMIN_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('admin')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.admin',
        data: { titleKey: 'admin.title' },
      },
    ],
  },
];

export default ADMIN_ROUTES;
