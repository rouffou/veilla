import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { filter, skip } from 'rxjs';
import { SkipLink } from '../a11y/skip-link';
import { AuthService } from '../auth/auth.service';
import { LanguageSelector } from '../i18n/language-selector';

export interface ShellNavItem {
  /** Chemin de la route (routerLink). */
  readonly path: string;
  /** Clé de traduction du libellé. */
  readonly labelKey: string;
  /** Correspondance exacte pour l'état actif (accueil). */
  readonly exact?: boolean;
}

export const MAIN_CONTENT_ID = 'contenu-principal';

/**
 * Coquille commune des applications : lien d'évitement, en-tête (banner), navigation
 * principale, contenu principal (main), pied de page (contentinfo).
 * Le focus est déplacé sur le contenu principal après chaque navigation (WCAG 2.4.3).
 */
@Component({
  selector: 'vl-shell',
  imports: [RouterLink, RouterLinkActive, TranslocoDirective, LanguageSelector, SkipLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
})
export class Shell {
  readonly appNameKey = input.required<string>();
  readonly navItems = input<readonly ShellNavItem[]>([]);

  protected readonly auth = inject(AuthService);
  protected readonly mainId = MAIN_CONTENT_ID;
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  constructor() {
    const subscription = inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        skip(1),
      )
      .subscribe(() => this.main().nativeElement.focus({ preventScroll: false }));
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected login(): void {
    this.auth.login();
  }

  protected logout(): void {
    this.auth.logout();
  }
}
