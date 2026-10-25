import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « travail » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `travail` (public/i18n/travail/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `travail.`.
 */
export const TRAVAIL_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('travail')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.travail',
        data: { titleKey: 'travail.title' },
      },
    ],
  },
];

export default TRAVAIL_ROUTES;
