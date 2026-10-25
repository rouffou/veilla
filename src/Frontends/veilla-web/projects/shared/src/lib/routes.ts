import { Routes } from '@angular/router';
import { AccessDeniedPage } from './pages/access-denied-page';
import { AccessibilityPage } from './pages/accessibility-page';
import { AuthErrorPage } from './pages/auth-error-page';

/** Routes publiques communes à toutes les applications (à placer avant la route générique). */
export const SHARED_PUBLIC_ROUTES: Routes = [
  { path: 'accessibilite', component: AccessibilityPage, title: 'a11y.title' },
  { path: 'erreur-connexion', component: AuthErrorPage, title: 'auth.errorTitle' },
  { path: 'acces-refuse', component: AccessDeniedPage, title: 'accessDenied.title' },
];
