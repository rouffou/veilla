import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « affilies » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `affilies` (public/i18n/affilies/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `affilies.`.
 */
export const AFFILIES_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('affilies')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.affilies',
        data: { titleKey: 'affilies.title' },
      },
    ],
  },
];

export default AFFILIES_ROUTES;
