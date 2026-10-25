import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « travailleurs » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `travailleurs` (public/i18n/travailleurs/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `travailleurs.`.
 */
export const TRAVAILLEURS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('travailleurs')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.travailleurs',
        data: { titleKey: 'travailleurs.title' },
      },
    ],
  },
];

export default TRAVAILLEURS_ROUTES;
