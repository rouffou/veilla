import { Routes } from '@angular/router';
import { PlaceholderPage, provideVeillaScope } from '@veilla/shared';

/**
 * Routes de la fonction « consultation » (chargées à la demande). Page « à venir » en attendant le lot concerné.
 * La portée i18n `consultation` (public/i18n/consultation/{fr,nl,de}.json) n'est chargée qu'à l'entrée de cette route :
 * ses clés sont préfixées par `consultation.`.
 */
export const CONSULTATION_ROUTES: Routes = [
  {
    path: '',
    providers: [provideVeillaScope('consultation')],
    children: [
      {
        path: '',
        component: PlaceholderPage,
        title: 'nav.consultation',
        data: { titleKey: 'consultation.title' },
      },
    ],
  },
];

export default CONSULTATION_ROUTES;
