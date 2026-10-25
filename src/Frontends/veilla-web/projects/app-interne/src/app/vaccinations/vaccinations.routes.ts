import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « vaccinations » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `vaccinations` (public/i18n/vaccinations/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `vaccinations.`.
 */
export const VACCINATIONS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('vaccinations')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.vaccinations',
        data: { titleKey: 'vaccinations.title' },
      },
    ],
  },
];

export default VACCINATIONS_ROUTES;
