import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « reintegration » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `reintegration` (public/i18n/reintegration/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `reintegration.`.
 */
export const REINTEGRATION_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('reintegration')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.reintegration',
        data: { titleKey: 'reintegration.title' },
      },
    ],
  },
];

export default REINTEGRATION_ROUTES;
