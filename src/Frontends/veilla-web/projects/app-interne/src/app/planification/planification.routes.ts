import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « planification » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `planification` (public/i18n/planification/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `planification.`.
 */
export const PLANIFICATION_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('planification')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.planification',
        data: { titleKey: 'planification.title' },
      },
    ],
  },
];

export default PLANIFICATION_ROUTES;
