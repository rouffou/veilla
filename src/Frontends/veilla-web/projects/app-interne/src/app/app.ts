import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService, filtrerNavigation, Shell, ShellNavItem } from '@veilla/shared';
import { ROLES_PAR_FONCTION } from './roles';
import { GlobalSearch } from './search/global-search';
import { KeyboardShortcuts } from './shortcuts/keyboard-shortcuts';
import { ShortcutsHelp } from './shortcuts/shortcuts-help';

const NAVIGATION: readonly ShellNavItem[] = [
  { path: '/', labelKey: 'nav.dashboard', exact: true },
  { path: '/recherche', labelKey: 'nav.search' },
  { path: '/travail', labelKey: 'nav.travail', roles: ROLES_PAR_FONCTION.travail },
  { path: '/planification', labelKey: 'nav.planification', roles: ROLES_PAR_FONCTION.planification },
  { path: '/consultation', labelKey: 'nav.consultation', roles: ROLES_PAR_FONCTION.consultation },
  { path: '/vaccinations', labelKey: 'nav.vaccinations', roles: ROLES_PAR_FONCTION.vaccinations },
  { path: '/reintegration', labelKey: 'nav.reintegration', roles: ROLES_PAR_FONCTION.reintegration },
  { path: '/travailleurs', labelKey: 'nav.travailleurs', roles: ROLES_PAR_FONCTION.travailleurs },
  { path: '/affilies', labelKey: 'nav.affilies', roles: ROLES_PAR_FONCTION.affilies },
  { path: '/admin', labelKey: 'nav.admin', roles: ROLES_PAR_FONCTION.admin },
];

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Shell, GlobalSearch, ShortcutsHelp],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onKeydown($event)' },
  template: `
    <vl-shell appNameKey="app.name" [navItems]="navItems()">
      <div vlShellHeader class="app-header-tools">
        <app-global-search />
        <app-shortcuts-help />
      </div>
      <router-outlet />
    </vl-shell>
  `,
})
export class App {
  private readonly auth = inject(AuthService);

  /** Navigation filtrée par rôle (matrice de `roles.ts`). */
  protected readonly navItems = computed(() => filtrerNavigation(NAVIGATION, this.auth.roles()));

  private readonly shortcuts = inject(KeyboardShortcuts);
  private readonly search = viewChild.required(GlobalSearch);
  private readonly help = viewChild.required(ShortcutsHelp);

  protected onKeydown(event: KeyboardEvent): void {
    switch (this.shortcuts.resolve(event)) {
      case 'focus-search':
        event.preventDefault();
        this.search().focus();
        break;
      case 'show-help':
        event.preventDefault();
        this.help().open();
        break;
      default:
        break;
    }
  }
}
